using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Plans.Rules;
using Selficient.Domain.Primitives;
using Selficient.Domain.Resilience;
using Selficient.Domain.Risks;
using Selficient.Domain.Upkeep;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Plans;

/// <summary>
/// The plan is only offered what the household can do: a tenant is not sold the landlord's roof, a
/// house on a borehole is not told to ring a water company, and a flat on the third floor is not
/// sold something to keep water out of a ground floor.
/// </summary>
public sealed class CircumstancesTests
{
    private static readonly RiskAssessment NoRisks = new RiskAssessmentPipeline([]).Run(Profiles.Flat());
    private static readonly RiskAssessment Flooding = new RiskAssessmentPipeline([new FloodingAssessor()]).Run(Profiles.FloodZoneBungalow());

    private static List<PlanAction> Run(IPlanRule rule, HouseholdProfile profile, RiskAssessment? risks = null) =>
        [.. rule.Contribute(profile, risks ?? NoRisks)];

    private static HouseholdProfile With(HouseholdProfile profile, Tenure tenure = Tenure.Owner, WaterSupply water = WaterSupply.Mains, bool ownDrains = false, bool meter = false, bool help = false, bool cellar = false, bool sleepsDownstairs = false, int floor = 0) =>
        profile with { Circumstances = new Circumstances(tenure, water, ownDrains, meter, help, cellar, sleepsDownstairs, floor) };

    private static HouseholdProfile FloodedTerrace(bool car = false) =>
        Profiles.Build(2, 0, false, false, false, DwellingType.Terrace, 2, HeatingType.GasBoiler, HobType.Gas, car, false, false, false, null, FloodRisk.High) with { Nation = Nation.England };

    private static HouseholdProfile FloodedFlat(int floor, bool car = false) =>
        With(Profiles.Build(1, 0, false, false, false, DwellingType.Flat, 1, HeatingType.Electric, HobType.Electric, car, false, false, false, null, FloodRisk.High) with { Nation = Nation.England }, floor: floor);

    [Fact]
    public void Circumstances_refuse_a_choice_off_the_list_and_a_floor_no_block_has()
    {
        Circumstances.Create(Tenure.Tenant, WaterSupply.OwnPumped, true, true, true, true, true, 40).Value
            .ShouldBe(new Circumstances(Tenure.Tenant, WaterSupply.OwnPumped, true, true, true, true, true, 40));
        Circumstances.Create((Tenure)9, WaterSupply.Mains, false, false, false, false, false, 0).Error.Code.ShouldBe(DomainErrors.UnknownChoice().Code);
        Circumstances.Create(Tenure.Owner, (WaterSupply)9, false, false, false, false, false, 0).Error.Code.ShouldBe(DomainErrors.UnknownChoice().Code);
        Circumstances.Create(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, -1).Error.Code.ShouldBe(DomainErrors.FloorOutOfRange().Code);
        Circumstances.Create(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 41).IsFailure.ShouldBeTrue();
        Circumstances.None.ShouldBe(new Circumstances(Tenure.Owner, WaterSupply.Mains, false, false, false, false, false, 0));
        Profiles.RobsHouse().Circumstances.ShouldBe(Circumstances.None, "a plan made before the questions is an owner on the mains");
        ResilienceProfile.MaxFloor.ShouldBe(Circumstances.MaxFloor);
    }

