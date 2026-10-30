using System.Globalization;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// One bar on the strip: a need and how long it holds from the start of the scenario. The shortest
/// Survive bar is the Days Ready figure, which is why the strip sits under it.
/// </summary>
/// <param name="Key">The need's row key, used by the script to find the bar.</param>
/// <param name="Name">The need as the board names it.</param>
/// <param name="Days">Days held before the need was rationed or lost; half a day when that is the first day.</param>
/// <param name="Reduced">Whether the need was met by a lesser means for any of those days.</param>
/// <param name="Scale">The number of days the strip is drawn to; a bar this long held for the whole stretch.</param>
public sealed record StripBar(string Key, string Name, decimal Days, bool Reduced, int Scale)
{
    /// <summary>True when the need held for everything that was simulated.</summary>
    public bool HeldThroughout => Days >= Scale;

    /// <summary>The figure on the bar: "0.5", "3", "30".</summary>
    public string Figure => Days.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The bar's sentence, for a reader who cannot see its length.</summary>
    public string Sentence
    {
        get
        {
            string how = Reduced ? "is kept in a reduced way" : "holds";
            return HeldThroughout
                ? $"{Name} {how} for the whole month."
                : $"{Name} {how} for {BoardCopy.DaysReady(Days)}, then not enough.";
        }
    }
}

/// <summary>Builds the strip from a board.</summary>
public static class Strip
{
    /// <summary>The needs on the strip, in the order they are drawn: the Survive rung's water, food and warmth, then light.</summary>
    public static IReadOnlyList<NeedId> Needs { get; } = [NeedId.DrinkingWater, NeedId.Food, NeedId.Warmth, NeedId.Light];

    /// <summary>One bar per strip need that the household has, from the board's day-by-day rows.</summary>
    public static IReadOnlyList<StripBar> From(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        List<StripBar> bars = [];
        foreach (NeedId id in Needs)
        {
            if (board.Row(id.ToString()) is { Needed: true } row)
            {
                bars.Add(Bar(row, board.DaysSimulated));
            }
        }

        return bars;
    }

    private static StripBar Bar(BoardRow row, int scale)
    {
        int? first = row.FirstRationedDay;
        decimal days = first switch
        {
            null => scale,
            1 => 0.5m,
            _ => first.Value - 1,
        };
        int lastHeld = first is { } f ? f - 1 : row.Days.Count;
        bool reduced = row.Days.Take(lastHeld).Any(day => day.State == CellState.Reduced);
        return new StripBar(row.Key, row.Name, days, reduced, scale);
    }
}
