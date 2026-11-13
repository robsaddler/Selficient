using System.Globalization;
using Selficient.Domain.Measures;
using Selficient.Domain.Parameters;

namespace Selficient.Domain.Resilience;

/// <summary>What people need in a day, as one set of figures gives it. Read once for a set and kept, so a day of the simulation costs no lookups.</summary>
/// <param name="DrinkingLitres">Litres a person drinks and cooks with in a day.</param>
/// <param name="HeatwaveExtraLitres">Extra litres a person a day in a heatwave.</param>
/// <param name="PetWaterLitres">Litres a day for the pets of a household.</param>
/// <param name="SanitationLitres">Litres a person a day for flushing and hand washing.</param>
/// <param name="WinterLitHours">Lit hours needed in a winter day.</param>
/// <param name="SummerLitHours">Lit hours needed in a summer day.</param>
/// <param name="LaundryAfterDays">Days a household goes before it needs to wash clothes.</param>
public sealed record DemandFigures(decimal DrinkingLitres, decimal HeatwaveExtraLitres, decimal PetWaterLitres, decimal SanitationLitres, decimal WinterLitHours, decimal SummerLitHours, decimal LaundryAfterDays)
{
    /// <summary>The id of the figure for what a person drinks, which the browser's own arithmetic uses too.</summary>
    public const string DrinkingLitresId = "demand.drinking-litres";

    /// <summary>What people need as the product ships, read straight from the code so the shipped providers can be worded before any set exists.</summary>
    internal static DemandFigures Shipped { get; } = new(Litres.DrinkingPerPersonPerDay, Demand.HeatwaveExtraLitresPerPersonPerDay, Demand.PetWaterLitresPerDay, Demand.SanitationLitresPerPersonPerDay, Demand.WinterLitHours, Demand.SummerLitHours, Demand.LaundryAfterDays);

    private const string Group = "What people need";
    private const string Board = "On the board, in what each need asks for each day";

    /// <summary>The figures as parameters, with the values the product ships with.</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        new(DrinkingLitresId, Group, "Water a person drinks and cooks with in a day", "litres", Litres.DrinkingPerPersonPerDay, 1m, 10m, "Be Ready: 3 litres a person a day", Board) { BrowserNeeds = true },
        new("demand.heatwave-extra-litres", Group, "Extra water a person drinks in a day of a heatwave", "litres", Demand.HeatwaveExtraLitresPerPersonPerDay, 0m, 5m, "UKHSA heat-health guidance", Board),
        new("demand.pet-water-litres", Group, "Water a household's pets drink in a day", "litres", Demand.PetWaterLitresPerDay, 0m, 20m, "Plan", Board),
        new("demand.sanitation-litres", Group, "Water a person uses to flush and wash hands in a day when the taps are dry", "litres", Demand.SanitationLitresPerPersonPerDay, 2m, 60m, "Water UK; one short flush and hand washing", Board),
        new("demand.winter-lit-hours", Group, "Hours of light a household needs on a winter day", "hours", Demand.WinterLitHours, 1m, 16m, "Dusk to bedtime in December", Board),
        new("demand.summer-lit-hours", Group, "Hours of light a household needs on a summer day", "hours", Demand.SummerLitHours, 0m, 12m, "Dusk to bedtime in June", Board),
        new("demand.laundry-after-days", Group, "Days a household goes before it needs to wash clothes", "whole days", Demand.LaundryAfterDays, 1m, 30m, "Plan", Board) { WholeNumber = true },
    ];

    /// <summary>The figures as a set gives them.</summary>
    public static DemandFigures Of(ParameterSet figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return figures.Derive(static set => new DemandFigures(
            set[DrinkingLitresId], set["demand.heatwave-extra-litres"], set["demand.pet-water-litres"], set["demand.sanitation-litres"],
            set["demand.winter-lit-hours"], set["demand.summer-lit-hours"], set["demand.laundry-after-days"]));
    }
}

/// <summary>
/// The figures the providers carry, as parameters. They are read off the shipped table itself: every
/// stock, load, flow and store is a figure, named for the provider that has it, so nothing can be on
/// the board and missing from the list.
/// </summary>
public static class ProviderFigures
{
    private const string OwnFigure = "Household's own figure";

