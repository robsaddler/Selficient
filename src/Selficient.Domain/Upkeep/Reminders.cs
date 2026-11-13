using Selficient.Domain.Households;

namespace Selficient.Domain.Upkeep;

/// <summary>How often a piece of upkeep comes round.</summary>
public enum Cadence
{
    /// <summary>On a fixed list of dates each year.</summary>
    OnDates,

    /// <summary>Every month on the first.</summary>
    Monthly,

    /// <summary>Every three months on the first of January, April, July and October.</summary>
    Quarterly,

    /// <summary>Once a year on the first of October.</summary>
    Yearly,
}

/// <summary>A recurring task the plan depends on, with the sentence that says why.</summary>
public sealed record Reminder(string Id, string Title, string Why, Cadence Cadence, IReadOnlyList<(int Month, int Day)> Dates)
{
    /// <summary>A reminder with no fixed dates (monthly, quarterly, yearly).</summary>
    public Reminder(string id, string title, string why, Cadence cadence)
        : this(id, title, why, cadence, [])
    {
    }
}

/// <summary>
/// The facts about a home that change its upkeep, and nothing else: ten yes-or-no answers, which
/// is all the calendar's address carries.
/// </summary>
/// <param name="Decking">Timber decking.</param>
/// <param name="Garden">A garden.</param>
/// <param name="PetrolCar">A car with a tank.</param>
/// <param name="ElectricCar">A car with a battery.</param>
/// <param name="TallTree">A tree tall enough to reach the house.</param>
/// <param name="WoodStove">A stove that burns logs.</param>
/// <param name="FloodRisk">The flood map puts the home at low risk or more.</param>
/// <param name="PrepaymentMeter">Gas or electricity is paid for through a prepayment meter.</param>
/// <param name="OwnWater">The water is the home's own supply.</param>
/// <param name="OwnDrains">The waste goes to the home's own tank.</param>
public sealed record UpkeepFacts(bool Decking = false, bool Garden = false, bool PetrolCar = false, bool ElectricCar = false, bool TallTree = false, bool WoodStove = false, bool FloodRisk = false, bool PrepaymentMeter = false, bool OwnWater = false, bool OwnDrains = false)
{
    /// <summary>The facts as a plan's profile gives them.</summary>
    public static UpkeepFacts From(HouseholdProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Dwelling dwelling = profile.Dwelling;
        Circumstances circumstances = profile.Circumstances;
        return new UpkeepFacts(dwelling.HasDecking, dwelling.HasGarden, profile.HasPetrolCar, profile.HasElectricCar, dwelling.HasTallTreeNearHouse, profile.HasWoodStove, dwelling.FloodRisk >= Households.FloodRisk.Low, circumstances.PrepaymentMeter, profile.HasOwnWater, circumstances.OwnDrains);
    }
}

