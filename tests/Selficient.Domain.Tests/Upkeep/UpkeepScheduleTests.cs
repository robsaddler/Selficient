using Selficient.Domain.Households;
using Selficient.Domain.Upkeep;
using Selficient.TestKit;

namespace Selficient.Domain.Tests.Upkeep;

public sealed class UpkeepScheduleTests
{
    [Theory]
    [InlineData(2026, 3, 29)]
    [InlineData(2026, 10, 25)]
    [InlineData(2027, 3, 28)]
    [InlineData(2027, 10, 31)]
    [InlineData(2028, 3, 26)]
    [InlineData(2028, 10, 29)]
    public void Clock_changes_are_the_last_sundays_of_march_and_october(int year, int month, int day)
    {
        UpkeepSchedule.LastSundayOf(year, month).ShouldBe(new DateOnly(year, month, day));
        UpkeepSchedule.LastSundayOf(year, month).DayOfWeek.ShouldBe(DayOfWeek.Sunday);
    }

    [Fact]
    public void Decking_and_a_garden_add_their_own_tasks()
    {
        UpkeepSchedule.For(new UpkeepFacts(PetrolCar: true)).Select(r => r.Id).ShouldBe(["water-rotation", "alarm-tests", "battery-top-up", "stored-cells-check", "first-aid-check", "winter-car", "annual-review"]);
        UpkeepSchedule.For(new UpkeepFacts()).Single(r => r.Id == "stored-cells-check").Why.ShouldStartWith("Open the radio and every torch that sits in a drawer and look for leaked cells");
        UpkeepSchedule.For(new UpkeepFacts(Decking: true)).Select(r => r.Id).ShouldContain("deck-leaf-litter");
        UpkeepSchedule.For(new UpkeepFacts(Garden: true)).Select(r => r.Id).ShouldContain("firebreak-and-hose");
        UpkeepSchedule.For(new UpkeepFacts(Decking: true, Garden: true, PetrolCar: true)).Count.ShouldBe(9);
        UpkeepSchedule.For(new UpkeepFacts(true, true, true, false, true, true, true)).ShouldAllBe(r => r.Why.EndsWith('.') && r.Title.Length > 0);
        Should.Throw<ArgumentNullException>(() => UpkeepSchedule.For(null!));
        Should.Throw<ArgumentNullException>(() => UpkeepFacts.From(null!));
    }

    [Fact]
    public void A_task_is_only_there_for_a_home_it_applies_to()
    {
        // No car: nothing about a tank. No tree: the yearly review does not send anyone to look at one.
        IReadOnlyList<Reminder> bare = UpkeepSchedule.For(new UpkeepFacts());
        bare.Select(r => r.Id).ShouldBe(["water-rotation", "alarm-tests", "battery-top-up", "stored-cells-check", "first-aid-check", "annual-review"]);
        bare.Single(r => r.Id == "annual-review").Why.ShouldBe("Once a year, walk through the plan and check that the cash limit on the contents policy still matches what is in the box.");
        bare.ShouldAllBe(r => !r.Why.Contains("tree", StringComparison.Ordinal) && !r.Title.Contains("the car", StringComparison.Ordinal));

        UpkeepSchedule.For(new UpkeepFacts(TallTree: true)).Single(r => r.Id == "annual-review").Why.ShouldEndWith("what is in the box. Look at the tree after the winter storms.");

        // An electric car has a charge to keep, not a tank.
        IReadOnlyList<Reminder> electric = UpkeepSchedule.For(new UpkeepFacts(ElectricCar: true));
        electric.Select(r => r.Id).ShouldNotContain("winter-car");
        electric.Single(r => r.Id == "winter-car-charge").Title.ShouldBe("Keep the car above half charge from now until March");

        // A stove is swept before the autumn; a house the flood map names checks its warnings before the winter.
        Reminder sweep = UpkeepSchedule.For(new UpkeepFacts(WoodStove: true)).Single(r => r.Id == "flue-sweep");
        sweep.Title.ShouldBe("Have the stove's flue swept and fill the log store");
        sweep.Dates.ShouldBe([(9, 1)]);
        Reminder flood = UpkeepSchedule.For(new UpkeepFacts(FloodRisk: true)).Single(r => r.Id == "flood-plan-check");
        flood.Title.ShouldBe("Check your flood warnings and walk through the flood plan");
        flood.Dates.ShouldBe([(10, 1)]);
    }

