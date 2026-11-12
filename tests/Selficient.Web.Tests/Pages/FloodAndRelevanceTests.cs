using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Geography;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Infrastructure.Geography;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The plan fits the answers: what the flood map says is looked up and said, whatever it says; an
/// action sends a household to its own nation's service; and nothing is sold to a house that said
/// it has one already or cannot use it.
/// </summary>
public sealed partial class FloodAndRelevanceTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static Dictionary<string, string> House(string postcode, string flood = "Unknown") => new()
    {
        ["Form.Postcode"] = postcode,
        ["Form.People"] = "4",
        ["Form.Children"] = "2",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "false",
        ["Form.DwellingType"] = "SemiDetached",
        ["Form.Storeys"] = "2",
        ["Form.Heating"] = "GasBoiler",
        ["Form.Hob"] = "Electric",
        ["Form.HasCar"] = "true",
        ["Form.HasGarden"] = "true",
        ["Form.HasDecking"] = "false",
        ["Form.HasTallTreeNearHouse"] = "false",
        ["Form.FloodRisk"] = flood,
    };

    private async Task<string> PlanAsync(Dictionary<string, string> form)
    {
        using HttpClient client = _factory.CreateClient();
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        Dictionary<string, string> body = new(form) { ["__RequestVerificationToken"] = Token().Match(wizard).Groups["token"].Value };
        using HttpResponseMessage response = await client.PostAsync(new Uri("/plan", UriKind.Relative), new FormUrlEncodedContent(body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A postcode in the given area that the stand-in map answers as wanted, found the way the site finds it.</summary>
    private static async Task<string> PostcodeWhereAsync(string area, FloodRisk band, FloodWater from)
    {
        FakeGeocoder geocoder = new();
        FakeFloodRiskProvider map = new();
        for (int district = 1; district < 60; district++)
        {
            foreach (char letter in "ABDEFGHJLNPQRSTUWXYZ")
            {
                string candidate = $"{area}{district} 1{letter}{letter}";
                Locality place = (await geocoder.LocateAsync(Postcode.Create(candidate).Value, CancellationToken.None)).Value;
                Domain.Primitives.Result<FloodFinding> found = await map.AtAsync(place.Coordinates, place.Nation, CancellationToken.None);
                if (found.IsSuccess && found.Value.Band == band && found.Value.From == from)
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("The stand-in map gives no such postcode.");
    }

    [Fact]
    public async Task The_wizard_offers_to_look_the_flood_map_up_and_says_what_the_postcode_is_used_for()
    {
        using HttpClient client = _factory.CreateClient();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative)));

        html.ShouldContain("How likely is flooding where you live?");
        html.ShouldContain("<option selected=\"selected\" value=\"Unknown\">Look it up for me</option>");
        html.ShouldNotContain("I have not checked yet");
        html.ShouldContain("Leave this as it is and the plan looks your postcode up on your nation's flood map when it is built.");
        html.ShouldContain("If you know the band for your own address, choose it: an address is more exact than a postcode. You can check yours for");
        html.ShouldContain("<a href=\"https://www.gov.uk/check-long-term-flood-risk\" rel=\"noopener noreferrer\" target=\"_blank\">England</a>,");
        html.ShouldContain("<a href=\"https://naturalresources.wales/flooding/check-your-flood-risk-by-postcode/\" rel=\"noopener noreferrer\" target=\"_blank\">Wales</a>,");
        html.ShouldContain("<a href=\"https://beta.sepa.scot/flooding/flood-maps/\" rel=\"noopener noreferrer\" target=\"_blank\">Scotland</a> or");
        html.ShouldContain("<a href=\"https://www.nidirect.gov.uk/articles/check-risk-flooding-your-area\" rel=\"noopener noreferrer\" target=\"_blank\">Northern Ireland</a>.");
        html.ShouldContain("Nothing here leaves your browser except the postcode, which we use once to find your area, your height above sea level and what the flood map says there. We do not store it.");
        html.ShouldContain("data-wizard-start");

        // The car is asked about once: whether, and what kind, in one answer.
        html.ShouldContain("<label for=\"Form_Car\">Is there a car?</label>");
        html.ShouldContain("<option value=\"None\">No</option>");
        html.ShouldContain("<option value=\"Petrol\">Yes, petrol or diesel</option>");
        html.ShouldContain("<option value=\"Electric\">Yes, electric</option>");
        html.ShouldNotContain("If there is a car, what kind?");
        html.ShouldNotContain("id=\"Form_HasCar\"", customMessage: "a tick beside a list that says there is no car was two answers to one question");
        html.ShouldContain("<p class=\"wizard__kept\" data-wizard-kept role=\"status\" hidden>These are the answers your plan was built from. Change what is different and build it again; what you have ticked stays as it is.</p>");
    }

    [Fact]
    public async Task A_very_low_postcode_is_told_it_was_looked_at_and_gets_nothing_about_flooding()
    {
        string html = await PlanAsync(House("NP12 1GW"));

        html.ShouldContain("data-flood-origin=\"Map\" data-flood-band=\"VeryLow\"");
        html.ShouldContain("<strong>Flooding.</strong>");
        html.ShouldContain("We looked the middle of your postcode up on Natural Resources Wales's flood map and it shows a very low risk from rivers, the sea and surface water, so there is nothing about flooding in your plan.");
        html.ShouldContain("A postcode is not a front door. If your own address says something different, change your answer and the plan follows.");
        html.ShouldContain("<a href=\"https://naturalresources.wales/flooding/check-your-flood-risk-by-postcode/\" rel=\"noopener noreferrer\" target=\"_blank\">Check your address with Natural Resources Wales<span class=\"sr-only\"> (opens in a new tab)</span></a>");
        html.ShouldNotContain("data-action-id=\"flood-warnings-and-plan\"");
        html.ShouldNotContain("data-action-id=\"check-flood-risk\"");
        html.ShouldContain("name=\"FloodRisk\" value=\"Unknown\"", customMessage: "the browser keeps the answer as given, so the map is read again when it has changed");
        html.ShouldContain("data-change-answers>Change your answers</a>");
        html.ShouldNotContain(">Start again<");
    }

    [Fact]
    public async Task A_river_postcode_gets_the_warnings_of_its_own_nation_and_the_odds_its_map_draws()
    {
        string england = await PlanAsync(House(await PostcodeWhereAsync("YO", FloodRisk.High, FloodWater.RiversOrSea)));

        england.ShouldContain("data-flood-origin=\"Map\" data-flood-band=\"High\"");
        england.ShouldContain("We looked the middle of your postcode up on the Environment Agency's flood map and it shows a high risk from rivers or the sea, so flooding is in your plan.");
        england.ShouldContain("The national flood map puts your area at high risk from rivers or the sea, which means more than a one in thirty chance in any year. Flooding goes to the top of your plan.");
        england.ShouldContain("Sign up for free flood warnings and write a one-page flood plan");
        england.ShouldContain("<a class=\"action__official\" href=\"https://www.gov.uk/get-flood-warnings\" rel=\"noopener noreferrer\" target=\"_blank\" data-official-link>Sign up for flood warnings on GOV.UK<span class=\"sr-only\"> (opens in a new tab)</span></a>");
        england.ShouldContain("data-action-id=\"flood-protection-kit\"");
        england.ShouldContain("Check your flood warnings and walk through the flood plan", customMessage: "and the upkeep keeps the plan alive");
        england.ShouldContain("&flood=true");

        string scotland = await PlanAsync(House(await PostcodeWhereAsync("PH", FloodRisk.Medium, FloodWater.RiversOrSea)));
        scotland.ShouldContain("on SEPA's flood map and it shows a medium risk from rivers or the sea");
        scotland.ShouldContain("which on Scotland's map is between a one in two hundred and a one in ten chance in any year.");
        scotland.ShouldContain("href=\"https://www.sepa.org.uk/flooding/floodline-register/\"");
    }

    [Fact]
    public async Task Surface_water_is_not_told_to_sign_up_for_a_warning_nobody_sends()
    {
        string html = await PlanAsync(House(await PostcodeWhereAsync("LS", FloodRisk.Low, FloodWater.SurfaceWater)));

        html.ShouldContain("it shows a low risk from surface water, so flooding is in your plan.");
        html.ShouldContain("Write a one-page flood plan, and watch the Met Office warnings for heavy rain");
        html.ShouldContain("The risk here is surface water, which comes with heavy rain, too fast for a flood warning service to call ahead");
        html.ShouldContain("href=\"https://weather.metoffice.gov.uk/warnings-and-advice/uk-warnings\"");
        html.ShouldNotContain("Sign up for free flood warnings");
    }

    [Fact]
    public async Task Where_no_map_can_be_read_the_plan_says_so_and_starts_with_checking()
    {
        string northernIreland = await PlanAsync(House("BT1 1AA"));

        northernIreland.ShouldContain("data-flood-origin=\"NotKnown\" data-flood-band=\"Unknown\"");
        northernIreland.ShouldContain("Northern Ireland's flood map is one to look at, not one we can ask for you, so your plan starts by asking you to check it.");
        northernIreland.ShouldContain("data-action-id=\"check-flood-risk\"");
        northernIreland.ShouldContain("Check what the flood map says about your address");
        northernIreland.ShouldContain("<a class=\"action__official\" href=\"https://www.nidirect.gov.uk/articles/check-risk-flooding-your-area\" rel=\"noopener noreferrer\" target=\"_blank\" data-official-link>Check your area on nidirect<span class=\"sr-only\"> (opens in a new tab)</span></a>");
        northernIreland.ShouldNotContain("data-action-id=\"flood-warnings-and-plan\"");

        string shetland = await PlanAsync(House("ZE2 9ZZ"));
        shetland.ShouldContain("data-flood-origin=\"NotKnown\"");
        shetland.ShouldContain("We could not read the flood map for your postcode just now, so your plan starts by asking you to check it.");
        shetland.ShouldContain("href=\"https://beta.sepa.scot/flooding/flood-maps/\"");
    }

    [Fact]
    public async Task What_the_household_says_about_its_own_address_stands_over_the_map()
    {
        string html = await PlanAsync(House(await PostcodeWhereAsync("YO", FloodRisk.High, FloodWater.RiversOrSea), flood: "Low"));

        html.ShouldContain("data-flood-origin=\"Household\" data-flood-band=\"Low\"");
        html.ShouldContain("You told us the flood map shows a low risk at your address, and the plan is built on that.");
        html.ShouldContain("If that changes, change your answer and the plan follows.");
        html.ShouldContain("The national flood map puts your area at low risk, which is still up to a one in a hundred chance in any year.");
        html.ShouldContain("name=\"FloodRisk\" value=\"Low\"");
    }

    [Fact]
    public async Task The_plan_is_built_from_the_later_answers_too()
    {
        Dictionary<string, string> answered = House("NP12 1GW");
        answered["Form.Car"] = "Electric";
        answered["Form.HasWoodStove"] = "true";
        answered["Form.HasWaterButt"] = "true";
        answered["Form.HasPets"] = "true";
        answered["Form.Infants"] = "1";

        string html = await PlanAsync(answered);

        html.ShouldContain("data-action-id=\"car-above-half-charge\"");
        html.ShouldContain("Keep the car above half charge from November to March");
        html.ShouldNotContain("half a tank", customMessage: "nowhere on the page: not the plan, not the upkeep");
        html.ShouldNotContain("data-action-id=\"water-butt\"");
        html.ShouldContain("data-action-id=\"wood-stove-ready\"");
        html.ShouldContain("The stove keeps one room warm.");
        html.ShouldContain("data-action-id=\"pet-food-two-weeks\"");
        html.ShouldContain("data-action-id=\"formula-and-nappies\"");
        html.ShouldContain("Have the stove's flue swept and fill the log store");
        html.ShouldContain("&petrol=false&electric=true&tree=false&stove=true");

        Dictionary<string, string> petrol = House("NP12 1GW");
        petrol["Form.Car"] = "Petrol";
        string plain = await PlanAsync(petrol);
        plain.ShouldContain("data-action-id=\"car-above-half-a-tank\"");
        plain.ShouldContain("data-action-id=\"water-butt\"");
        plain.ShouldNotContain("data-action-id=\"wood-stove-ready\"");
        plain.ShouldNotContain("data-action-id=\"pet-food-two-weeks\"");
        plain.ShouldNotContain("data-action-id=\"formula-and-nappies\"");

        Dictionary<string, string> noCar = House("NP12 1GW");
        noCar["Form.HasCar"] = "false";
        string walking = await PlanAsync(noCar);
        walking.ShouldNotContain("data-action-id=\"car-above-half-a-tank\"");
        walking.ShouldNotContain("Keep the car above half", customMessage: "a house with no car is given no task about one, in the plan or the upkeep");
        walking.ShouldContain("&petrol=false&electric=false");
    }

    [Fact]
    public async Task A_flat_with_nowhere_outside_is_offered_flasks_and_never_a_camping_stove()
    {
        Dictionary<string, string> flat = House("NP12 1GW");
        flat["Form.DwellingType"] = "Flat";
        flat["Form.Storeys"] = "1";
        flat["Form.HasGarden"] = "false";
        flat["Form.HasCar"] = "false";

        string html = await PlanAsync(flat);

        html.ShouldContain("data-action-id=\"flasks-for-hot-food\"");
        html.ShouldContain("A camping stove is not safe indoors and a flat has nowhere outside to use one, so this plan does not ask you to buy one.");
        html.ShouldNotContain("data-action-id=\"butane-cartridge-stove\"");
    }

    [Fact]
    public void The_flood_line_says_where_the_band_came_from_in_every_case()
    {
        FloodCopy.Says(new FloodAnswer(FloodRisk.Unknown, FloodOrigin.NotKnown), Nation.England).ShouldBe("We could not read the flood map for your postcode just now, so your plan starts by asking you to check it.");
        FloodCopy.Says(new FloodAnswer(FloodRisk.Unknown, FloodOrigin.NotKnown), Nation.NorthernIreland).ShouldStartWith("Northern Ireland's flood map is one to look at");
        FloodCopy.Says(new FloodAnswer(FloodRisk.Medium, FloodOrigin.Household), Nation.Scotland).ShouldBe("You told us the flood map shows a medium risk at your address, and the plan is built on that.");
        FloodCopy.Says(new FloodAnswer(FloodRisk.VeryLow, FloodOrigin.Household), Nation.Scotland).ShouldBe("You told us the flood map shows a very low risk at your address, and the plan is built on that.");
        FloodCopy.Says(new FloodAnswer(FloodRisk.High, FloodOrigin.Map, FloodWater.RiversOrSea, "SEPA's flood map"), Nation.Scotland).ShouldBe("We looked the middle of your postcode up on SEPA's flood map and it shows a high risk from rivers or the sea, so flooding is in your plan.");
        FloodCopy.Says(new FloodAnswer(FloodRisk.Low, FloodOrigin.Map, FloodWater.NotSaid, "a map"), Nation.Scotland).ShouldBe("We looked the middle of your postcode up on a map and it shows a low risk of flooding, so flooding is in your plan.");
        FloodCopy.OwnAddress(new FloodAnswer(FloodRisk.Unknown, FloodOrigin.NotKnown)).ShouldStartWith("A postcode is not a front door.");
        FloodCopy.OwnAddress(new FloodAnswer(FloodRisk.Low, FloodOrigin.Household)).ShouldBe("If that changes, change your answer and the plan follows.");
        FloodCopy.CheckLinks(Nation.Unknown).ShouldBeEmpty("a place whose nation is not known is given no nation's service");
        FloodCopy.CheckLinks(Nation.Scotland).ShouldHaveSingleItem().Text.ShouldBe("Check your address on SEPA's flood maps");
        FloodCopy.Nations.Select(n => FloodCopy.NationName(n.Nation)).ShouldBe(["England", "Wales", "Scotland", "Northern Ireland"]);
        FloodCopy.Heading.ShouldBe("Flooding");
        Should.Throw<ArgumentNullException>(() => FloodCopy.Says(null!, Nation.England));
        Should.Throw<ArgumentNullException>(() => FloodCopy.OwnAddress(null!));
    }

    [Fact]
    public async Task The_legal_pages_name_the_flood_maps_what_they_are_sent_and_whose_data_they_are()
    {
        using HttpClient client = _factory.CreateClient();

        string privacy = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/privacy", UriKind.Relative)));
        string terms = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/terms", UriKind.Relative)));
        string landing = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/", UriKind.Relative)));

        privacy.ShouldContain("The Environment Agency, Natural Resources Wales or SEPA, whichever keeps the flood map for your nation");
        privacy.ShouldContain("a box fifty metres across round your postcode's centre");
        privacy.ShouldContain("to read the flood map there, only when you leave the flood question for us to look up; Northern Ireland has no such service, so nothing is sent for a postcode there");
        privacy.ShouldContain("It goes to public lookups to find your area, your height above sea level and what the flood map says there, and it is not kept afterwards.");
        terms.ShouldContain("Contains Environment Agency information © Environment Agency and/or database right.");
        terms.ShouldContain("Contains Natural Resources Wales information © Natural Resources Wales and database right.");
        terms.ShouldContain("Contains SEPA data © Scottish Environment Protection Agency and database right.");
        terms.ShouldContain("Each is an indication for an area, not an assessment of a property.");
        landing.ShouldContain("a burst main, a river in flood, a road lost to snow, a tall tree, leaf litter under decking.");
        landing.ShouldContain("Your postcode, your height above sea level, what the flood map says there and a few facts about the house decide which come first.");
        landing.ShouldContain("The flood map we look up for you.");
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
