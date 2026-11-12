using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Selficient.Application.Calendar;
using Selficient.Domain.Upkeep;
using Selficient.Web.Security;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// <c>GET /plan/calendar.ics?decking=true&amp;garden=true…</c>: the upkeep calendar as a download.
/// Seven booleans in, static copy and computed dates out; nothing personal passes through.
/// </summary>
[EnableRateLimiting(RateLimits.ApiPolicy)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CalendarModel : PageModel
{
    /// <summary>How far ahead the calendar reaches.</summary>
    public const int Years = 2;

    private readonly TimeProvider _clock;

    /// <summary>Creates the page.</summary>
    public CalendarModel(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Returns the calendar file.</summary>
    public IActionResult OnGet(bool decking = false, bool garden = false, bool petrol = false, bool electric = false, bool tree = false, bool stove = false, bool flood = false)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        // One car at most: a link that claims both is given the tank, which is what an old link meant.
        IReadOnlyList<Reminder> reminders = UpkeepSchedule.For(new UpkeepFacts(decking, garden, petrol, electric && !petrol, tree, stove, flood));
        string calendar = IcsCalendar.Write(UpkeepSchedule.Occurrences(reminders, today, Years), now);

        return File(System.Text.Encoding.UTF8.GetBytes(calendar), "text/calendar; charset=utf-8", "selficient-calendar.ics");
    }
}
