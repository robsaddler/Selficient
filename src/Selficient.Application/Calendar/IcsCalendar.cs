using System.Globalization;
using System.Text;
using Selficient.Domain.Upkeep;

namespace Selficient.Application.Calendar;

/// <summary>
/// Writes an RFC 5545 calendar of all-day events. Every text value is escaped and every line is
/// folded at 75 octets, so no content can break the structure whatever it contains.
/// </summary>
public static class IcsCalendar
{
    private const string ProductId = "-//Selficient//Household resilience plan//EN";

    /// <summary>Builds the calendar text for the occurrences.</summary>
    public static string Write(IReadOnlyList<(Reminder Reminder, DateOnly Date)> occurrences, DateTimeOffset stamp)
    {
        ArgumentNullException.ThrowIfNull(occurrences);

        StringBuilder calendar = new();
        Line(calendar, "BEGIN:VCALENDAR");
        Line(calendar, "VERSION:2.0");
        Line(calendar, "PRODID:" + ProductId);
        Line(calendar, "CALSCALE:GREGORIAN");
        Line(calendar, "METHOD:PUBLISH");
        Line(calendar, "X-WR-CALNAME:Selficient upkeep");

        string stampText = stamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach ((Reminder reminder, DateOnly date) in occurrences)
        {
            string day = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            Line(calendar, "BEGIN:VEVENT");
            Line(calendar, $"UID:{reminder.Id}-{day}@selficient.app");
            Line(calendar, "DTSTAMP:" + stampText);
            Line(calendar, "DTSTART;VALUE=DATE:" + day);
            Line(calendar, "DTEND;VALUE=DATE:" + date.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Line(calendar, "SUMMARY:" + Escape(reminder.Title));
            Line(calendar, "DESCRIPTION:" + Escape(reminder.Why));
            Line(calendar, "TRANSP:TRANSPARENT");
            Line(calendar, "END:VEVENT");
        }

        Line(calendar, "END:VCALENDAR");
        return calendar.ToString();
    }

    /// <summary>Backslash, semicolon, comma and newlines per RFC 5545 section 3.3.11.</summary>
    internal static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
    }

    /// <summary>Writes one content line, folded at 75 octets with CRLF and a leading space, as the standard requires.</summary>
    internal static void Line(StringBuilder calendar, string content)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(content);

        byte[] bytes = Encoding.UTF8.GetBytes(content);
        int offset = 0;
        bool first = true;
        while (offset < bytes.Length)
        {
            int limit = first ? 75 : 74;
            int take = Math.Min(limit, bytes.Length - offset);

            // Never split a multi-byte character: back up to a byte that starts a sequence.
            while (take > 0 && offset + take < bytes.Length && (bytes[offset + take] & 0xC0) == 0x80)
            {
                take--;
            }

            if (!first)
            {
                calendar.Append(' ');
            }

            calendar.Append(Encoding.UTF8.GetString(bytes, offset, take));
            calendar.Append("\r\n");
            offset += take;
            first = false;
        }

        if (bytes.Length == 0)
        {
            calendar.Append("\r\n");
        }
    }
}
