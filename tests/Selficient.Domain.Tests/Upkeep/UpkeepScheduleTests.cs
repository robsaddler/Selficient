using Selficient.Domain.Upkeep;

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
        UpkeepSchedule.For(hasDecking: false, hasGarden: false).Select(r => r.Id).ShouldBe(["water-rotation", "alarm-tests", "battery-top-up", "stored-cells-check", "first-aid-check", "winter-car", "annual-review"]);
        UpkeepSchedule.For(false, false).Single(r => r.Id == "stored-cells-check").Why.ShouldStartWith("Open the radio and every torch that sits in a drawer and look for leaked cells");
        UpkeepSchedule.For(hasDecking: true, hasGarden: false).Select(r => r.Id).ShouldContain("deck-leaf-litter");
        UpkeepSchedule.For(hasDecking: false, hasGarden: true).Select(r => r.Id).ShouldContain("firebreak-and-hose");
        UpkeepSchedule.For(hasDecking: true, hasGarden: true).Count.ShouldBe(9);
        UpkeepSchedule.For(true, true).ShouldAllBe(r => r.Why.EndsWith('.') && r.Title.Length > 0);
    }

    [Fact]
    public void Occurrences_cover_the_window_in_date_order_and_follow_the_clock_changes()
    {
        IReadOnlyList<Reminder> reminders = UpkeepSchedule.For(hasDecking: true, hasGarden: true);

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
