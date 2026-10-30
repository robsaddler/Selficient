using Selficient.Domain.Households;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Resilience;

public sealed class ContentTests
{
    [Fact]
    public void Every_need_has_a_rung_a_name_and_a_unit_and_can_be_looked_up()
    {
        Needs.All.Select(need => need.Id).ShouldBe(Enum.GetValues<NeedId>(), ignoreOrder: true);
        Needs.All.ShouldAllBe(need => need.Name.Length > 0 && need.Unit.Length > 0);
        Needs.Of(NeedId.ColdStorage).Rung.ShouldBe(Rung.House);
        Needs.All.Select(need => need.Rung).ShouldBe(Needs.All.Select(need => need.Rung).OrderBy(rung => rung), "needs are listed rung by rung from Survive down");
    }

    [Fact]
    public void Scenarios_are_distinct_bounded_and_ordered()
    {
        Scenarios.All.Count.ShouldBe(7);
        Scenarios.All.Select(scenario => scenario.Id).ShouldBeUnique();
        foreach (Scenario scenario in Scenarios.All)
        {
            scenario.Phases.ShouldAllBe(phase => phase.Availability >= 0m && phase.Availability <= 1m && phase.FromDay >= 1 && phase.ToDay >= phase.FromDay);
            scenario.Horizons.ToList().ShouldBe(scenario.Horizons.OrderBy(horizon => horizon).ToList(), "horizons run short to long");
            scenario.LongestHorizon.ShouldBe(scenario.Horizons[^1]);
            scenario.Sentence.ShouldEndWith(".");
        }

        Scenarios.Find("storm").ShouldBe(Scenarios.Storm);
        Scenarios.Find("nothing").ShouldBeNull();
        Scenarios.Storm.Risk.ShouldBe(RiskKind.StormPowerCut);
        Scenarios.Heatwave.Risk.ShouldBeNull();
        Scenarios.Heatwave.DefaultSeason.ShouldBe(Season.Summer);
    }

    [Fact]
    public void Availability_takes_the_worst_phase_covering_the_day()
    {
        Scenarios.Storm.Availability(Utility.MobileNetwork, 1).ShouldBe(0.5m);
        Scenarios.Storm.Availability(Utility.MobileNetwork, 2).ShouldBe(0m);
        Scenarios.Storm.Availability(Utility.MainsWater, 5).ShouldBe(1m, "water stays on in a storm");
        Scenarios.Shortages.Availability(Utility.Electricity, 13).ShouldBe(1m);
        Scenarios.Shortages.Availability(Utility.Electricity, 14).ShouldBe(0.83m);

        Scenario overlapping = Scenarios.Storm with { Phases = [new(Utility.Shops, 1, 10, 0.6m), new(Utility.Shops, 1, 10, 0.2m), new(Utility.Shops, 1, 10, 0.9m)] };
        overlapping.Availability(Utility.Shops, 3).ShouldBe(0.2m);
        overlapping.Availability(Utility.Shops, 11).ShouldBe(1m);
    }

    [Fact]
    public void Providers_are_distinct_sourced_and_consistent()
    {
        Providers.All.Select(provider => provider.Id).ShouldBeUnique();
        foreach (Provider provider in Providers.All)
        {
            provider.Source.Length.ShouldBeGreaterThan(0, provider.Id);
            provider.Sentence.ShouldEndWith(".", customMessage: provider.Id);
            ((provider.Stock is not null ? 1 : 0) + (provider.Store is not null ? 1 : 0) + (provider.Load is not null ? 1 : 0))
                .ShouldBeLessThanOrEqualTo(2, $"{provider.Id} is one kind of thing");
            if (provider.Load is { } load)
            {
                load.WinterWhPerDay.ShouldBeGreaterThan(0m, provider.Id);
                load.SummerWhPerDay.ShouldBeGreaterThan(0m, provider.Id);
                load.BurstW.ShouldBeGreaterThan(0m, provider.Id);
            }

            if (provider.Store is { } store)
            {
                store.UsableWh.ShouldBeGreaterThan(store.ReserveWh, provider.Id);
                store.CeilingW.ShouldBeGreaterThan(0m, provider.Id);
                provider.Meets.ShouldBeEmpty($"{provider.Id} carries loads rather than meeting a need itself");
            }

            foreach (Provision provision in provider.Meets)
            {
                provision.PerDay.ShouldBeGreaterThanOrEqualTo(0m);
                if (provision.RequiresAnyProvider is { } any)
                {
                    any.ShouldAllBe(id => Providers.Find(id) != null, $"{provider.Id} requires a provider that exists");
                }
            }
        }

        Providers.Find("gas-boiler")!.For(NeedId.HotWater).ShouldNotBeNull();
        Providers.Find("gas-boiler")!.For(NeedId.Food).ShouldBeNull();
        Providers.Find("nothing").ShouldBeNull();
    }

