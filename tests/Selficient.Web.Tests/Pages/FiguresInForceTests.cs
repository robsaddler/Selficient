using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Figures;
using Selficient.Domain.Parameters;
using Selficient.TestKit;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// A plan is built from the figures in force when it is asked for: a published correction is on the
/// next page a household opens, in the plan's own words and on the board.
/// </summary>
public sealed partial class FiguresInForceTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static Dictionary<string, string> House() => new()
    {
        ["Form.Postcode"] = "NP12 1GW", ["Form.People"] = "4", ["Form.Children"] = "2", ["Form.HasVulnerablePerson"] = "false", ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "false", ["Form.DwellingType"] = "SemiDetached", ["Form.Storeys"] = "2", ["Form.Heating"] = "GasBoiler", ["Form.Hob"] = "Electric",
        ["Form.Car"] = "Petrol", ["Form.HasGarden"] = "true", ["Form.HasDecking"] = "false", ["Form.HasTallTreeNearHouse"] = "false", ["Form.FloodRisk"] = "VeryLow",
    };

    private static async Task<string> PlanAsync(HttpClient client)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        Dictionary<string, string> body = new(House()) { ["__RequestVerificationToken"] = Token().Match(wizard).Groups["token"].Value };
        using HttpResponseMessage response = await client.PostAsync(new Uri("/plan", UriKind.Relative), new FormUrlEncodedContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_host_with_nothing_published_builds_from_the_shipped_figures_it_read_at_start()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await PlanAsync(client);

        CurrentFigures current = _factory.Services.GetRequiredService<CurrentFigures>();
        current.IsLoaded.ShouldBeTrue("the store was read when the host started");
        _factory.Services.GetRequiredService<ICurrentFigures>().ShouldBeSameAs(current);
        _factory.Services.GetRequiredService<FigureDesk>().ShouldNotBeNull();
        html.ShouldContain("36 litres of sealed bottled water");
        html.ShouldContain("A 210-litre water butt on a downpipe");
        html.ShouldContain("data-plan data-hero data-figures-version=\"0\"", customMessage: "the plan says which set it was built with");
        html.ShouldContain("<p class=\"callout\" role=\"status\" data-figures-updated hidden>The figures behind your plan have been updated, and this is your plan worked out from them. Nothing you have ticked has changed.</p>");
    }

    [Fact]
    public async Task The_browser_is_told_which_set_is_in_force_and_the_figures_a_household_may_give()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/figures", UriKind.Relative));
        FiguresAnswer answer = (await response.Content.ReadFromJsonAsync<FiguresAnswer>(Web))!;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue("the point of asking is to learn that the number has moved");
        answer.Version.ShouldBe(0);
        answer.PublishedAt.ShouldBeNull();
        answer.Figures.Select(figure => figure.Id).ShouldBe([.. ParameterRegistry.All.Where(parameter => parameter.HouseholdMayOverride).Select(parameter => parameter.Id)]);
        answer.Figures.Single(figure => figure.Id == "stairlift-batteries.stock").ShouldBe(new FigureAnswer("stairlift-batteries.stock", "How many days of trips to bed and the bathroom does your stairlift make in a power cut?", "days", 0m, 100m, false, 2m));
        answer.Figures.Single(figure => figure.Id == "assumed.cash-pounds-a-person-a-day").Whole.ShouldBeTrue();
    }

    [Fact]
    public async Task The_figures_the_script_ships_with_are_the_figures_the_server_ships_with()
    {
        string script = await File.ReadAllTextAsync(Path.Combine(SolutionRoot(), "src", "Selficient.Web", "wwwroot", "js", "figures-shipped.js"));
        string json = script[script.IndexOf("export const SHIPPED = ", StringComparison.Ordinal)..].Replace("export const SHIPPED = ", string.Empty, StringComparison.Ordinal).TrimEnd().TrimEnd(';');

        FiguresAnswer inScript = JsonSerializer.Deserialize<FiguresAnswer>(json, Web)!;
        Selficient.Web.Endpoints.FiguresResponse onServer = Selficient.Web.Endpoints.FiguresEndpoint.For(ParameterSet.Default, null);

        inScript.Version.ShouldBe(0);
        inScript.PublishedAt.ShouldBeNull();
        inScript.Figures.ShouldBe([.. onServer.Figures.Select(figure => new FigureAnswer(figure.Id, figure.Question, figure.Unit, figure.Min, figure.Max, figure.Whole, figure.Value))], "a browser that has never reached the server must start from the same figures");
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Endpoints.FiguresEndpoint.For(null!, null));
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Endpoints.FiguresEndpoint.MapFiguresEndpoint(null!));
    }

    [Fact]
    public async Task A_published_correction_is_on_the_next_plan_in_its_words_its_quantities_and_its_prices()
    {
        ICurrentFigures corrected = new Fixed(ParameterSet.Create(6, new Dictionary<string, decimal>
        {
            ["water-bottled-three-days.stock"] = 12m, ["water-butt.stock"] = 250m, ["price.radio-dab-fm-with-crank.high"] = 175m,
        }).Value);
        using WebApplicationFactory<Program> host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(corrected)));
        using HttpClient client = host.CreateClient();

        string html = await PlanAsync(client);

        html.ShouldContain("48 litres of sealed bottled water");
        html.ShouldContain("Four days of drinking water for 4 people.");
        html.ShouldContain("A 250-litre water butt on a downpipe");
        html.ShouldContain("£30 to £175");
        html.ShouldNotContain("36 litres of sealed bottled water");
        html.ShouldContain("data-figures-version=\"6\"");
        html.ShouldContain("The figures behind your plan were updated on 14 November 2026, and this is your plan worked out from them. Nothing you have ticked has changed.");

        FiguresAnswer answer = (await client.GetFromJsonAsync<FiguresAnswer>(new Uri("/api/figures", UriKind.Relative), Web))!;
        answer.Version.ShouldBe(6);
        answer.PublishedAt.ShouldBe("2026-11-14T09:30:00Z");
        answer.Figures.Single(figure => figure.Id == "water-butt.stock").Value.ShouldBe(250m, "the value in force, not the shipped one");
    }

    public void Dispose() => _factory.Dispose();

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string SolutionRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Selficient.slnx")))
        {
            directory = directory.Parent!;
        }

        return directory.FullName;
    }

    private sealed record FigureAnswer(string Id, string Question, string Unit, decimal Min, decimal Max, bool Whole, decimal Value);

    private sealed record FiguresAnswer(int Version, string? PublishedAt, IReadOnlyList<FigureAnswer> Figures);

    private sealed class Fixed(ParameterSet set) : ICurrentFigures
    {
        public ParameterSet Current => set;

        public DateTimeOffset? PublishedAt => new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
