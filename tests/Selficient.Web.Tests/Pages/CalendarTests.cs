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
        body.ShouldNotContain("half a tank", customMessage: "a link that names no car gets no task about one");
    }

    [Theory]
    [InlineData("petrol=true", "Keep the car above half a tank from now until March", "half charge")]
    [InlineData("electric=true", "Keep the car above half charge from now until March", "half a tank")]
    [InlineData("petrol=true&electric=true", "Keep the car above half a tank from now until March", "half charge")]
    public async Task The_car_task_is_for_the_car_there_is(string query, string expected, string absent)
    {
        using HttpClient client = _factory.CreateClient();

        string body = await client.GetStringAsync(new Uri("/plan/calendar.ics?" + query + "&tree=true&stove=true&flood=true", UriKind.Relative));

        body.ShouldContain("SUMMARY:" + expected);
        body.ShouldNotContain(absent);
        body.ShouldContain("SUMMARY:Have the stove's flue swept and fill the log store");
        body.ShouldContain("SUMMARY:Check your flood warnings and walk through the flood plan");
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
    public void The_upkeep_view_links_only_the_yes_or_no_facts_and_words_the_cadences()
    {
        HouseholdProfile profile = Profiles.RobsHouse();

        UpkeepView view = new(profile);

        view.CalendarHref.ShouldBe("/plan/calendar.ics?decking=true&garden=true&petrol=true&electric=false&tree=true&stove=false&flood=false&meter=false&supply=false&tank=false&lift=false&heater=false");
        view.Reminders.Count.ShouldBe(9);
        UpkeepView.When(new UpkeepView(Profiles.FloodZoneBungalow()).Reminders.Single(r => r.Id == "flue-sweep")).ShouldBe("1 September");
        UpkeepView.When(view.Reminders.Single(r => r.Id == "stored-cells-check")).ShouldBe("At the clock changes, March and October");
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
