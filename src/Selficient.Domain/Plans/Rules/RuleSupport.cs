using System.Globalization;
using Selficient.Domain.Measures;
using Selficient.Domain.Risks;

namespace Selficient.Domain.Plans.Rules;

/// <summary>Shared helpers so every rule reads the same way.</summary>
internal static class RuleSupport
{
    internal static IReadOnlySet<RiskKind> Mitigates(params RiskKind[] kinds) => new HashSet<RiskKind>(kinds);

    internal static PlanAction Action(string id, Tier tier, string title, string why, MoneyRange cost, IReadOnlySet<RiskKind> mitigates, ActionLink? link = null) =>
        new(ActionId.From(id), tier, title, why, cost, mitigates, link);

    /// <summary>What an action costs for this household: the price of one under its figures, as many times as it needs.</summary>
    internal static MoneyRange Price(Selficient.Domain.Households.HouseholdProfile profile, string action, decimal times = 1m) =>
        RuleFigures.Price(profile.Figures, action, times);

    /// <summary>Formats a whole number for a sentence, invariant so a comma never becomes a point.</summary>
    internal static string N(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>"one person" or "four people".</summary>
    internal static string People(int count) => count == 1 ? "one person" : $"{N(count)} people";
}