    private static readonly IReadOnlyList<Figure> Figures =
    [
        new("stock", "how much there is", static p => p.Stock is { } stock && p.Source != OwnFigure ? stock.Quantity : null, static (p, v) => p with { Stock = p.Stock! with { Quantity = v } }, static p => Needs.Of(p.Meets[0].Need).Unit + (p.Stock!.PerPerson ? " a person" : string.Empty), Wide),
        new("per-day", "how much it gives in a day", static p => p.Meets.Count > 0 && p.Meets[0].PerDay > 0m ? p.Meets[0].PerDay : null, static (p, v) => p with { Meets = [p.Meets[0] with { PerDay = v }, .. p.Meets.Skip(1)] }, static p => Needs.Of(p.Meets[0].Need).Unit + " a day", Wide),
        new("winter-wh", "energy it draws on a winter day", static p => p.Load?.WinterWhPerDay, static (p, v) => p with { Load = p.Load! with { WinterWhPerDay = v } }, static _ => "watt-hours", static _ => 100_000m),
        new("summer-wh", "energy it draws on a summer day", static p => p.Load?.SummerWhPerDay, static (p, v) => p with { Load = p.Load! with { SummerWhPerDay = v } }, static _ => "watt-hours", static _ => 100_000m),
        new("burst-w", "power it needs at once", static p => p.Load?.BurstW, static (p, v) => p with { Load = p.Load! with { BurstW = v } }, static _ => "watts", static _ => 20_000m),
        new("winter-flow", "what it gives on a winter day", static p => p.Flow?.WinterPerDay, static (p, v) => p with { Flow = p.Flow! with { WinterPerDay = v } }, static p => Needs.Of(p.Meets[0].Need).Unit + " a day", Wide),
        new("summer-flow", "what it gives on a summer day", static p => p.Flow?.SummerPerDay, static (p, v) => p with { Flow = p.Flow! with { SummerPerDay = v } }, static p => Needs.Of(p.Meets[0].Need).Unit + " a day", Wide),
        new("lead-days", "days before it gives anything", static p => p.Flow?.LeadTimeDays, static (p, v) => p with { Flow = p.Flow! with { LeadTimeDays = (int)v } }, static _ => "whole days", static _ => 365m),
        new("store-wh", "energy it holds", static p => p.Store?.StoreWh, static (p, v) => p with { Store = p.Store! with { StoreWh = v } }, static _ => "watt-hours", static _ => 200_000m),
        new("usable", "share of what it holds that can be drawn", static p => p.Store?.UsableFraction, static (p, v) => p with { Store = p.Store! with { UsableFraction = v } }, static _ => "as a share, 0 to 1", static _ => 1m),
        new("ceiling-w", "most power it gives at once", static p => p.Store?.CeilingW, static (p, v) => p with { Store = p.Store! with { CeilingW = v } }, static _ => "watts", static _ => 20_000m),
        new("refill-winter-wh", "energy put back on a winter day", static p => p.Store?.RefillWinterWh, static (p, v) => p with { Store = p.Store! with { RefillWinterWh = v } }, static _ => "watt-hours", static _ => 200_000m),
        new("refill-summer-wh", "energy put back on a summer day", static p => p.Store?.RefillSummerWh, static (p, v) => p with { Store = p.Store! with { RefillSummerWh = v } }, static _ => "watt-hours", static _ => 200_000m),
        new("reserve-wh", "energy kept back and never drawn", static p => p.Store?.ReserveWh, static (p, v) => p with { Store = p.Store! with { ReserveWh = v } }, static _ => "watt-hours", static _ => 200_000m),
    ];

    // The figures a household can know better than any table, and what it is asked.
    private static readonly Dictionary<string, string> HouseholdQuestions = new(StringComparer.Ordinal)
    {
        ["stairlift-batteries.stock"] = "How many days of trips to bed and the bathroom does your stairlift make in a power cut?",
        ["closed-freezer.stock"] = "How many days does your freezer keep its cold with the door shut?",
        ["portable-power-station.store-wh"] = "How many watt-hours does your power station hold?",
        ["petrol-car.stock"] = "How many days of your usual driving is half a tank?",
        ["electric-car.stock"] = "How many days of your usual driving is an ordinary charge?",
        ["wood-stove.stock"] = "How many days of fires does a full log store give you?",
        ["water-butt.stock"] = "How many litres does your water butt hold?",
    };

    /// <summary>Every provider figure as a parameter, in the order of the providers.</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } = [.. Providers.All.SelectMany(Define)];

    /// <summary>The providers as a set of figures makes them, in the shipped order.</summary>
    public static IReadOnlyList<Provider> Under(ParameterSet figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return Table(figures).Providers;
    }