    [Fact]
    public void The_facts_are_read_from_the_plans_own_profile()
    {
        UpkeepFacts.From(Profiles.RobsHouse()).ShouldBe(new UpkeepFacts(Decking: true, Garden: true, PetrolCar: true, TallTree: true));
        UpkeepFacts.From(Profiles.Flat()).ShouldBe(new UpkeepFacts());
        UpkeepFacts.From(Profiles.FloodZoneBungalow()).ShouldBe(new UpkeepFacts(Garden: true, PetrolCar: true, WoodStove: true, FloodRisk: true), "wood as the main heating is a stove, and high on the flood map is a flood plan to keep");

        HouseholdProfile electric = Profiles.RobsHouse() with { Particulars = new Particulars(ElectricCar: true, WoodStove: true, WaterButt: false, Infants: 0) };
        UpkeepFacts.From(electric).ShouldBe(new UpkeepFacts(Decking: true, Garden: true, ElectricCar: true, TallTree: true, WoodStove: true));
        UpkeepFacts.From(Profiles.Flat() with { Particulars = new Particulars(true, false, false, 0) }).ElectricCar.ShouldBeFalse("there is no car to be electric");
    }

    [Fact]
    public void Occurrences_cover_the_window_in_date_order_and_follow_the_clock_changes()
    {
        IReadOnlyList<Reminder> reminders = UpkeepSchedule.For(new UpkeepFacts(Decking: true, Garden: true, PetrolCar: true));

        IReadOnlyList<(Reminder Reminder, DateOnly Date)> occurrences = UpkeepSchedule.Occurrences(reminders, new DateOnly(2026, 11, 1), 2);

        occurrences.Select(o => o.Date).ShouldBeInOrder();
        occurrences.ShouldAllBe(o => o.Date >= new DateOnly(2026, 11, 1) && o.Date < new DateOnly(2028, 1, 1));
        occurrences.Where(o => o.Reminder.Id == "water-rotation").Select(o => o.Date).ShouldBe([new DateOnly(2027, 3, 28), new DateOnly(2027, 10, 31)]);
        occurrences.Where(o => o.Reminder.Id == "stored-cells-check").Select(o => o.Date).ShouldBe([new DateOnly(2027, 3, 28), new DateOnly(2027, 10, 31)], "the cells are checked when the clocks change, with the water");
        occurrences.Count(o => o.Reminder.Id == "alarm-tests").ShouldBe(14);
        occurrences.Count(o => o.Reminder.Id == "battery-top-up").ShouldBe(4);
        occurrences.Count(o => o.Reminder.Id == "annual-review").ShouldBe(1);
        occurrences.Where(o => o.Reminder.Id == "deck-leaf-litter").Select(o => o.Date).ShouldBe([new DateOnly(2026, 11, 15), new DateOnly(2027, 5, 1), new DateOnly(2027, 11, 15)]);
        occurrences.Where(o => o.Date == new DateOnly(2027, 11, 1)).Select(o => o.Reminder.Id).ShouldBe(["alarm-tests", "firebreak-and-hose", "winter-car"], "same-day ties are broken by id");
    }

    [Fact]
    public void Guards_reject_nulls()
    {
        Should.Throw<ArgumentNullException>(() => UpkeepSchedule.FollowsClockChanges(null!));
        Should.Throw<ArgumentNullException>(() => UpkeepSchedule.Occurrences(null!, new DateOnly(2026, 1, 1), 1));
    }
}
