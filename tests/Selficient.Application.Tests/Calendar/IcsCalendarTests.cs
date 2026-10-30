using System.Text;
using Selficient.Application.Calendar;
using Selficient.Domain.Upkeep;

namespace Selficient.Application.Tests.Calendar;

public sealed class IcsCalendarTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 30, 9, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void The_calendar_has_every_required_property()
    {
        IReadOnlyList<Reminder> reminders = UpkeepSchedule.For(false, false);
        IReadOnlyList<(Reminder, DateOnly)> occurrences = UpkeepSchedule.Occurrences(reminders, new DateOnly(2026, 11, 1), 1);

        string ics = IcsCalendar.Write(occurrences, Stamp);
        string[] lines = ics.Split("\r\n");

        lines[0].ShouldBe("BEGIN:VCALENDAR");
        lines.ShouldContain("VERSION:2.0");
        lines.ShouldContain("PRODID:-//Selficient//Household resilience plan//EN");
        lines[^2].ShouldBe("END:VCALENDAR");
        lines[^1].ShouldBeEmpty();
        ics.Count(c => c == '\n').ShouldBe(ics.Split("\r\n").Length - 1, "every line ends with CRLF");
        lines.Count(l => l == "BEGIN:VEVENT").ShouldBe(occurrences.Count);
        lines.Count(l => l == "END:VEVENT").ShouldBe(occurrences.Count);
        lines.Count(l => l.StartsWith("UID:", StringComparison.Ordinal)).ShouldBe(occurrences.Count);
        lines.Where(l => l.StartsWith("UID:", StringComparison.Ordinal)).ShouldBeUnique();
    }

    [Fact]
    public void Dates_are_all_day_and_stamps_are_utc()
    {
        Reminder reminder = new("x", "Title", "Why.", Cadence.Yearly);

        string ics = IcsCalendar.Write([(reminder, new DateOnly(2026, 12, 31))], Stamp);

        ics.ShouldContain("DTSTART;VALUE=DATE:20261231\r\n");
        ics.ShouldContain("DTEND;VALUE=DATE:20270101\r\n");
        ics.ShouldContain("DTSTAMP:20261030T083000Z\r\n");
        ics.ShouldContain("UID:x-20261231@selficient.app\r\n");
    }

    [Fact]
    public void Text_is_escaped_and_lines_are_folded()
    {
        string hostile = "Semi;colon, comma\r\nnewline\nand back\\slash";
        Reminder reminder = new("x", hostile, new string('w', 200) + " é end", Cadence.Yearly);

        string ics = IcsCalendar.Write([(reminder, new DateOnly(2026, 12, 31))], Stamp);
        string[] lines = ics.Split("\r\n");

        ics.ShouldContain("SUMMARY:Semi\\;colon\\, comma\\nnewline\\nand back\\\\slash\r\n");
        lines.ShouldAllBe(l => Encoding.UTF8.GetByteCount(l) <= 75);
        string description = string.Concat(lines.SkipWhile(l => !l.StartsWith("DESCRIPTION:", StringComparison.Ordinal)).TakeWhile((l, i) => i == 0 || l.StartsWith(' ')).Select((l, i) => i == 0 ? l : l[1..]));
        description.ShouldBe("DESCRIPTION:" + new string('w', 200) + " é end");
        IcsCalendar.Escape("plain").ShouldBe("plain");
    }

    [Fact]
    public void Folding_never_splits_a_multibyte_character()
    {
        StringBuilder builder = new();

        IcsCalendar.Line(builder, "X:" + new string('é', 60));
        string[] lines = builder.ToString().Split("\r\n");

        lines.Where(l => l.Length > 0).ShouldAllBe(l => Encoding.UTF8.GetByteCount(l) <= 75 && !l.Contains('�'));
        string.Concat(lines.Select((l, i) => i == 0 ? l : l.TrimStart(' '))).ShouldBe("X:" + new string('é', 60));
    }

    [Fact]
    public void An_empty_line_is_still_terminated()
    {
        StringBuilder builder = new();

        IcsCalendar.Line(builder, string.Empty);

        builder.ToString().ShouldBe("\r\n");
    }

    [Fact]
    public void Guards_reject_nulls()
    {
        Should.Throw<ArgumentNullException>(() => IcsCalendar.Write(null!, Stamp));
        Should.Throw<ArgumentNullException>(() => IcsCalendar.Escape(null!));
        Should.Throw<ArgumentNullException>(() => IcsCalendar.Line(null!, "x"));
        Should.Throw<ArgumentNullException>(() => IcsCalendar.Line(new StringBuilder(), null!));
    }
}
