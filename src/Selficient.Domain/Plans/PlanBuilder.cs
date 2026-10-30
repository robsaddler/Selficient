using Selficient.Domain.Households;
using Selficient.Domain.Primitives;
using Selficient.Domain.Risks;

namespace Selficient.Domain.Plans;

/// <summary>Assembles a plan from rule contributions, deterministically, refusing duplicates.</summary>
public sealed class PlanBuilder
{
    private static readonly Dictionary<Tier, string> TierTitles = new()
    {
        [Tier.ThisWeek] = "This week",
        [Tier.ThisMonth] = "This month",
        [Tier.ThreeMonths] = "Within three months",
        [Tier.TwelveMonths] = "Within a year",
    };

    private readonly IReadOnlyList<IPlanRule> _rules;

    /// <summary>Creates the builder over a fixed set of rules; order of registration does not matter.</summary>
    public PlanBuilder(IEnumerable<IPlanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = [.. rules.OrderBy(static rule => rule.Order)];
    }

    /// <summary>Builds the plan, or fails if two rules claim the same action.</summary>
    public Result<Plan> Build(HouseholdProfile profile, RiskAssessment risks)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(risks);

        // Rule order first, then the order a rule emitted its actions in, which is the order a
        // reader expects (water before candles). Both are deterministic; registration order is not.
        List<(int Order, int Sequence, PlanAction Action)> contributions = [];
        HashSet<ActionId> seen = [];
        int sequence = 0;

        foreach (IPlanRule rule in _rules)
        {
            foreach (PlanAction action in rule.Contribute(profile, risks))
            {
                if (!seen.Add(action.Id))
                {
                    return Result.Failure<Plan>(DomainErrors.DuplicateAction(action.Id.Value));
                }

                contributions.Add((rule.Order, sequence++, action));
            }
        }

        List<PlanTier> tiers = [];
        foreach (Tier tier in Enum.GetValues<Tier>())
        {
            List<PlanAction> actions = [.. contributions
                .Where(c => c.Action.Tier == tier)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Sequence)
                .Select(c => c.Action)];

            tiers.Add(new PlanTier(tier, TierTitles[tier], actions));
        }

        return Result.Success(new Plan(profile, risks, tiers));
    }
}
