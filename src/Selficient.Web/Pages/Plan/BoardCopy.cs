using System.Globalization;
using Selficient.Application.Resilience;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>The sentences on the board. Every one is whole, names what carried and what failed, and never guesses.</summary>
public static class BoardCopy
{
    /// <summary>A state in a word, for the key and the cells.</summary>
    public static string State(CellState state) => state switch
    {
        CellState.Held => "Held",
        CellState.Reduced => "Held in a reduced way",
        CellState.Rationed => "Not enough",
        _ => "Lost",
    };

    /// <summary>What a cell says when read out: the row, the horizon, the state and how far in it got.</summary>
    public static string Cell(BoardRow row, HorizonCell cell)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cell);
        string when = BoardComposer.HorizonWords(cell.Horizon);
        return cell.State switch
        {
            CellState.Held => $"{row.Name}, {when}: held throughout.",
            _ when cell.HeldDays > 0 => $"{row.Name}, {when}: held for {Days(cell.HeldDays)}, then {State(cell.State).ToLowerInvariant()}.",
            _ => $"{row.Name}, {when}: {State(cell.State).ToLowerInvariant()} from the start.",
        };
    }

    /// <summary>The row's sentence: who carries it, who fails it, and when it turns.</summary>
    public static string Row(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.Needed)
        {
            return "Not something this house needs in this scenario.";
        }

        DayState first = row.Days[0];
        int heldDays = 0;
        while (heldDays < row.Days.Count && row.Days[heldDays].State == CellState.Held)
        {
            heldDays++;
        }

        if (heldDays == row.Days.Count)
        {
            return $"Held for the whole stretch by {Name(first.CarriedBy)}.";
        }

        DayState turn = row.Days[heldDays];
        string carried = heldDays > 0 ? $"Held for {Days(heldDays)} by {Name(first.CarriedBy)}, then " : string.Empty;
        string then = turn.State switch
        {
            CellState.Reduced => $"held in a reduced way by {Name(turn.CarriedBy)}",
            CellState.Rationed => $"not enough: {Name(turn.CarriedBy)} gives some of it",
            _ => "lost",
        };
        string because = turn.FailedBy is null ? string.Empty : $", because {Name(turn.FailedBy)} cannot carry it";
        string sentence = $"{carried}{then}{because}.";
        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>A provider's name in lower case for the middle of a sentence, or the engine's own prose for a dependency.</summary>
    public static string Name(string? providerId)
    {
        if (providerId is null)
        {
            return "nothing in the house";
        }

        Provider? provider = Providers.Find(providerId);
        if (provider is null)
        {
            return providerId;
        }

        string name = provider.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>"1 day" or "n days".</summary>
    public static string Days(int days) => days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");

    /// <summary>The headline figure as the page says it.</summary>
    /// <summary>What an action does to the board, as the line under the action says it: each need from the days it holds to the days it would.</summary>
    public static string Turns(IReadOnlyList<Selficient.Application.Resilience.NeedShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        List<string> parts = [];
        foreach (Selficient.Application.Resilience.NeedShift shift in shifts.Take(3))
        {
            parts.Add(shift.To > shift.From
                ? $"{shift.Need.ToLowerInvariant()} from {DaysReady(shift.From)} to {DaysReady(shift.To)}"
                : $"{shift.Need.ToLowerInvariant()} eased, though not for longer");
        }

        string more = shifts.Count > 3 ? string.Create(CultureInfo.InvariantCulture, $", and {shifts.Count - 3} more") : string.Empty;
        return $"With this: {string.Join(", ", parts)}{more}.";
    }

    /// <summary>What a tier buys, as the tier header says it.</summary>
    public static string TierOutcome(decimal? days, bool excludes = false) => days is not { } d
        ? string.Empty
        : excludes
            ? $"With everything up to here except what is not for you, the house stands for {DaysReady(d)}."
            : $"With everything up to here ticked, the house stands for {DaysReady(d)}.";

    private static readonly Dictionary<Rung, (string Name, string What, string Mark)> Rungs = new()
    {
        [Rung.Survive] = ("Survive", "Stay alive: water, food, warmth, medicines, and the people and animals who depend on you.", "need-warmth"),
        [Rung.Function] = ("Keep going", "Get through it: light, information, a way to call for help, cooking, sanitation and cash.", "need-light"),
        [Rung.House] = ("Keep the house running", "The house as a house: the fridge and freezer, the heating, hot water, the internet and the car.", "source-house"),
        [Rung.Normal] = ("Carry on as normal", "Life as usual: working from home, laundry and something to watch.", "need-entertainment"),
    };

    /// <summary>The rung's name as the board says it.</summary>
    public static string RungName(Rung rung) => Rungs[rung].Name;

    /// <summary>What the rung is for, in one line under its name.</summary>
    public static string RungWhat(Rung rung) => Rungs[rung].What;

    /// <summary>The mark drawn beside the rung's name.</summary>
    public static string RungMark(Rung rung) => Rungs[rung].Mark;

    /// <summary>How the rung stands over the whole stretch: how many of its needs are held to the last horizon.</summary>
    public static string RungSummary(IReadOnlyList<BoardRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int held = rows.Count(row => row.Cells.Count > 0 && row.Cells[^1].State == CellState.Held);
        return string.Create(CultureInfo.InvariantCulture, $"Held for the whole stretch: {held} of {rows.Count}.");
    }

    /// <summary>The unit after a figure: "day" for one, "days" for anything else.</summary>
    public static string Unit(decimal days) => days == 1m ? "day" : "days";

    /// <summary>The headline figure as words: "half a day", "1 day", "3 days".</summary>
    public static string DaysReady(decimal days) => days == 0.5m ? "half a day" : days == 1m ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days:0.#} days");
}
