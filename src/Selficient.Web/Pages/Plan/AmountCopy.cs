using System.Globalization;
using Selficient.Application.Resilience;
using Selficient.Domain.Plans;
using Selficient.Domain.Resilience;
using Selficient.Web.Formatting;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The sentences around a household's own figure: the question, the unit the figure is counted in,
/// what the plan suggests, and how far the figure goes towards an action. Kept here so the views
/// hold no choice of their own and every sentence is pinned by a test.
/// </summary>
public static class AmountCopy
{
    /// <summary>The label on every figure's box.</summary>
    public const string Question = "How much do you have?";

    private static readonly Dictionary<string, (string Unit, string Short)> Units = new(StringComparer.Ordinal)
    {
        [MeasuredStocks.FoodDays] = ("days of food for the {0} of you", "days of food"),
        [MeasuredStocks.BottledWaterLitres] = ("litres of sealed bottled water", "litres of bottled water"),
        [MeasuredStocks.WaterCansLitres] = ("litres of water in cans", "litres in cans"),
        [MeasuredStocks.CashPounds] = ("pounds in notes and coins", "pounds in cash"),
        [MeasuredStocks.StoveCartridges] = ("cartridges for the stove", "cartridges"),
    };

    /// <summary>What the figure counts, as the words after the box: "days of food for the 4 of you".</summary>
    public static string Unit(string stockId, int people) => stockId == MeasuredStocks.FoodDays && people == 1
        ? "days of food for you"
        : string.Format(CultureInfo.InvariantCulture, Units[stockId].Unit, people);

    /// <summary>What the plan asks for, beside the box.</summary>
    public static string Suggests(decimal target) => string.Create(CultureInfo.InvariantCulture, $"The plan suggests {target:0}.");

    /// <summary>How far the household's figure goes towards an action, as the line under the action says it.</summary>
    public static string Progress(CountedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Progress counted = action.Counted;
        string unit = Units[counted.StockId].Short;
        if (counted.Amount >= counted.Target)
        {
            return string.Create(CultureInfo.InvariantCulture, $"You have {counted.Amount:0} {unit}.");
        }

        string have = counted.Amount == 0m
            ? string.Create(CultureInfo.InvariantCulture, $"You have none of the {counted.Target:0} {unit}.")
            : string.Create(CultureInfo.InvariantCulture, $"You have {counted.Amount:0} of {counted.Target:0} {unit}.");

        // The rest is costed only where a part bought is a part of the price: a share of a month of food, not a share of a stove.
        return counted.CostRemaining < 1m && action.Rest.To.Pence > 0
            ? $"{have} The rest would cost {MoneyFormat.Range(action.Rest)}."
            : have;
    }
}

/// <summary>The box a household types its own figure into, for one action: which stock it counts, what the action asks for, and the words around it.</summary>
public sealed record AmountView(string StockId, int Target, int Max, string Unit, string Suggests)
{
    /// <summary>The boxes for the actions in a tier that have one, by action id.</summary>
    public static IReadOnlyDictionary<string, AmountView> For(IEnumerable<PlanAction> actions, ResilienceProfile household)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(household);
        Dictionary<string, AmountView> views = new(StringComparer.Ordinal);
        foreach (PlanAction action in actions)
        {
            if (MeasuredStocks.ForAction(action.Id.Value) is { } stock)
            {
                decimal target = MeasuredStocks.Target(stock, action.Id.Value, household);
                views[action.Id.Value] = new AmountView(stock.Id, (int)target, stock.Max, AmountCopy.Unit(stock.Id, household.Household.Size.Count), AmountCopy.Suggests(target));
            }
        }

        return views;
    }
}
