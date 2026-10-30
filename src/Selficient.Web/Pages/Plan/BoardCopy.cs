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
    /// <summary>The unit after a figure: "day" for one, "days" for anything else.</summary>
    public static string Unit(decimal days) => days == 1m ? "day" : "days";

    /// <summary>The headline figure as words: "half a day", "1 day", "3 days".</summary>
    public static string DaysReady(decimal days) => days == 0.5m ? "half a day" : days == 1m ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days:0.#} days");
}
