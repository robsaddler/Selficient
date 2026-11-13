using System.Net;
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
    }

    public void Dispose() => _factory.Dispose();

    private sealed class Fixed(ParameterSet set) : ICurrentFigures
    {
        public ParameterSet Current => set;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