    [Fact]
    public void A_profile_knows_who_is_above_the_water_and_who_sleeps_where_it_arrives()
    {
        With(Profiles.Flat(), floor: 2).IsAboveGround.ShouldBeTrue();
        With(Profiles.Flat(), floor: 0).IsAboveGround.ShouldBeFalse("a ground-floor flat floods like a house");
        With(Profiles.RobsHouse(), floor: 2).IsAboveGround.ShouldBeFalse("a floor is only asked of a flat");

        With(Profiles.Flat(), floor: 0).SleepsAtGroundLevel.ShouldBeTrue("a home on one floor has every bed on it");
        With(Profiles.Flat(), floor: 2, sleepsDownstairs: true).SleepsAtGroundLevel.ShouldBeFalse("the second floor is not the ground");
        Profiles.RobsHouse().SleepsAtGroundLevel.ShouldBeFalse();
        With(Profiles.RobsHouse(), sleepsDownstairs: true).SleepsAtGroundLevel.ShouldBeTrue();

        With(Profiles.RobsHouse(), tenure: Tenure.Tenant).IsTenant.ShouldBeTrue();
        Profiles.RobsHouse().IsTenant.ShouldBeFalse();
        With(Profiles.RobsHouse(), water: WaterSupply.OwnGravity).HasOwnWater.ShouldBeTrue();
        With(Profiles.RobsHouse(), water: WaterSupply.OwnPumped).HasOwnWater.ShouldBeTrue();
        Profiles.RobsHouse().HasOwnWater.ShouldBeFalse();

        Profiles.Flat().QualifiesForPriorityServices.ShouldBeFalse();
        With(Profiles.Flat(), help: true).QualifiesForPriorityServices.ShouldBeTrue("someone who cannot get out alone is who the registers are for");
        Profiles.RobsHouse().QualifiesForPriorityServices.ShouldBeTrue("children qualify");
    }

    [Fact]
    public void A_tenant_is_not_offered_what_only_the_owner_can_do()
    {
        HouseholdProfile tenant = With(Profiles.RobsHouse(), tenure: Tenure.Tenant);

        Run(new SolarQuotesRule(), tenant).ShouldBeEmpty("the roof is the landlord's");

        PlanAction letter = Run(new TallTreeRule(), tenant).ShouldHaveSingleItem();
        letter.Id.Value.ShouldBe("tree-tell-landlord");
        letter.Tier.ShouldBe(Tier.ThisWeek);
        letter.Title.ShouldBe("Tell your landlord in writing about the tall tree");
        letter.Why.ShouldBe("The tree is your landlord's to have surveyed and made safe, and a dated letter or email puts it on record that they were told. Ask for an Arboricultural Association approved contractor to look at it, and keep a copy with your tenancy papers.");
        letter.Cost.ShouldBe(MoneyRange.Free);
        letter.Mitigates.ShouldBe([RiskKind.FallingTree]);
        Run(new TallTreeRule(), With(Profiles.Flat(), tenure: Tenure.Tenant)).ShouldBeEmpty("no tree, no letter");

        List<PlanAction> power = Run(new PowerStationRule(), tenant);
        power[0].Why.ShouldBe("Runs the fridge-freezer, the router and the lights through a cut, and acts as a UPS. It needs nothing fitted and goes with you when you move, which makes it the backup a rented home can have: buy the 2 kWh size if you can.");
        power[0].Why.ShouldNotContain("rooftop solar");
        power[1].Id.Value.ShouldBe("boiler-plug-and-socket");
        power[1].Title.ShouldBe("Ask your landlord to have an electrician fit a plug-and-socket on the boiler's spur");
        power[1].Why.ShouldEndWith("never by backfeeding a wall socket. The boiler is your landlord's, so get their agreement in writing first; some will pay for it.");
        power[1].Cost.ShouldBe(MoneyRange.Pounds(0m, 200m));

        List<PlanAction> owner = Run(new PowerStationRule(), Profiles.RobsHouse());
        owner[0].Why.ShouldEndWith("Buy the 1 kWh size if rooftop solar with backup is coming within a year; otherwise the 2 kWh size.");
        owner[1].Title.ShouldBe("Have an electrician fit a plug-and-socket on the boiler's spur");
        owner[1].Why.ShouldEndWith("never by backfeeding a wall socket.");
        owner[1].Cost.ShouldBe(MoneyRange.Pounds(100m, 200m));

        Run(new WaterStoreRule(), tenant).Single(a => a.Id.Value == "water-butt").Why.ShouldBe("For flushing and washing when the mains is off. Never drink it untreated. Ask your landlord before a diverter is cut into the downpipe.");
        Run(new WaterStoreRule(), Profiles.RobsHouse()).Single(a => a.Id.Value == "water-butt").Why.ShouldBe("For flushing and washing when the mains is off. Never drink it untreated.");
    }

