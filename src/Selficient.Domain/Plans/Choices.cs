using Selficient.Domain.Measures;

namespace Selficient.Domain.Plans;

/// <summary>
/// What a household has said about the plan's actions: the ones it has, the ones it plans to get
/// and the ones that are not for it. Anything in none of the three is not decided. An action is in
/// one state only: having it beats planning it, and planning it beats ruling it out, so a muddled
/// post still reads one way.
/// </summary>
public sealed class PlanChoices
{
    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

    /// <summary>Creates the choices, settling any id that arrives in more than one list.</summary>
    public PlanChoices(IReadOnlySet<string> have, IReadOnlySet<string> planned, IReadOnlySet<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(have);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(skipped);
        Have = have;
        Planned = new HashSet<string>(planned.Where(id => !have.Contains(id)), StringComparer.Ordinal);
        Skipped = new HashSet<string>(skipped.Where(id => !have.Contains(id) && !planned.Contains(id)), StringComparer.Ordinal);
    }

    /// <summary>Nothing said about anything.</summary>
    public static PlanChoices None { get; } = new(Nothing, Nothing, Nothing);

    /// <summary>The actions the household has today.</summary>
    public IReadOnlySet<string> Have { get; }

    /// <summary>The actions the household plans to get or do, and does not have yet.</summary>
    public IReadOnlySet<string> Planned { get; }

    /// <summary>The actions the household has ruled out.</summary>
    public IReadOnlySet<string> Skipped { get; }
}

/// <summary>
/// The count of a set of actions by what the household said about each, with the money still to
/// spend on the ones it plans and the money the undecided ones would cost. What it already has is
/// not counted in either sum: that money is spent.
/// </summary>
public sealed record ChoiceTally(int Actions, int Have, int Planned, MoneyRange PlannedCost, int Undecided, MoneyRange UndecidedCost, int Skipped)
{
    /// <summary>Counts the actions.</summary>
    public static ChoiceTally Of(IEnumerable<PlanAction> actions, PlanChoices choices)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(choices);
        int all = 0;
        int have = 0;
        int planned = 0;
        int undecided = 0;
        int skipped = 0;
        MoneyRange plannedCost = MoneyRange.Free;
        MoneyRange undecidedCost = MoneyRange.Free;
        foreach (PlanAction action in actions)
        {
            all++;
            string id = action.Id.Value;
            if (choices.Have.Contains(id))
            {
                have++;
            }
            else if (choices.Planned.Contains(id))
            {
                planned++;
                plannedCost += action.Cost;
            }
            else if (choices.Skipped.Contains(id))
            {
                skipped++;
            }
            else
            {
                undecided++;
                undecidedCost += action.Cost;
            }
        }

        return new ChoiceTally(all, have, planned, plannedCost, undecided, undecidedCost, skipped);
    }
}
