using Selficient.Domain.Households;
using Selficient.Domain.Upkeep;

namespace Selficient.Web.Pages.Plan;

/// <summary>What the upkeep section of the plan page shows.</summary>
public sealed class UpkeepView
{
    /// <summary>Builds the view for a home.</summary>
    public UpkeepView(Dwelling dwelling)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        Reminders = UpkeepSchedule.For(dwelling.HasDecking, dwelling.HasGarden);
        CalendarHref = $"/plan/calendar.ics?decking={Flag(dwelling.HasDecking)}&garden={Flag(dwelling.HasGarden)}";
    }

    /// <summary>The reminders, in schedule order.</summary>
    public IReadOnlyList<Reminder> Reminders { get; }

    /// <summary>The download link, carrying only the two booleans.</summary>
    public string CalendarHref { get; }

    /// <summary>When a reminder comes round, in the interface's voice.</summary>
    public static string When(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        if (UpkeepSchedule.FollowsClockChanges(reminder))
        {
            return "At the clock changes, March and October";
        }

        return reminder.Cadence switch
        {
            Cadence.Monthly => "Monthly",
            Cadence.Quarterly => "Every three months",
            Cadence.Yearly => "Every October",
            _ => string.Join(" and ", reminder.Dates.Select(static d => new DateOnly(2000, d.Month, d.Day).ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")))),
        };
    }

    private static string Flag(bool value) => value ? "true" : "false";
}