    [Fact]
    public void A_home_on_its_own_water_has_no_water_company_and_is_told_what_its_pump_does()
    {
        HouseholdProfile pumped = With(Profiles.RobsHouse(), water: WaterSupply.OwnPumped);
        HouseholdProfile gravity = With(Profiles.RobsHouse(), water: WaterSupply.OwnGravity);

        PlanAction pump = Run(new OwnSuppliesRule(), pumped).ShouldHaveSingleItem();
        pump.Id.Value.ShouldBe("own-water-in-a-power-cut");
        pump.Tier.ShouldBe(Tier.ThisMonth);
        pump.Title.ShouldBe("Find out what your water supply does in a power cut");
        pump.Why.ShouldBe("A borehole or well pump stops with the electricity, and so does an ultraviolet filter, so in a cut you have what is in the pressure vessel and what you have stored. Run a tap with the pump switched off to see how much that is, and ask whoever services the pump what could run it. Your stored water matters more than it does on the mains.");
        pump.Cost.ShouldBe(MoneyRange.Free);
        Run(new OwnSuppliesRule(), gravity).ShouldBeEmpty("nothing stops a spring in a power cut");
        Run(new OwnSuppliesRule(), Profiles.RobsHouse()).ShouldBeEmpty();

        Run(new BaselineChecklistRule(), pumped).Single(a => a.Id.Value == "save-emergency-numbers").Title.ShouldBe("Save 105, whoever looks after your water supply and the weather warnings on every phone");
        Run(new BaselineChecklistRule(), Profiles.RobsHouse()).Single(a => a.Id.Value == "save-emergency-numbers").Title.ShouldBe("Save 105, your water company and the weather warnings on every phone");

        PlanAction register = Run(new PriorityServicesRule(), gravity).ShouldHaveSingleItem();
        register.Title.ShouldBe("Register with your electricity network's Priority Services Register");
        register.Why.ShouldBe("It costs nothing and brings earlier reconnection, a call when the power is off and advance notice of planned work. Households with children or anyone with a medical or mobility need qualify.");
        register.Why.ShouldNotContain("doorstep water", customMessage: "nobody brings water to a private supply");
        Run(new PriorityServicesRule(), Profiles.RobsHouse()).Single().Title.ShouldBe("Register with your electricity and water companies' Priority Services Registers");
        Run(new PriorityServicesRule(), With(Profiles.Flat(), help: true)).ShouldHaveSingleItem("help to leave is a reason to register");

        Run(new WaterStoreRule(), pumped).Single(a => a.Id.Value == "water-butt").Why.ShouldStartWith("For flushing and washing when the supply is off.");

        Risk pumpedRisk = new WaterInterruptionAssessor().Assess(pumped)!;
        pumpedRisk.Rationale.ShouldBe("Your water is lifted by an electric pump, so every power cut is a water cut as well. No water company brings bottled water to a private supply, so what you have stored is what you have.");
        (pumpedRisk.Likelihood, pumpedRisk.Impact).ShouldBe((Likelihood.High, Impact.High));
        Risk gravityRisk = new WaterInterruptionAssessor().Assess(gravity)!;
        gravityRisk.Rationale.ShouldBe("Your own supply runs without a pump, so a power cut does not stop it, but a hard frost, a dry summer or a fouled source can. No water company brings bottled water to a private supply, so what you have stored is what you have.");
        (gravityRisk.Likelihood, gravityRisk.Impact).ShouldBe((Likelihood.Medium, Impact.Medium));
        new WaterInterruptionAssessor().Assess(Profiles.RobsHouse())!.Rationale.ShouldStartWith("A burst main or a treatment problem");
    }