    [Fact]
    public void Every_provider_named_after_a_plan_action_is_a_real_action()
    {
        Selficient.Domain.Risks.RiskAssessmentPipeline risks = new(Selficient.Application.Composition.ApplicationServiceCollectionExtensions.Assessors());
        PlanBuilder builder = new(Selficient.Application.Composition.ApplicationServiceCollectionExtensions.Rules());
        HashSet<string> actionIds = new[] { Profiles.RobsHouse(), Profiles.Flat(), Profiles.FloodZoneBungalow() }
            .SelectMany(profile => builder.Build(profile, risks.Run(profile)).Value.Tiers.SelectMany(tier => tier.Actions))
            .Select(action => action.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        string[] soldByThePlan = ["water-bottled-three-days", "water-cans", "bath-liner", "food-no-cook-three-days", "food-one-month", "butane-cartridge-stove", "cash-in-small-notes", "heating-fallback", "power-failure-lights", "headtorches", "rechargeable-lanterns", "candles-and-matches", "radio-dab-fm-with-crank", "power-banks", "portable-power-station", "rooftop-solar-and-battery", "boiler-plug-and-socket", "satellite-internet-on-standby", "car-above-half-a-tank", "gravity-water-filter"];

        foreach (string id in soldByThePlan)
        {
            Providers.Find(id).ShouldNotBeNull(id);
            actionIds.ShouldContain(id, $"{id} is sold by the plan for at least one of the three houses");
        }
    }

    [Fact]
    public void Record_helpers_read_the_season()
    {
        Load load = new(1200m, 300m, 150m);
        Flow flow = new(0.3m, 1.3m, 90);
        EnergyStore store = new(13500m, 0.9m, 5000m, 3000m, 20000m, RefillSource.Roof, 0m, WholeHouse: true);

        load.WhPerDay(Season.Winter).ShouldBe(1200m);
        load.WhPerDay(Season.Summer).ShouldBe(300m);
        flow.PerDay(Season.Winter).ShouldBe(0.3m);
        flow.PerDay(Season.Summer).ShouldBe(1.3m);
        store.UsableWh.ShouldBe(12150m);
        store.RefillWh(Season.Winter).ShouldBe(3000m);
        store.RefillWh(Season.Summer).ShouldBe(20000m);
        UtilityPhase.ToEnd.ShouldBe(int.MaxValue);
        DayState.NotNeeded.State.ShouldBe(CellState.Held);
    }

    [Fact]
    public void Demand_follows_the_household_the_season_and_the_weather()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        ResilienceProfile pets = ResilienceProfile.FromProfile(Profiles.FloodZoneBungalow());
        ResilienceProfile flat = ResilienceProfile.FromProfile(Profiles.Flat());
        ResilienceProfile freezerAndBaby = ResilienceProfile.Create(Profiles.RobsHouse(), true, false, false, Growing.None, CarKind.Petrol, 1, 7, 0, []).Value;

        Demand.For(rob, NeedId.DrinkingWater, Scenarios.Storm, Season.Winter, 1).ShouldBe(12m);
        Demand.For(rob, NeedId.DrinkingWater, Scenarios.Heatwave, Season.Summer, 1).ShouldBe(16m, "an extra litre each in a heatwave");
        Demand.For(pets, NeedId.DrinkingWater, Scenarios.Storm, Season.Winter, 1).ShouldBe(8m, "two people and the dog");
        Demand.For(rob, NeedId.Food, Scenarios.Storm, Season.Winter, 1).ShouldBe(4m);
        Demand.For(rob, NeedId.Warmth, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Warmth, Scenarios.Storm, Season.Summer, 1).ShouldBe(0m);
        Demand.For(rob, NeedId.Warmth, Scenarios.Heatwave, Season.Winter, 1).ShouldBe(0m, "no heating in a heatwave whatever the calendar says");
        Demand.For(rob, NeedId.Health, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Dependants, Scenarios.Storm, Season.Winter, 1).ShouldBe(0m);
        Demand.For(pets, NeedId.Dependants, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(freezerAndBaby, NeedId.Dependants, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Light, Scenarios.Storm, Season.Winter, 1).ShouldBe(8m);
        Demand.For(rob, NeedId.Light, Scenarios.Storm, Season.Summer, 1).ShouldBe(4m);
        Demand.For(rob, NeedId.Sanitation, Scenarios.Storm, Season.Winter, 1).ShouldBe(48m);
        Demand.For(rob, NeedId.ColdStorage, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(freezerAndBaby, NeedId.ColdStorage, Scenarios.Storm, Season.Winter, 1).ShouldBe(2m, "two cold boxes to keep");
        Demand.For(rob, NeedId.HeatingSystem, Scenarios.Storm, Season.Summer, 1).ShouldBe(0m);
        Demand.For(rob, NeedId.HeatingSystem, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Mobility, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(flat, NeedId.Mobility, Scenarios.Storm, Season.Winter, 1).ShouldBe(0m);
        Demand.For(rob, NeedId.WorkingFromHome, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(flat, NeedId.WorkingFromHome, Scenarios.Storm, Season.Winter, 1).ShouldBe(0m);
        Demand.For(rob, NeedId.Laundry, Scenarios.Storm, Season.Winter, 7).ShouldBe(0m);
        Demand.For(rob, NeedId.Laundry, Scenarios.Storm, Season.Winter, 8).ShouldBe(1m);
        Demand.For(rob, NeedId.Entertainment, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Information, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Contact, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Cooking, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Cash, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.HotWater, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Demand.For(rob, NeedId.Internet, Scenarios.Storm, Season.Winter, 1).ShouldBe(1m);
        Should.Throw<ArgumentNullException>(() => Demand.For(null!, NeedId.Food, Scenarios.Storm, Season.Winter, 1));
        Should.Throw<ArgumentNullException>(() => Demand.For(rob, NeedId.Food, null!, Season.Winter, 1));
    }
}
