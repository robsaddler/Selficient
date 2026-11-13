using System.Text.RegularExpressions;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Risks;

namespace Selficient.Domain.Plans;

/// <summary>When an action belongs. A true sequence, so it is numbered in the interface.</summary>
public enum Tier
{
    /// <summary>The government checklist done properly.</summary>
    ThisWeek,

    /// <summary>A week without mains water or power.</summary>
    ThisMonth,

    /// <summary>Heating, fridge and internet through a multi-day cut.</summary>
    ThreeMonths,

    /// <summary>The investment that pays for itself.</summary>
    TwelveMonths,
}

/// <summary>A stable, kebab-case identifier for an action; what a tick refers to.</summary>
public readonly partial record struct ActionId
{
    private ActionId(string value) => Value = value;

    /// <summary>The identifier.</summary>
    public string Value { get; }

    /// <summary>Creates an id. Rules are code, so a bad id is a programming error and throws.</summary>
    public static ActionId From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return KebabCase().IsMatch(value)
            ? new ActionId(value)
            : throw new ArgumentException("An action id is lower-case words joined by hyphens.", nameof(value));
    }

    /// <summary>Whether a string is shaped like an action id, for input that arrives from a browser.</summary>
    public static bool IsValid(string? value) => value is not null && KebabCase().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}

/// <summary>
/// The official place an action is done: a government service, never a shop. The address is a
/// constant in the rules, so nothing a household typed can become a link.
/// </summary>
public sealed record ActionLink(string Text, Uri Address);

/// <summary>One thing to buy, do or arrange, with the sentence that says why.</summary>
public sealed record PlanAction(
    ActionId Id,
    Tier Tier,
    string Title,
    string Why,
    MoneyRange Cost,
    IReadOnlySet<RiskKind> Mitigates,
    ActionLink? Link = null);

/// <summary>Contributes actions for a household. Rules are open for extension: add a class, not a branch.</summary>
public interface IPlanRule
{
    /// <summary>Stable ordering key so output is deterministic regardless of registration order.</summary>
    int Order { get; }

    /// <summary>The actions this rule adds, if any.</summary>
    IEnumerable<PlanAction> Contribute(HouseholdProfile profile, RiskAssessment risks);
}

/// <summary>A tier with its actions and summed cost.</summary>
public sealed record PlanTier(Tier Tier, string Title, IReadOnlyList<PlanAction> Actions)
{
    /// <summary>The cost of everything in the tier.</summary>
    public MoneyRange Cost => Actions.Aggregate(MoneyRange.Free, static (sum, action) => sum + action.Cost);
}

/// <summary>The whole plan for one household.</summary>
public sealed record Plan(HouseholdProfile Profile, RiskAssessment Risks, IReadOnlyList<PlanTier> Tiers)
{
    /// <summary>Every action across every tier, in plan order.</summary>
    public IEnumerable<PlanAction> Actions => Tiers.SelectMany(static tier => tier.Actions);

    /// <summary>The cost of the whole plan.</summary>
    public MoneyRange Cost => Tiers.Aggregate(MoneyRange.Free, static (sum, tier) => sum + tier.Cost);
}
