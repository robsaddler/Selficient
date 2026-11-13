using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Planning;
using Selficient.Domain.Households;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

/// <summary>
/// The plan is only offered what this household can do: the wizard asks who owns the home, where
/// its water comes from and goes, how the energy is paid for, who would need help to leave and
/// where the water would arrive first, and the page that comes back follows every answer.
/// </summary>
public sealed partial class CircumstancesPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private static Dictionary<string, string> House(string flood = "VeryLow") => new()
    {
        ["Form.Postcode"] = "NP12 1GW",
        ["Form.People"] = "4",
        ["Form.Children"] = "2",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "false",
        ["Form.DwellingType"] = "SemiDetached",
        ["Form.Storeys"] = "2",
        ["Form.Heating"] = "GasBoiler",
        ["Form.Hob"] = "Electric",
        ["Form.Car"] = "Petrol",
        ["Form.HasGarden"] = "true",
        ["Form.HasDecking"] = "false",
        ["Form.HasTallTreeNearHouse"] = "true",
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

    [Fact]
    public async Task The_wizard_asks_whose_home_it_is_what_it_is_connected_to_and_who_would_need_help()
    {
        using HttpClient client = _factory.CreateClient();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative)));

        html.ShouldContain("<label for=\"Form_NeedsHelpToLeave\">Would anyone need help to get downstairs or out of the home in an emergency?</label>");
        html.ShouldContain("<label for=\"Form_Tenure\">Is the home yours or rented?</label>");
        html.ShouldContain("<option value=\"Owner\">It is ours, with or without a mortgage</option>");
        html.ShouldContain("<option value=\"Tenant\">It is rented</option>");
        html.ShouldContain("A rented home is not offered what only its owner can do, such as panels on the roof.");
        html.ShouldContain("<label for=\"Form_HasCellar\">Is there a cellar or basement?</label>");
        html.ShouldContain("<label for=\"Form_SleepsDownstairs\">Does anyone sleep on the ground floor or below it?</label>");
        html.ShouldContain("<legend>Water, drains and the meter</legend>");
        html.ShouldContain("<label for=\"Form_Water\">Where does your water come from?</label>");
        html.ShouldContain("<option value=\"Mains\">The mains</option>");
        html.ShouldContain("<option value=\"OwnPumped\">Our own borehole or well, with a pump</option>");
        html.ShouldContain("<option value=\"OwnGravity\">Our own spring or tank, with no pump</option>");
        html.ShouldContain("<label for=\"Form_OwnDrains\">Does the waste go to a septic tank, cesspit or treatment plant, and not a sewer?</label>");
        html.ShouldContain("<label for=\"Form_PrepaymentMeter\">Is the gas or electricity on a prepayment meter?</label>");
    }

    [Fact]
    public async Task A_rented_home_is_not_sold_its_landlords_roof_boiler_or_tree()
    {
        Dictionary<string, string> rented = House();
        rented["Form.Tenure"] = "Tenant";

        string html = await PlanAsync(rented);

        html.ShouldNotContain("data-action-id=\"solar-quotes\"");
        html.ShouldNotContain("data-action-id=\"rooftop-solar-and-battery\"");
        html.ShouldNotContain("data-action-id=\"arborist-survey\"");
        html.ShouldNotContain("data-action-id=\"tree-work-if-advised\"");
        html.ShouldContain("data-action-id=\"tree-tell-landlord\"");
        html.ShouldContain("Tell your landlord in writing about the tall tree");
        html.ShouldContain("Ask your landlord to have an electrician fit a plug-and-socket on the boiler's spur");
        html.ShouldContain("It needs nothing fitted and goes with you when you move, which makes it the backup a rented home can have");
        html.ShouldContain("Ask your landlord before a diverter is cut into the downpipe.");
        html.ShouldContain("name=\"Tenure\" value=\"Tenant\"", customMessage: "the browser keeps the answer");

        string owned = await PlanAsync(House());
        owned.ShouldContain("data-action-id=\"solar-quotes\"");
        owned.ShouldContain("data-action-id=\"arborist-survey\"");
        owned.ShouldNotContain("data-action-id=\"tree-tell-landlord\"");
        owned.ShouldNotContain("landlord");
        owned.ShouldContain("name=\"Tenure\" value=\"Owner\"", customMessage: "a plan made before the question is an owner's");
        owned.ShouldContain("name=\"Water\" value=\"Mains\"");
    }

    [Fact]
    public async Task A_home_on_its_own_water_and_tank_with_a_prepayment_meter_gets_a_plan_for_those()
    {
        Dictionary<string, string> remote = House();
        remote["Form.Water"] = "OwnPumped";
        remote["Form.OwnDrains"] = "true";
        remote["Form.PrepaymentMeter"] = "true";

        string html = await PlanAsync(remote);

        html.ShouldContain("data-action-id=\"own-water-in-a-power-cut\"");
        html.ShouldContain("Find out what your water supply does in a power cut");
        html.ShouldContain("data-action-id=\"own-drains-in-a-power-cut\"");
        html.ShouldContain("data-action-id=\"prepayment-credit-ahead\"");
        html.ShouldContain("Keep a week of credit on the prepayment meter through the winter");
        html.ShouldNotContain("data-action-id=\"no-flush-toilet-kit\"", customMessage: "a tank of its own does not stop with the sewers");
        html.ShouldContain("Save 105, whoever looks after your water supply and the weather warnings on every phone");
        html.ShouldContain("Register with your electricity network's Priority Services Register");
        html.ShouldNotContain("your water company");
        html.ShouldContain("Your water is lifted by an electric pump, so every power cut is a water cut as well.");
        html.ShouldContain("Build up credit on the prepayment meter before winter");
        html.ShouldContain("Have your water supply tested");
        html.ShouldContain("Check when the tank was last emptied");
        html.ShouldContain("&meter=true&supply=true&tank=true&lift=false");
        html.ShouldContain("name=\"Water\" value=\"OwnPumped\"");
        html.ShouldContain("name=\"OwnDrains\" value=\"true\"");
        html.ShouldContain("name=\"PrepaymentMeter\" value=\"true\"");

        string mains = await PlanAsync(House());
        mains.ShouldContain("data-action-id=\"no-flush-toilet-kit\"");
        mains.ShouldContain("&meter=false&supply=false&tank=false&lift=false");
        mains.ShouldNotContain("data-action-id=\"prepayment-credit-ahead\"");
    }

    [Fact]
    public async Task Someone_who_would_need_help_to_leave_gets_a_plan_for_it_and_the_registers()
    {
        Dictionary<string, string> helped = House();
        helped["Form.Children"] = "0";
        helped["Form.NeedsHelpToLeave"] = "true";

        string html = await PlanAsync(helped);

        html.ShouldContain("data-action-id=\"help-to-leave-plan\"");
        html.ShouldContain("Agree who helps whom to get out, and walk it through once");
        html.ShouldContain("data-action-id=\"priority-services-register\"");
        html.ShouldContain("name=\"NeedsHelpToLeave\" value=\"true\"");

        Dictionary<string, string> alone = House();
        alone["Form.Children"] = "0";
        string plain = await PlanAsync(alone);
        plain.ShouldNotContain("data-action-id=\"help-to-leave-plan\"");
        plain.ShouldNotContain("data-action-id=\"priority-services-register\"");
    }

    [Fact]
    public async Task A_flood_plan_follows_the_floor_the_cellar_and_where_people_sleep()
    {
        Dictionary<string, string> cellar = House("High");
        cellar["Form.HasCellar"] = "true";
        cellar["Form.SleepsDownstairs"] = "true";
        string house = await PlanAsync(cellar);

        house.ShouldContain("data-action-id=\"flood-protection-kit\"");
        house.ShouldContain("data-action-id=\"flood-water-alarm\"");
        house.ShouldContain("A battery water alarm on the cellar floor");
        house.ShouldContain("A cellar fills first, often up through its own floor");
        house.ShouldContain("Someone sleeps downstairs, so agree where they sleep on a night with a warning out.");
        house.ShouldContain("name=\"HasCellar\" value=\"true\"");
        house.ShouldContain("name=\"SleepsDownstairs\" value=\"true\"");

        Dictionary<string, string> third = House("High");
        third["Form.DwellingType"] = "Flat";
        third["Form.Storeys"] = "1";
        third["Form.Floor"] = "3";
        string flat = await PlanAsync(third);

        flat.ShouldContain("data-action-id=\"flood-warnings-and-plan\"");
        flat.ShouldContain("The water will not reach your floor, but it can take the power, the lift and the way out for days.");
        flat.ShouldNotContain("data-action-id=\"flood-protection-kit\"", customMessage: "nothing that keeps water out of a ground floor is sold to the third");
        flat.ShouldNotContain("data-action-id=\"flood-water-alarm\"");

        string upstairs = await PlanAsync(House("High"));
        upstairs.ShouldContain("data-action-id=\"flood-protection-kit\"");
        upstairs.ShouldNotContain("data-action-id=\"flood-water-alarm\"", customMessage: "beds upstairs and no cellar");
    }

    [Fact]
    public async Task The_wizard_asks_about_a_stairlift_and_says_what_not_knowing_means()
    {
        using HttpClient client = _factory.CreateClient();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative)));

        html.ShouldContain("<label for=\"Form_Stairlift\">Is there a stairlift?</label>");
        html.ShouldContain("<option value=\"None\">No</option>");
        html.ShouldContain("<option value=\"Battery\">Yes, and it has its own battery</option>");
        html.ShouldContain("<option value=\"MainsOnly\">Yes, and it stops with the power</option>");
        html.ShouldContain("<option value=\"NotKnown\">Yes, and I do not know which</option>");
        html.ShouldContain("Most modern stairlifts run on their own batteries and keep going for a number of trips in a power cut. Until you know which yours is, the plan takes it that it stops.");
    }

    [Fact]
    public async Task A_stairlift_gets_a_row_on_the_board_a_plan_for_a_cut_and_a_service_before_winter()
    {
        Dictionary<string, string> battery = House();
        battery["Form.Children"] = "0";
        battery["Form.Stairlift"] = "Battery";
        string own = await PlanAsync(battery);

        own.ShouldContain("data-action-id=\"stairlift-trips-in-a-power-cut\"");
        own.ShouldContain("Find out how many trips the stairlift makes in a power cut, and keep it parked on its charge point");
        own.ShouldContain("data-action-id=\"downstairs-in-a-power-cut\"");
        own.ShouldContain("A bed downstairs and a commode, for a cut that outlasts the stairlift's batteries");
        own.ShouldContain("data-action-id=\"help-to-leave-plan\"");
        own.ShouldContain("The stairlift is the way down for someone here");
        own.ShouldContain("data-action-id=\"priority-services-register\"");
        own.ShouldContain("Getting upstairs");
        own.ShouldContain("Have the stairlift serviced and its batteries checked");
        own.ShouldContain("&lift=true");
        own.ShouldContain("name=\"Stairlift\" value=\"Battery\"");
        own.ShouldContain("Drive DeVilbiss folding lightweight commode");
        own.ShouldNotContain("data-action-id=\"stairlift-find-out\"");

        Dictionary<string, string> unknown = House();
        unknown["Form.Stairlift"] = "NotKnown";
        string notKnown = await PlanAsync(unknown);
        notKnown.ShouldContain("data-action-id=\"stairlift-find-out\"");
        notKnown.ShouldContain("Until you know, this plan takes it that yours stops.");
        notKnown.ShouldContain("A bed made up downstairs and a commode, decided now and not on the night");
        notKnown.ShouldNotContain("data-action-id=\"stairlift-trips-in-a-power-cut\"");

        string none = await PlanAsync(House());
        none.ShouldNotContain("data-action-id=\"downstairs-in-a-power-cut\"");
        none.ShouldNotContain("Getting upstairs", customMessage: "a house with no stairlift has no such row");
        none.ShouldContain("name=\"Stairlift\" value=\"None\"");

        Dictionary<string, string> bad = House();
        bad["Form.Stairlift"] = "9";
        (await PlanAsync(bad)).ShouldContain("That answer is not one of the options offered. Choose one from the list.");

        using HttpClient client = _factory.CreateClient();
        (await client.GetStringAsync(new Uri("/plan/calendar.ics?lift=true", UriKind.Relative))).ShouldContain("SUMMARY:Have the stairlift serviced and its batteries checked");
        new PlanForm { Stairlift = Stairlift.MainsOnly }.ToRequest().Command.Stairlift.ShouldBe(Stairlift.MainsOnly);
        PlanForm.Stairlifts.Select(s => s.Value).ShouldBe(["None", "Battery", "MainsOnly", "NotKnown"]);
    }

    [Fact]
    public async Task A_choice_that_is_not_on_the_list_is_answered_with_a_sentence()
    {
        Dictionary<string, string> tenure = House();
        tenure["Form.Tenure"] = "9";
        Dictionary<string, string> water = House();
        water["Form.Water"] = "9";

        (await PlanAsync(tenure)).ShouldContain("That answer is not one of the options offered. Choose one from the list.");
        (await PlanAsync(water)).ShouldContain("That answer is not one of the options offered. Choose one from the list.");
    }

    [Fact]
    public async Task The_calendar_carries_the_meter_the_supply_and_the_tank()
    {
        using HttpClient client = _factory.CreateClient();

        string all = await client.GetStringAsync(new Uri("/plan/calendar.ics?meter=true&supply=true&tank=true", UriKind.Relative));
        string none = await client.GetStringAsync(new Uri("/plan/calendar.ics", UriKind.Relative));

        all.ShouldContain("SUMMARY:Build up credit on the prepayment meter before winter");
        all.ShouldContain("SUMMARY:Have your water supply tested");
        all.ShouldContain("SUMMARY:Check when the tank was last emptied");
        none.ShouldNotContain("prepayment meter");
        none.ShouldNotContain("water supply tested");
        none.ShouldNotContain("tank was last emptied");
    }

    [Fact]
    public void The_form_hands_every_answer_to_the_plan_and_reads_a_floor_only_from_a_flat()
    {
        PlanForm form = new()
        {
            Postcode = "NP12 1GW", DwellingType = DwellingType.Flat, Floor = 5, Tenure = Tenure.Tenant, Water = WaterSupply.OwnGravity,
            OwnDrains = true, PrepaymentMeter = true, NeedsHelpToLeave = true, HasCellar = true, SleepsDownstairs = true,
        };

        GeneratePlanCommand command = form.ToRequest().Command;

        (command.Tenure, command.Water, command.OwnDrains, command.PrepaymentMeter).ShouldBe((Tenure.Tenant, WaterSupply.OwnGravity, true, true));
        (command.NeedsHelpToLeave, command.HasCellar, command.SleepsDownstairs, command.Floor).ShouldBe((true, true, true, 5));
        form.FloorOfFlat.ShouldBe(5);

        PlanForm house = new() { DwellingType = DwellingType.Detached, Floor = 5 };
        house.FloorOfFlat.ShouldBe(0, "a floor left over from a flat is not a house's");
        new PlanForm { DwellingType = DwellingType.Flat }.FloorOfFlat.ShouldBe(0);
        GeneratePlanCommand plain = house.ToRequest().Command;
        (plain.Tenure, plain.Water, plain.Floor).ShouldBe((Tenure.Owner, WaterSupply.Mains, 0));
        PlanForm.Tenures.Select(t => t.Value).ShouldBe(["Owner", "Tenant"]);
        PlanForm.WaterSupplies.Select(w => w.Value).ShouldBe(["Mains", "OwnPumped", "OwnGravity"]);
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex Token();
}