    [Fact]
    public void A_home_with_its_own_tank_is_not_sold_bags_for_when_the_sewers_stop()
    {
        HouseholdProfile tank = With(Profiles.RobsHouse(), ownDrains: true);

        List<PlanAction> baseline = Run(new BaselineChecklistRule(), tank);
        baseline.Count.ShouldBe(11);
        baseline.ShouldNotContain(a => a.Id.Value == "no-flush-toilet-kit");

        PlanAction drains = Run(new OwnSuppliesRule(), tank).ShouldHaveSingleItem();
        drains.Id.Value.ShouldBe("own-drains-in-a-power-cut");
        drains.Tier.ShouldBe(Tier.ThisMonth);
        drains.Title.ShouldBe("Find out whether your tank or treatment plant needs power");
        drains.Why.ShouldBe("A septic tank that drains by gravity carries on through a power cut and does not depend on the sewers, so you can flush with stored water when the taps are dry. A treatment plant or a pumped system stops with the electricity and can only take so much: ask whoever empties it how long, and flush as little as you can during a cut.");
        drains.Cost.ShouldBe(MoneyRange.Free);

        Run(new OwnSuppliesRule(), With(Profiles.RobsHouse(), water: WaterSupply.OwnPumped, ownDrains: true)).Select(a => a.Id.Value).ShouldBe(["own-water-in-a-power-cut", "own-drains-in-a-power-cut"]);
    }

    [Fact]
    public void A_prepayment_meter_is_kept_in_credit_ahead_of_the_weather()
    {
        PlanAction credit = Run(new PrepaymentRule(), With(Profiles.Flat(), meter: true)).ShouldHaveSingleItem();

        credit.Id.Value.ShouldBe("prepayment-credit-ahead");
        credit.Tier.ShouldBe(Tier.ThisWeek);
        credit.Title.ShouldBe("Keep a week of credit on the prepayment meter through the winter");
        credit.Why.ShouldBe("A prepayment meter goes off when the credit runs out, and in a wide cut the shops and the app that top it up stop too, so the power can come back to a meter with nothing on it. Top up when a storm is forecast. Your supplier has to offer emergency credit, and more help if anyone here is ill, elderly or very young: find out how to switch it on before you need it.");
        credit.Cost.ShouldBe(MoneyRange.Free);
        credit.Mitigates.ShouldBe([RiskKind.StormPowerCut, RiskKind.RegionalDisruption], ignoreOrder: true);
        Run(new PrepaymentRule(), Profiles.Flat()).ShouldBeEmpty();
    }

    [Fact]
    public void Someone_who_cannot_get_out_alone_gets_a_plan_for_it_and_a_flat_is_told_about_the_lift()
    {
        PlanAction house = Run(new HelpToLeaveRule(), With(Profiles.RobsHouse(), help: true)).ShouldHaveSingleItem();
        PlanAction flat = Run(new HelpToLeaveRule(), With(Profiles.Flat(), help: true, floor: 5)).Single();

        house.Id.Value.ShouldBe("help-to-leave-plan");
        house.Tier.ShouldBe(Tier.ThisWeek);
        house.Title.ShouldBe("Agree who helps whom to get out, and walk it through once");
        house.Why.ShouldBe("Someone here would need help to get downstairs or out of the home. Decide who helps, by which way, and where the chair, the frame and the medicines are kept at night. Your fire and rescue service will visit free of charge to plan it with you: ask for a home fire safety visit.");
        house.Cost.ShouldBe(MoneyRange.Free);
        flat.Why.ShouldContain("kept at night. A lift stops with the power, so the plan has to work on the stairs. Your fire and rescue service");
        Run(new HelpToLeaveRule(), With(Profiles.Flat(), help: true)).Single().Why.ShouldNotContain("lift", customMessage: "a ground-floor flat has no lift to lose");
        Run(new HelpToLeaveRule(), Profiles.RobsHouse()).ShouldBeEmpty();
    }