/// <summary>
/// The upkeep schedule for a household. Only facts that change the list are consulted, and a task
/// is only there for a home it applies to: no tank to keep half full in a house with no car, and no
/// tree to look at where there is none.
/// </summary>
public static class UpkeepSchedule
{
    /// <summary>Builds the schedule.</summary>
    public static IReadOnlyList<Reminder> For(UpkeepFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string review = "Once a year, walk through the plan and check that the cash limit on the contents policy still matches what is in the box.";
        if (facts.TallTree)
        {
            review += " Look at the tree after the winter storms.";
        }

        List<Reminder> reminders =
        [
            new("water-rotation", "Refill the stored water", "Empty, rinse and refill the water cans, add a purifying tablet, and swap the bottled water. Tied to the clock changes so it is never forgotten.", Cadence.OnDates, [(3, 29), (10, 25)]),
            new("alarm-tests", "Test the smoke and carbon monoxide alarms", "Press the test button on each alarm and check that the plug-in power-failure torches light when unplugged.", Cadence.Monthly),
            new("battery-top-up", "Top up the power banks and the power station", "Charge them to between half and three-quarters full, and check the stove cartridges and candles.", Cadence.Quarterly),
            new("stored-cells-check", "Check the cells in the radio and the torches", "Open the radio and every torch that sits in a drawer and look for leaked cells, because a leaked cell ruins the thing it was meant to power. Store the spare cells beside each one, not inside it, and switch the radio on to hear that it works.", Cadence.OnDates, [(3, 29), (10, 25)]),
            new("first-aid-check", "Check the first-aid kit and medicines", "Replace anything used or out of date, and make sure there are two weeks of any prescription.", Cadence.OnDates, [(3, 29), (10, 25)]),
        ];

        if (facts.PetrolCar)
        {
            reminders.Add(new("winter-car", "Keep the car above half a tank from now until March", "Fuel pumps need electricity too, and a charged car is a charger and a way off the hill.", Cadence.OnDates, [(11, 1)]));
        }

        if (facts.ElectricCar)
        {
            reminders.Add(new("winter-car-charge", "Keep the car above half charge from now until March", "Charging stops with the power, at home and at most public chargers, and a charged car is a charger and a way off the hill.", Cadence.OnDates, [(11, 1)]));
        }

        reminders.Add(new("annual-review", "Review the plan, the prices and the insurance limits", review, Cadence.Yearly));

        if (facts.Decking)
        {
            reminders.Add(new("deck-leaf-litter", "Clear the leaf litter from under the decking", "After leaf-fall and again in late spring. A leaf blower through the gaps, or lift two boards and rake.", Cadence.OnDates, [(11, 15), (5, 1)]));
        }

        if (facts.Garden)
        {
            reminders.Add(new("firebreak-and-hose", "Walk the firebreak and check the hose and outside tap", "Keep a metre clear at the boundary before summer, and drain the outside tap before the first frost.", Cadence.OnDates, [(5, 1), (11, 1)]));
        }

        if (facts.WoodStove)
        {
            reminders.Add(new("flue-sweep", "Have the stove's flue swept and fill the log store", "Before the first fire of the autumn. A swept flue and a store of dry logs are what make the stove safe to lean on when the power is off.", Cadence.OnDates, [(9, 1)]));
        }

        if (facts.FloodRisk)
        {
            reminders.Add(new("flood-plan-check", "Check your flood warnings and walk through the flood plan", "Make sure the phone numbers and addresses on the warning service are current, and that everyone knows where the stopcock is and what is moved out of the water's way first.", Cadence.OnDates, [(10, 1)]));
        }

        if (facts.PrepaymentMeter)
        {
            reminders.Add(new("prepayment-winter-credit", "Build up credit on the prepayment meter before winter", "A week or more ahead through the cold months, so a storm that shuts the shops and the app does not leave the meter empty when the power comes back.", Cadence.OnDates, [(11, 1)]));
        }

        if (facts.OwnWater)
        {
            reminders.Add(new("own-water-test", "Have your water supply tested", "A private supply has nobody testing it but you. Your council's environmental health team will sample it for a fee, and a year is the longest to leave it.", Cadence.OnDates, [(5, 1)]));
        }

        if (facts.OwnDrains)
        {
            reminders.Add(new("tank-emptying", "Check when the tank was last emptied", "A full tank backs up at the worst time. Book the tanker before winter, while it can still reach you.", Cadence.OnDates, [(9, 1)]));
        }

        return reminders;
    }

    /// <summary>
    /// The UK clock changes: the last Sunday of March and of October. The water rotation, the
    /// first-aid check and the check of stored cells follow these rather than fixed dates.
    /// </summary>
    public static DateOnly LastSundayOf(int year, int month)
    {
        DateOnly last = new(year, month, DateTime.DaysInMonth(year, month));
        int back = ((int)last.DayOfWeek - (int)DayOfWeek.Sunday + 7) % 7;
        return last.AddDays(-back);
    }

    /// <summary>True for the reminders that follow the clock changes rather than their nominal dates.</summary>
    public static bool FollowsClockChanges(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        return reminder.Id is "water-rotation" or "first-aid-check" or "stored-cells-check";
    }

    /// <summary>The next occurrence of each reminder on or after <paramref name="from"/>, for the calendar.</summary>
    public static IReadOnlyList<(Reminder Reminder, DateOnly Date)> Occurrences(IReadOnlyList<Reminder> reminders, DateOnly from, int years)
    {
        ArgumentNullException.ThrowIfNull(reminders);
        List<(Reminder, DateOnly)> occurrences = [];

        foreach (Reminder reminder in reminders)
        {
            for (int year = from.Year; year < from.Year + years; year++)
            {
                foreach (DateOnly date in DatesIn(reminder, year))
                {
                    if (date >= from)
                    {
                        occurrences.Add((reminder, date));
                    }
                }
            }
        }

        occurrences.Sort(static (a, b) => a.Item2.CompareTo(b.Item2) != 0 ? a.Item2.CompareTo(b.Item2) : string.CompareOrdinal(a.Item1.Id, b.Item1.Id));
        return occurrences;
    }

    private static IEnumerable<DateOnly> DatesIn(Reminder reminder, int year)
    {
        switch (reminder.Cadence)
        {
            case Cadence.Monthly:
                for (int month = 1; month <= 12; month++)
                {
                    yield return new DateOnly(year, month, 1);
                }

                break;
            case Cadence.Quarterly:
                yield return new DateOnly(year, 1, 1);
                yield return new DateOnly(year, 4, 1);
                yield return new DateOnly(year, 7, 1);
                yield return new DateOnly(year, 10, 1);
                break;
            case Cadence.Yearly:
                yield return new DateOnly(year, 10, 1);
                break;
            default:
                if (FollowsClockChanges(reminder))
                {
                    yield return LastSundayOf(year, 3);
                    yield return LastSundayOf(year, 10);
                }
                else
                {
                    foreach ((int month, int day) in reminder.Dates)
                    {
                        yield return new DateOnly(year, month, day);
                    }
                }

                break;
        }
    }
}
