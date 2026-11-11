using System.Globalization;
using Selficient.Application.Resilience;
using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Web.Formatting;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The sentences of the tally: how many actions the household has, plans, has not decided and has
/// ruled out, what the planned and undecided ones cost, and what the buttons beside them say and
/// did. Kept here so the views hold no choice of their own and every sentence is pinned by a test.
/// </summary>
public static class TallyCopy
{
    /// <summary>The kind for what the household has.</summary>
    public const string Have = "have";

    /// <summary>The kind for what the household plans to get.</summary>
    public const string Plan = "plan";

    /// <summary>The kind for what the household has ruled out.</summary>
    public const string Skip = "skip";

    /// <summary>The kind for the button that plans everything not decided.</summary>
    public const string PlanAll = "plan-all";

    private static readonly Dictionary<string, (string Label, string Said)> Buttons = new(StringComparer.Ordinal)
    {
        [Have] = ("Clear what you have", "What you had {0} is back to not decided."),
        [Plan] = ("Clear what you plan to get", "What you planned {0} is back to not decided."),
        [Skip] = ("Clear what is not for you", "What you ruled out {0} is back to not decided."),
        [PlanAll] = ("Plan all of these not yet decided", "Everything not decided {0} is now planned."),
    };

    /// <summary>"2 of 12".</summary>
    public static string HaveCount(ChoiceTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return string.Create(CultureInfo.InvariantCulture, $"{tally.Have} of {tally.Actions}");
    }

    /// <summary>"3, £40 to £90 still to spend", "3, nothing to spend" or "None yet".</summary>
    public static string PlannedCount(ChoiceTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return tally.Planned == 0 ? "None yet" : Counted(tally.Planned, tally.PlannedCost, " still to spend");
    }

    /// <summary>"5, £150 to £300", "5, nothing to spend" or "None".</summary>
    public static string UndecidedCount(ChoiceTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return tally.Undecided == 0 ? "None" : Counted(tally.Undecided, tally.UndecidedCost, string.Empty);
    }

    /// <summary>"2" or "None".</summary>
    public static string SkippedCount(ChoiceTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return tally.Skipped == 0 ? "None" : tally.Skipped.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The two figures side by side: where the house stands today, and where the plan takes it.</summary>
    public static string Standing(decimal today, decimal withPlanned, int planned) => planned == 0
        ? $"Today the house stands for {BoardCopy.DaysReady(today)}. Mark an action as planned and this line says where your plan takes it."
        : $"Today the house stands for {BoardCopy.DaysReady(today)}. With everything you plan to get, it stands for {BoardCopy.DaysReady(withPlanned)}.";

    /// <summary>What a button is called to someone who cannot see which stretch it sits in: "Clear what you have: This week".</summary>
    public static string Label(string kind, string scopeName) => $"{Buttons[kind].Label}: {scopeName}";

    /// <summary>What the page says once a button has done its work, beside the way to undo it.</summary>
    public static string Said(string kind, bool wholePlan) =>
        string.Format(CultureInfo.InvariantCulture, Buttons[kind].Said, wholePlan ? "across the whole plan" : "in this stretch");

    /// <summary>A flag as an ARIA attribute wants it: "true" or "false", lower case.</summary>
    public static string Flag(bool value) => value ? "true" : "false";

    private static string Counted(int count, MoneyRange cost, string after) => cost.To.Pence == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{count}, nothing to spend")
        : string.Create(CultureInfo.InvariantCulture, $"{count}, {MoneyFormat.Range(cost)}{after}");
}

/// <summary>One tally as the page draws it: for the whole plan, with the two figures, or for one stretch.</summary>
public sealed record TallyView(string Scope, string Name, ChoiceTally Tally, string Standing)
{
    /// <summary>The scope of the tally that covers every stretch.</summary>
    public const string PlanScope = "plan";

    /// <summary>True for the tally that covers every stretch.</summary>
    public bool WholePlan => Scope == PlanScope;

    /// <summary>The tally over the whole plan, with where the house stands today and with what is planned.</summary>
    public static TallyView ForPlan(BoardSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new TallyView(PlanScope, "The whole plan", set.Tally, TallyCopy.Standing(set.DaysReady, set.PlannedDaysReady, set.Tally.Planned));
    }

    /// <summary>The tally for one stretch.</summary>
    public static TallyView ForTier(TierOutcome tier)
    {
        ArgumentNullException.ThrowIfNull(tier);
        return new TallyView(tier.Tier.ToString(), tier.Title, tier.Tally, string.Empty);
    }
}