    [Fact]
    public void A_flood_plan_is_written_for_the_floors_and_the_people_this_home_has()
    {
        static string Why(HouseholdProfile profile) => Run(new FloodRule(), profile, Flooding)[0].Why;
        const string Rest = " Warnings come by phone call, text and email. Check that your buildings and contents insurance covers flood.";

        Why(FloodedTerrace()).ShouldBe("Know where the stopcock and the fuse board are, and what goes upstairs first." + Rest);
        Why(With(FloodedTerrace(), cellar: true)).ShouldBe("Know where the stopcock and the fuse board are, and what goes upstairs first. A cellar fills first, often up through its own floor: keep nothing down there you would miss, and never go down into flood water." + Rest);
        Why(With(FloodedTerrace(), sleepsDownstairs: true)).ShouldBe("Know where the stopcock and the fuse board are, and what goes upstairs first. Someone sleeps downstairs, so agree where they sleep on a night with a warning out." + Rest);
        Why(With(FloodedTerrace(), ownDrains: true)).ShouldBe("Know where the stopcock and the fuse board are, and what goes upstairs first. A tank in flooded ground cannot drain, so the toilets may not flush until the water has gone." + Rest);
        Why(With(FloodedTerrace(), help: true)).ShouldBe("Know where the stopcock and the fuse board are, and what goes upstairs first. Someone here needs help to leave, so go when the warning comes and not when the water does." + Rest);
        Why(Profiles.FloodZoneBungalow() with { Nation = Nation.England }).ShouldBe("Know where the stopcock and the fuse board are, where you would move the car, and what is lifted off the floor first. There is no upstairs to go to, so decide now where you would go and when." + Rest);

        // A rented home insures its contents; the building is someone else's.
        Why(With(FloodedTerrace(), tenure: Tenure.Tenant)).ShouldEndWith("Warnings come by phone call, text and email. Check that your contents insurance covers flood; the building is your landlord's to insure.");

        // A flat above the ground plans for being cut off, not for water in the rooms.
        Why(FloodedFlat(3)).ShouldBe("The water will not reach your floor, but it can take the power, the lift and the way out for days. Decide now when you would leave." + Rest);
        Why(FloodedFlat(3, car: true)).ShouldStartWith("The water will not reach your floor, but it can take the power, the lift and the way out for days. Decide now when you would leave, where you would move the car.");
        Why(With(FloodedFlat(3), help: true, floor: 3)).ShouldStartWith("The water will not reach your floor, but it can take the power, the lift and the way out for days. Decide now when you would leave. Someone here needs help to leave, so go when the warning comes and not when the water does.");
        Why(FloodedFlat(0)).ShouldStartWith("Know where the stopcock and the fuse board are, and what is lifted off the floor first. There is no upstairs to go to");
    }

    [Fact]
    public void Nothing_that_keeps_water_out_of_a_ground_floor_is_sold_to_a_flat_above_it()
    {
        Run(new FloodRule(), FloodedFlat(3), Flooding).Select(a => a.Id.Value).ShouldBe(["flood-warnings-and-plan"]);
        Run(new FloodRule(), FloodedFlat(0), Flooding).Select(a => a.Id.Value).ShouldBe(["flood-warnings-and-plan", "flood-protection-kit", "flood-water-alarm"]);
        Run(new FloodRule(), FloodedTerrace(), Flooding).Select(a => a.Id.Value).ShouldBe(["flood-warnings-and-plan", "flood-protection-kit"], "beds upstairs and no cellar: nothing an alarm would tell them first");

        PlanAction kit = Run(new FloodRule(), With(FloodedTerrace(), tenure: Tenure.Tenant), Flooding)[1];
        kit.Why.ShouldBe("Simple products that keep shallow water out of the ground floor for long enough to matter. Fit the air-brick covers before the warning, not during it. Ask your landlord before anything is fixed to a door frame; the absorbent barriers need no fixing.");
        Run(new FloodRule(), FloodedTerrace(), Flooding)[1].Why.ShouldEndWith("not during it.");
    }

