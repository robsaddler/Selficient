using System.Net;
using Selficient.Domain.Households;
using Selficient.Domain.Upkeep;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

public sealed class CalendarTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Fact]
    public async Task The_download_has_a_fixed_type_and_name()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/plan/calendar.ics?decking=true&garden=true", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/calendar; charset=utf-8");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("selficient-calendar.ics");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        body.ShouldStartWith("BEGIN:VCALENDAR\r\n");
        body.ShouldContain("SUMMARY:Clear the leaf litter from under the decking");
        body.ShouldContain("SUMMARY:Walk the firebreak and check the hose and outside tap");
    }

    [Fact]
    public async Task Unknown_or_malformed_query_values_are_ignored()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/plan/calendar.ics?decking=maybe&garden=<script>&extra=1", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("leaf litter");
        body.ShouldNotContain("firebreak");
        body.ShouldNotContain("<script>");
        body.ShouldContain("SUMMARY:Refill the stored water");
    }

    [Fact]
    public async Task Rate_limit_applies_to_the_calendar()
    {
        using SelficientWebApplicationFactory limited = new SelficientWebApplicationFactory().WithSetting("RateLimits:ApiPerMinute", "1");
        using HttpClient client = limited.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/plan/calendar.ics", UriKind.Relative));
        using HttpResponseMessage second = await client.GetAsync(new Uri("/plan/calendar.ics", UriKind.Relative));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void The_upkeep_view_links_only_the_two_booleans_and_words_the_cadences()
    {
        Dwelling dwelling = Profiles.RobsHouse().Dwelling;

        UpkeepView view = new(dwelling);

        view.CalendarHref.ShouldBe("/plan/calendar.ics?decking=true&garden=true");
        view.Reminders.Count.ShouldBe(8);
        UpkeepView.When(view.Reminders.Single(r => r.Id == "water-rotation")).ShouldBe("At the clock changes, March and October");
        UpkeepView.When(view.Reminders.Single(r => r.Id == "alarm-tests")).ShouldBe("Monthly");
        UpkeepView.When(view.Reminders.Single(r => r.Id == "battery-top-up")).ShouldBe("Every three months");
        UpkeepView.When(view.Reminders.Single(r => r.Id == "annual-review")).ShouldBe("Every October");
        UpkeepView.When(view.Reminders.Single(r => r.Id == "deck-leaf-litter")).ShouldBe("15 November and 1 May");
        Should.Throw<ArgumentNullException>(() => new UpkeepView(null!));
        Should.Throw<ArgumentNullException>(() => UpkeepView.When(null!));
        Should.Throw<ArgumentNullException>(() => new CalendarModel(null!));
    }

    public void Dispose() => _factory.Dispose();
}
