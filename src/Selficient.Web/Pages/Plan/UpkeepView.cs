using Selficient.Domain.Households;
using Selficient.Domain.Upkeep;

namespace Selficient.Web.Pages.Plan;

/// <summary>What the upkeep section of the plan page shows.</summary>
public sealed class UpkeepView
{
    /// <summary>Builds the view for a household.</summary>
    public UpkeepView(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        UpkeepFacts facts = UpkeepFacts.From(profile);
        Reminders = UpkeepSchedule.For(facts);
        CalendarHref = $"/plan/calendar.ics?decking={Flag(facts.Decking)}&garden={Flag(facts.Garden)}&petrol={Flag(facts.PetrolCar)}&electric={Flag(facts.ElectricCar)}&tree={Flag(facts.TallTree)}&stove={Flag(facts.WoodStove)}&flood={Flag(facts.FloodRisk)}&meter={Flag(facts.PrepaymentMeter)}&supply={Flag(facts.OwnWater)}&tank={Flag(facts.OwnDrains)}&lift={Flag(facts.Stairlift)}&heater={Flag(facts.GasHeater)}";
    }

    /// <summary>The reminders, in schedule order.</summary>
    public IReadOnlyList<Reminder> Reminders { get; }

    /// <summary>The download link, carrying only the eleven yes-or-no facts that change the list.</summary>
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