    [Fact]
    public void A_water_alarm_goes_where_the_water_arrives_before_anyone_knows()
    {
        PlanAction cellar = Run(new FloodRule(), With(FloodedTerrace(), cellar: true, sleepsDownstairs: true), Flooding)[2];
        PlanAction bed = Run(new FloodRule(), With(FloodedTerrace(), sleepsDownstairs: true), Flooding)[2];

        cellar.Id.Value.ShouldBe("flood-water-alarm");
        cellar.Tier.ShouldBe(Tier.ThisMonth);
        cellar.Title.ShouldBe("A battery water alarm on the cellar floor");
        cellar.Why.ShouldBe("It sounds the moment water touches it. A cellar fills first and nobody is down there to see it, so the alarm is the hour you would not otherwise have. Test it when you test the smoke alarms.");
        cellar.Cost.ShouldBe(MoneyRange.Pounds(8m, 25m));
        cellar.Mitigates.ShouldBe([RiskKind.Flooding]);
        bed.Title.ShouldBe("A battery water alarm on the floor by the outside door");
        bed.Why.ShouldBe("It sounds the moment water touches it. Someone here sleeps on the floor the water reaches first, and surface water comes at night with no warning call, so the alarm is what wakes you. Test it when you test the smoke alarms.");
        Run(new FloodRule(), With(FloodedTerrace(), cellar: true), Flooding)[2].Title.ShouldBe("A battery water alarm on the cellar floor");
    }

    [Fact]
    public void The_new_rules_reject_a_missing_profile()
    {
        foreach (IPlanRule rule in new IPlanRule[] { new HelpToLeaveRule(), new PrepaymentRule(), new OwnSuppliesRule() })
        {
            Should.Throw<ArgumentNullException>(() => rule.Contribute(null!, NoRisks).ToList());
        }
    }

    [Fact]
    public void Upkeep_follows_the_meter_the_supply_and_the_tank()
    {
        Reminder meter = UpkeepSchedule.For(new UpkeepFacts(PrepaymentMeter: true)).Single(r => r.Id == "prepayment-winter-credit");
        meter.Title.ShouldBe("Build up credit on the prepayment meter before winter");
        meter.Why.ShouldBe("A week or more ahead through the cold months, so a storm that shuts the shops and the app does not leave the meter empty when the power comes back.");
        meter.Dates.ShouldBe([(11, 1)]);

        Reminder test = UpkeepSchedule.For(new UpkeepFacts(OwnWater: true)).Single(r => r.Id == "own-water-test");
        test.Title.ShouldBe("Have your water supply tested");
        test.Why.ShouldBe("A private supply has nobody testing it but you. Your council's environmental health team will sample it for a fee, and a year is the longest to leave it.");
        test.Dates.ShouldBe([(5, 1)]);

        Reminder tank = UpkeepSchedule.For(new UpkeepFacts(OwnDrains: true)).Single(r => r.Id == "tank-emptying");
        tank.Title.ShouldBe("Check when the tank was last emptied");
        tank.Why.ShouldBe("A full tank backs up at the worst time. Book the tanker before winter, while it can still reach you.");
        tank.Dates.ShouldBe([(9, 1)]);

        UpkeepSchedule.For(new UpkeepFacts()).Select(r => r.Id).ShouldNotContain("prepayment-winter-credit");
        UpkeepSchedule.For(new UpkeepFacts()).Select(r => r.Id).ShouldNotContain("own-water-test");
        UpkeepSchedule.For(new UpkeepFacts()).Select(r => r.Id).ShouldNotContain("tank-emptying");
        UpkeepFacts.From(With(Profiles.Flat(), water: WaterSupply.OwnGravity, ownDrains: true, meter: true)).ShouldBe(new UpkeepFacts(PrepaymentMeter: true, OwnWater: true, OwnDrains: true));
    }

