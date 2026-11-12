using System.Globalization;
using Selficient.Application.Resilience;
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

    /// <summary>The first choice on every action.</summary>
    public const string HaveAll = "Have it";

    /// <summary>The first choice on an action the household counted and has part of: the figure is used, and the pill says so.</summary>
    public const string HaveSome = "Have some";

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

    /// <summary>What the plan asks for, beside the box; for cash, with the rate it rests on, which the household can change.</summary>
    public static string Suggests(string stockId, decimal target, Assumptions assumed)
    {
        ArgumentNullException.ThrowIfNull(assumed);
        return stockId == MeasuredStocks.CashPounds
            ? string.Create(CultureInfo.InvariantCulture, $"The plan suggests {target:0}, at £{assumed.CashPoundsPerPersonPerDay} a person a day. The rate is yours to change, where the plan lists what it assumes.")
            : string.Create(CultureInfo.InvariantCulture, $"The plan suggests {target:0}.");
    }

    /// <summary>The label on the cash rate's box.</summary>
    public const string CashRateLabel = "What a person needs in cash for a day, when only cash will do";

    /// <summary>The button that puts the plan's own rate back.</summary>
    public static string CashRateReset => string.Create(CultureInfo.InvariantCulture, $"Use the plan's £{Assumptions.Default.CashPoundsPerPersonPerDay}");

    /// <summary>What the rate comes to for this household, under the box.</summary>
    public static string CashRateSays(Assumptions assumed, int people)
    {
        ArgumentNullException.ThrowIfNull(assumed);
        decimal tenDays = 10m * people * assumed.CashPoundsPerPersonPerDay;
        string whose = people == 1 ? "you" : string.Create(CultureInfo.InvariantCulture, $"the {people} of you");
        return string.Create(CultureInfo.InvariantCulture, $"At £{assumed.CashPoundsPerPersonPerDay} a person a day, ten days of cash for {whose} is {tenDays.ToString("C0", Selficient.Web.Composition.SelficientCulture.EnGb)}.");
    }

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
    /// <summary>The boxes for the actions that have one, by action id, from what the board says each asks for.</summary>
    public static IReadOnlyDictionary<string, AmountView> For(BoardSet set, int people)
    {
        ArgumentNullException.ThrowIfNull(set);
        Dictionary<string, AmountView> views = new(StringComparer.Ordinal);
        foreach (ActionTarget target in set.Targets)
        {
            views[target.ActionId] = new AmountView(target.StockId, (int)target.Target, MeasuredStocks.Find(target.StockId)!.Max, AmountCopy.Unit(target.StockId, people), AmountCopy.Suggests(target.StockId, target.Target, set.Assumed));
        }

        return views;
    }
}