    private static ProviderTable Table(ParameterSet figures) => figures.Derive(static set =>
    {
        DemandFigures demand = DemandFigures.Of(set);
        return new ProviderTable([.. Providers.All.Select(provider => Apply(provider, set, demand))]);
    });

    /// <summary>A sentence when the values contradict each other, or null: a store cannot keep back more than it can give.</summary>
    internal static string? Contradiction(IReadOnlyDictionary<string, decimal> values)
    {
        foreach (Provider provider in Providers.All)
        {
            if (provider.Store is not null && values[$"{provider.Id}.reserve-wh"] > values[$"{provider.Id}.store-wh"] * values[$"{provider.Id}.usable"])
            {
                return $"{provider.Name} cannot keep back more energy than it can give.";
            }
        }

        return null;
    }

    private static decimal Wide(decimal shipped) => Math.Max(100m, shipped * 20m);

    private static IEnumerable<ParameterDefinition> Define(Provider provider)
    {
        foreach (Figure figure in Figures)
        {
            if (figure.Read(provider) is { } shipped)
            {
                string id = $"{provider.Id}.{figure.Suffix}";
                yield return new ParameterDefinition(id, GroupOf(provider), $"{provider.Name}: {figure.Label}", figure.Unit(provider), shipped, 0m, figure.Max(shipped), provider.Source, ShownOn(provider))
                {
                    HouseholdQuestion = HouseholdQuestions.GetValueOrDefault(id),
                    WholeNumber = figure.WholeNumber,
                };
            }
        }
    }

    private static Provider Apply(Provider provider, ParameterSet set, DemandFigures demand)
    {
        // Only a figure that has been corrected is written, so a provider nobody has touched is the shipped one itself.
        Provider made = provider;
        foreach (Figure figure in Figures)
        {
            if (figure.Read(provider) is { } shipped && set[$"{provider.Id}.{figure.Suffix}"] != shipped)
            {
                made = figure.Write(made, set[$"{provider.Id}.{figure.Suffix}"]);
            }
        }

        // The words that state its figures are said again from this set's, which may differ only in what people need:
        // the same bottles are fewer days when a person drinks more. A provider whose words come out the same is the shipped one.
        Provider worded = ProviderWords.Worded(made, demand);
        return worded.Equals(provider) ? provider : worded;
    }

    private static string GroupOf(Provider provider) => provider.Meets.Count > 0 ? Needs.Of(provider.Meets[0].Need).Name : "Sources of electricity";

    private static string ShownOn(Provider provider) => provider.Meets.Count > 0
        ? "On the board, under " + string.Join(", ", provider.Meets.Select(static provision => Needs.Of(provision.Need).Name.ToLower(CultureInfo.InvariantCulture)).Distinct())
        : "On the board, in what carries each electrical thing when the grid is off";

    /// <summary>The provider as a set of figures makes it, or null when there is no such provider.</summary>
    public static Provider? Find(ParameterSet figures, string id)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(id);
        return Table(figures).ById.GetValueOrDefault(id);
    }

    private sealed record Figure(string Suffix, string Label, Func<Provider, decimal?> Read, Func<Provider, decimal, Provider> Write, Func<Provider, string> Unit, Func<decimal, decimal> Max)
    {
        public bool WholeNumber => Suffix == "lead-days";
    }

    private sealed record ProviderTable(IReadOnlyList<Provider> Providers)
    {
        public Dictionary<string, Provider> ById { get; } = Providers.ToDictionary(static provider => provider.Id, StringComparer.Ordinal);
    }
}

/// <summary>
/// The figures the scenarios carry, as parameters: for each stretch of each scenario, how much of a
/// utility a household gets, and the days the stretch runs from and to.
/// </summary>
public static class ScenarioFigures
{
    private static readonly Dictionary<Utility, string> Words = new()
    {
        [Utility.Electricity] = "electricity",
        [Utility.MainsWater] = "mains water",
        [Utility.MainsGas] = "mains gas",
        [Utility.MobileNetwork] = "the mobile network",
        [Utility.FixedInternet] = "the line and the exchange",
        [Utility.Roads] = "the roads",
        [Utility.Shops] = "the shops",
        [Utility.Fuel] = "fuel at the forecourt",
        [Utility.Deliveries] = "deliveries",
        [Utility.Pharmacy] = "the pharmacy",
        [Utility.Cash] = "cash machines and card payments",
    };

