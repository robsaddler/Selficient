#pragma warning disable CA5394 // The seeded Random builds deterministic test households; nothing here is security.
using FsCheck;
using FsCheck.Xunit;
using Selficient.Domain.Households;
using Selficient.Domain.Resilience;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Resilience;

/// <summary>The invariants of the engine, for any household FsCheck can invent.</summary>
public sealed class OutageBoardProperties
{
    private static readonly string[] Kit =
    [
        "water-bottled-three-days", "water-cans", "bath-liner", "gravity-water-filter", "food-no-cook-three-days", "food-one-month",
        "butane-cartridge-stove", "cash-in-small-notes", "heating-fallback", "power-failure-lights", "headtorches", "rechargeable-lanterns",
        "candles-and-matches", "radio-dab-fm-with-crank", "power-banks", "portable-power-station", "large-power-station",
        "rooftop-solar-and-battery", "petrol-generator", "boiler-plug-and-socket", "satellite-internet-on-standby", "car-above-half-a-tank",
        "formula-and-nappies", "pet-food-two-weeks",
    ];

    /// <summary>A household from a seed: every enum and flag the profile has, bounded so creation succeeds.</summary>
    internal static ResilienceProfile Household(int seed)
    {
        Random random = new(seed);
        int people = random.Next(1, 8);
        int children = random.Next(0, people + 1);
        DwellingType type = (DwellingType)random.Next(0, 5);
        HeatingType heating = (HeatingType)random.Next(0, 6);
        HobType hob = (HobType)random.Next(0, 3);
        bool car = random.Next(2) == 0;
        bool garden = random.Next(2) == 0;
        HouseholdProfile profile = Profiles.Build(people, children, random.Next(4) == 0, random.Next(3) == 0, random.Next(3) == 0, type, random.Next(1, 4), heating, hob, car, garden, false, false, random.Next(0, 400), FloodRisk.Low);
        CarKind kind = car ? (CarKind)random.Next(1, 4) : CarKind.None;
        return ResilienceProfile.Create(profile, random.Next(2) == 0, random.Next(3) == 0, garden && random.Next(2) == 0, (Growing)random.Next(0, 3), kind, Math.Min(children, random.Next(0, 2)), random.Next(0, 60), type == DwellingType.Flat ? random.Next(0, 12) : 0, []).Value;
    }

    private static HashSet<string> Held(int mask) => Kit.Where((_, i) => (mask & (1 << i)) != 0).ToHashSet(StringComparer.Ordinal);

    [Property(MaxTest = 200)]
    public bool Days_ready_never_falls_when_kit_is_added(int seed, int kit, int extra, int scenarioIndex)
    {
        ResilienceProfile profile = Household(seed);
        Scenario scenario = Scenarios.All[Math.Abs(scenarioIndex % Scenarios.All.Count)];
        HashSet<string> fewer = Held(kit);
        HashSet<string> more = [.. fewer, .. Held(extra)];

        Board before = OutageBoard.Simulate(profile, fewer, scenario, scenario.DefaultSeason);
        Board after = OutageBoard.Simulate(profile, more, scenario, scenario.DefaultSeason);

        return after.DaysReady >= before.DaysReady;
    }

    [Property(MaxTest = 100)]
    public bool A_longer_horizon_never_rewrites_the_days_already_simulated(int seed, int kit, int scenarioIndex)
    {
        ResilienceProfile profile = Household(seed);
        Scenario scenario = Scenarios.All[Math.Abs(scenarioIndex % Scenarios.All.Count)];
        HashSet<string> held = Held(kit);

        Board shorter = OutageBoard.Simulate(profile, held, scenario, scenario.DefaultSeason, scenario.Horizons[0]);
        Board longer = OutageBoard.Simulate(profile, held, scenario, scenario.DefaultSeason);

        return shorter.Rows.Zip(longer.Rows).All(pair => pair.First.Days.SequenceEqual(pair.Second.Days.Take(pair.First.Days.Count)));
    }

    [Property(MaxTest = 100)]
    public bool With_every_utility_up_nothing_is_lost_or_rationed(int seed, int kit)
    {
        ResilienceProfile profile = Household(seed);
        Scenario calm = Scenarios.Storm with { Id = "calm", Phases = [] };

        Board board = OutageBoard.Simulate(profile, Held(kit), calm, Season.Winter);

        return board.Rows.All(row => row.Days.All(day => day.State <= CellState.Reduced)) && board.DaysReady == board.DaysSimulated;
    }

    [Property(MaxTest = 100)]
    public bool An_unknown_tick_changes_nothing(int seed, int kit, int scenarioIndex)
    {
        ResilienceProfile profile = Household(seed);
        Scenario scenario = Scenarios.All[Math.Abs(scenarioIndex % Scenarios.All.Count)];
        HashSet<string> held = Held(kit);
        HashSet<string> withNoise = [.. held, "solar-panels-without-a-battery", "not-a-provider"];

        Board plain = OutageBoard.Simulate(profile, held, scenario, scenario.DefaultSeason);
        Board noisy = OutageBoard.Simulate(profile, withNoise, scenario, scenario.DefaultSeason);

        return plain.Rows.Zip(noisy.Rows).All(pair => pair.First.Days.SequenceEqual(pair.Second.Days));
    }

    [Property(MaxTest = 100)]
    public bool Every_cell_is_the_worst_of_its_days_and_the_headline_is_the_first_survive_failure(int seed, int kit, int scenarioIndex)
    {
        ResilienceProfile profile = Household(seed);
        Scenario scenario = Scenarios.All[Math.Abs(scenarioIndex % Scenarios.All.Count)];

        Board board = OutageBoard.Simulate(profile, Held(kit), scenario, scenario.DefaultSeason);

        bool cells = board.Rows.All(row => row.Cells.All(cell => cell.State == row.Days.Take((int)cell.Horizon).Max(day => day.State)
            && cell.HeldDays == row.Days.Take((int)cell.Horizon).TakeWhile(day => day.State == CellState.Held).Count()));
        int? first = board.Rows.Where(row => row.Rung == Rung.Survive && row.Needed).Select(row => row.FirstRationedDay).Where(day => day is not null).Min();
        decimal expected = first is null ? board.DaysSimulated : first == 1 ? 0.5m : first.Value - 1;
        return cells && board.DaysReady == expected;
    }
}
