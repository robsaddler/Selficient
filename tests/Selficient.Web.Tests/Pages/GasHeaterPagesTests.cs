using System.Net;
using System.Text.RegularExpressions;
using Selficient.TestKit;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The portable gas heater from the questionnaire to the plan and the board: asked beside the wood
/// stove, scored as a warm room for a household that has one, and offered to one that needs it.
/// </summary>
public sealed partial class GasHeaterPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "1000");

    private static Dictionary<string, string> Frail(params (string Field, string Value)[] more)
    {
        Dictionary<string, string> form = new()
        {
            ["Form.Postcode"] = "NP12 1GW", ["Form.People"] = "2", ["Form.Children"] = "0", ["Form.HasVulnerablePerson"] = "true", ["Form.HasPets"] = "false",
            ["Form.RunsBusinessFromHome"] = "false", ["Form.DwellingType"] = "Terrace", ["Form.Storeys"] = "2", ["Form.Heating"] = "GasBoiler", ["Form.Hob"] = "Gas",
            ["Form.Car"] = "None", ["Form.HasGarden"] = "false", ["Form.HasDecking"] = "false", ["Form.HasTallTreeNearHouse"] = "false", ["Form.FloodRisk"] = "VeryLow",
        };
        foreach ((string field, string value) in more)
        {
            form[field] = value;
        }

        return form;
    }

    private static async Task<string> PlanAsync(HttpClient client, Dictionary<string, string> form)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = Token().Match(wizard).Groups["token"].Value };
        using HttpResponseMessage response = await client.PostAsync(new Uri("/plan", UriKind.Relative), new FormUrlEncodedContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_questionnaire_asks_about_a_gas_heater_beside_the_wood_stove()
    {
        using HttpClient client = _factory.CreateClient();

        string wizard = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative)));

        wizard.ShouldContain(">Is there a portable gas heater with its own cylinder?</label>");
        wizard.IndexOf("Is there a portable gas heater", StringComparison.Ordinal).ShouldBeGreaterThan(wizard.IndexOf("Is there a wood or multi-fuel stove", StringComparison.Ordinal));
        wizard.ShouldNotContain("field is required.", customMessage: "no framework fragment may reach the page");
    }

    [Fact]
    public async Task A_household_that_needs_a_warm_room_is_offered_a_heater_with_what_it_costs_and_where_to_look()
    {
        using HttpClient client = _factory.CreateClient();

        string house = await PlanAsync(client, Frail());
        string upstairs = await PlanAsync(client, Frail(("Form.DwellingType", "Flat"), ("Form.Storeys", "1"), ("Form.Floor", "1")));
        string ground = await PlanAsync(client, Frail(("Form.DwellingType", "Flat"), ("Form.Storeys", "1"), ("Form.Floor", "0")));

        house.ShouldContain("data-action-id=\"portable-gas-heater\"");
        house.ShouldContain("A portable gas heater and a full cylinder, for one properly warm room");
        house.ShouldContain("about twelve days on a full cylinder, on its low setting from morning to bedtime.");
        house.ShouldContain("never in a bedroom or a bathroom, and turn it off before anyone sleeps.");
        house.ShouldContain("£184 to £234");
        house.ShouldContain("Straame portable gas heater, 4.2 kW, three heat settings");
        house.ShouldContain("Calor 15 kg butane gas bottle, new, with its refill agreement");
        house.ShouldNotContain("data-action-id=\"gas-heater-ready\"");
        house.ShouldContain("name=\"HasGasHeater\" value=\"false\"", customMessage: "the answer is kept with the others");
        house.ShouldContain("heater=false");
        upstairs.ShouldNotContain("data-action-id=\"portable-gas-heater\"", customMessage: "as shipped, a flat above the ground floor is not recommended a gas cylinder");
        ground.ShouldContain("data-action-id=\"portable-gas-heater\"");

        // The plan's total sits at the foot of the four stretches it adds up, above what the plan assumes.
        int total = house.IndexOf("<p class=\"plan__total\"><span class=\"plan__total-label\">Everything together:</span> <span class=\"plan__total-figure data\">£", StringComparison.Ordinal);
        total.ShouldBeGreaterThan(house.IndexOf("data-tier=\"TwelveMonths\"", StringComparison.Ordinal));
        total.ShouldBeLessThan(house.IndexOf("id=\"assumptions-heading\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_household_that_has_one_keeps_its_warm_room_on_the_board_and_is_told_to_keep_a_spare_cylinder()
    {
        using HttpClient client = _factory.CreateClient();

        string without = await PlanAsync(client, Frail());
        string with = await PlanAsync(client, Frail(("Form.HasGasHeater", "true")));

        without.ShouldNotContain("by a portable gas heater and its cylinder.");
        with.ShouldContain("Held for 12 days by a portable gas heater and its cylinder, then lost, because the gas boiler cannot carry it.", customMessage: "in a long cut the boiler has no power and the heater needs none, for as long as the cylinder lasts");
        with.ShouldContain("Warmth holds for 12 days, then not enough.");
        without.ShouldContain("Lost, because the gas boiler cannot carry it.");
        with.ShouldContain("data-action-id=\"gas-heater-ready\"");
        with.ShouldContain("Keep a full spare cylinder for the gas heater, and have the heater checked before winter");
        with.ShouldContain("£58 to £108");
        with.ShouldContain("Calor 15 kg butane gas bottle, a full one for an empty one");
        with.ShouldNotContain("data-action-id=\"portable-gas-heater\"", customMessage: "nobody who has one is sold another");
        with.ShouldContain("name=\"HasGasHeater\" value=\"true\"");
        with.ShouldContain("heater=true");
        with.ShouldContain("Check the gas heater, its hose and the spare cylinder");
        with.ShouldContain("How many days does your gas heater run on the cylinders you keep, at twelve hours a day?", customMessage: "and it may say how long its own cylinders last");

        string calendar = await client.GetStringAsync(new Uri("/plan/calendar.ics?heater=true", UriKind.Relative));
        calendar.ShouldContain("Check the gas heater");
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