    private static Board Simulate(HouseholdProfile profile, Scenario scenario, params string[] held) =>
        OutageBoard.Simulate(ResilienceProfile.FromProfile(profile), held.ToHashSet(StringComparer.Ordinal), scenario, Season.Winter);

    private static BoardRow Row(Board board, NeedId need) => board.Row(need.ToString())!;

    [Fact]
    public void A_pumped_supply_stops_with_the_power_and_a_spring_does_not()
    {
        HouseholdProfile pumped = With(Profiles.RobsHouse(), water: WaterSupply.OwnPumped);
        HouseholdProfile gravity = With(Profiles.RobsHouse(), water: WaterSupply.OwnGravity);

        ResilienceProfile.FromProfile(pumped).Fixtures.ShouldContain("own-water-pumped");
        ResilienceProfile.FromProfile(pumped).Fixtures.ShouldNotContain("mains-tap");
        ResilienceProfile.FromProfile(gravity).Fixtures.ShouldContain("own-water-gravity");
        ResilienceProfile.FromProfile(Profiles.RobsHouse()).Fixtures.ShouldContain("mains-tap");

        Row(Simulate(Profiles.RobsHouse(), Scenarios.Storm), NeedId.DrinkingWater).Days[1].ShouldBe(new DayState(CellState.Held, "mains-tap", null), "the mains stay on in a storm");
        Row(Simulate(pumped, Scenarios.Storm), NeedId.DrinkingWater).Days[1].ShouldBe(new DayState(CellState.Lost, null, "own-water-pumped"), "the pump has no power and the kettle was emptied on the first day");
        Row(Simulate(gravity, Scenarios.Storm), NeedId.DrinkingWater).Days[1].ShouldBe(new DayState(CellState.Held, "own-water-gravity", null));

        // The boiler's plug frees the boiler; it does not put a wired-in pump on a portable battery.
        Row(Simulate(pumped, Scenarios.Storm, "portable-power-station", "boiler-plug-and-socket"), NeedId.DrinkingWater).Days[1].State.ShouldBe(CellState.Lost);
        Row(Simulate(pumped, Scenarios.Storm, "rooftop-solar-and-battery"), NeedId.DrinkingWater).Days[1].ShouldBe(new DayState(CellState.Held, "own-water-pumped", null), "a whole-house battery lifts the water");

        // A burst main is somebody else's problem.
        Row(Simulate(pumped, Scenarios.WaterCut), NeedId.DrinkingWater).Days[0].ShouldBe(new DayState(CellState.Held, "own-water-pumped", null));
        Row(Simulate(gravity, Scenarios.WaterCut), NeedId.Laundry).Cells.ShouldAllBe(cell => cell.State == CellState.Held, "the washing machine has its water");
    }

    [Fact]
    public void A_tank_of_your_own_still_takes_a_flush_when_the_sewers_have_stopped()
    {
        HouseholdProfile sewer = Profiles.RobsHouse();
        HouseholdProfile tank = With(Profiles.RobsHouse(), ownDrains: true);

        Row(Simulate(sewer, Scenarios.Blackout, "bath-liner"), NeedId.Sanitation).Days[1].ShouldBe(new DayState(CellState.Reduced, "bin-bags", "mains-tap"), "a bath of water and nowhere for a flush to go");
        Row(Simulate(tank, Scenarios.Blackout, "bath-liner"), NeedId.Sanitation).Days[1].State.ShouldBe(CellState.Held, "the tank is in the garden, not at the end of a sewer");
    }
}