    /// <summary>Every scenario figure as a parameter, scenario by scenario.</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } = [.. Scenarios.All.SelectMany(Define)];

    /// <summary>The scenarios as a set of figures makes them, in the shipped order.</summary>
    public static IReadOnlyList<Scenario> Under(ParameterSet figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return figures.Derive(static set => new ScenarioTable([.. Scenarios.All.Select(scenario => Apply(scenario, set))])).Scenarios;
    }

    /// <summary>The scenario as a set of figures makes it. One that is not a shipped scenario has no figures and is returned as it is.</summary>
    public static Scenario Under(ParameterSet figures, Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        IReadOnlyList<Scenario> made = Under(figures);
        for (int i = 0; i < made.Count; i++)
        {
            if (ReferenceEquals(Scenarios.All[i], scenario))
            {
                return made[i];
            }
        }

        return scenario;
    }

    /// <summary>A sentence when the values contradict each other, or null: a stretch cannot end before it starts.</summary>
    internal static string? Contradiction(IReadOnlyDictionary<string, decimal> values)
    {
        foreach (Scenario scenario in Scenarios.All)
        {
            foreach (UtilityPhase phase in scenario.Phases)
            {
                if (phase.ToDay != UtilityPhase.ToEnd && values[$"{Key(scenario, phase)}.last-day"] < values[$"{Key(scenario, phase)}.first-day"])
                {
                    return $"In \"{scenario.Name}\", a stretch for {Words[phase.Utility]} ends before it starts.";
                }
            }
        }

        return null;
    }

    private static string Key(Scenario scenario, UtilityPhase phase) => $"scenario.{scenario.Id}.{phase.Utility.ToString().ToLowerInvariant()}-from-{phase.FromDay.ToString(CultureInfo.InvariantCulture)}";

    private static IEnumerable<ParameterDefinition> Define(Scenario scenario)
    {
        string group = $"Scenario: {scenario.Name}";
        string shown = $"On the board, when \"{scenario.Name}\" is chosen";
        const string Source = "National Risk Register 2025; Ofcom; Energy Networks Association storm reports";
        foreach (UtilityPhase phase in scenario.Phases)
        {
            string key = Key(scenario, phase);
            string span = phase.ToDay == UtilityPhase.ToEnd ? $"from day {phase.FromDay.ToString(CultureInfo.InvariantCulture)}" : $"from day {phase.FromDay.ToString(CultureInfo.InvariantCulture)} to day {phase.ToDay.ToString(CultureInfo.InvariantCulture)}";
            yield return new ParameterDefinition($"{key}.share", group, $"How much of {Words[phase.Utility]} there is, {span}", "as a share, 0 to 1", phase.Availability, 0m, 1m, Source, shown);
            yield return new ParameterDefinition($"{key}.first-day", group, $"The day the stretch for {Words[phase.Utility]} starts ({span})", "whole days", phase.FromDay, 1m, 180m, Source, shown) { WholeNumber = true };
            if (phase.ToDay != UtilityPhase.ToEnd)
            {
                yield return new ParameterDefinition($"{key}.last-day", group, $"The day the stretch for {Words[phase.Utility]} ends ({span})", "whole days", phase.ToDay, 1m, 180m, Source, shown) { WholeNumber = true };
            }
        }
    }

    // A scenario nobody has corrected is the shipped one itself, so the board it draws is the board the tests pin.
    // Its sentence is said again from this set's figures, which may differ only in what people need.
    private static Scenario Apply(Scenario scenario, ParameterSet set)
    {
        List<UtilityPhase> phases = [.. scenario.Phases.Select(phase => Apply(scenario, phase, set))];
        Scenario made = phases.SequenceEqual(scenario.Phases) ? scenario : scenario with { Phases = phases };
        string sentence = ScenarioWords.Sentence(made, DemandFigures.Of(set));
        return sentence == made.Sentence ? made : made with { Sentence = sentence };
    }

    private static UtilityPhase Apply(Scenario scenario, UtilityPhase phase, ParameterSet set)
    {
        string key = Key(scenario, phase);
        int last = phase.ToDay == UtilityPhase.ToEnd ? UtilityPhase.ToEnd : (int)set[$"{key}.last-day"];
        return new UtilityPhase(phase.Utility, (int)set[$"{key}.first-day"], last, set[$"{key}.share"]);
    }

    private sealed record ScenarioTable(IReadOnlyList<Scenario> Scenarios);
}
