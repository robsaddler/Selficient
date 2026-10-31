using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Resilience;
using Selficient.Domain.Resilience;
using Selficient.TestKit;
using Selficient.Web.Pages.Plan;

namespace Selficient.Web.Tests.Pages;

public sealed class BoardPagesTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new SelficientWebApplicationFactory().WithSetting("RateLimits:PlanPerMinute", "1000").WithSetting("RateLimits:BoardPerMinute", "1000");

    [Fact]
    public async Task The_plan_page_carries_the_board_for_the_storm_with_nothing_ticked()
    {
        using HttpClient client = _factory.CreateClient();

        string html = await Post(client, "/plan", RobsHouse());

        html.ShouldContain("A storm takes the power out: what holds, what goes, and when");
        html.ShouldContain("<days-ready value=\"1\"");
        html.ShouldContain("data-source=\"house\" data-season=\"Winter\"");
        html.ShouldContain("<h3 class=\"board__rung-name\" id=\"rung-Survive\">Survive</h3>", customMessage: "each rung is its own band with a heading");
        html.ShouldContain("Stay alive: water, food, warmth, medicines, and the people and animals who depend on you.");
        html.ShouldContain("Held for the whole stretch: ", customMessage: "each band says how it stands");
        html.ShouldNotContain("data-source=\"rooftop-solar-and-battery\"", customMessage: "one sheet is drawn; a switch posts for another");
        html.ShouldContain("Held for the whole stretch by the tap.");
        html.ShouldContain("Water to drink, a day: held throughout.");
        html.ShouldContain("What you rely on");
        html.ShouldNotContain("A home battery needs a roof of your own", customMessage: "a detached house has one");
        Regex.Count(html, "data-scenario-card").ShouldBe(Scenarios.All.Count);
    }

    [Fact]
    public async Task The_fragment_redraws_for_ticks_and_a_chosen_scenario()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Scenario"] = "snow";

        string bare = await Post(client, "/plan/board", RobsHouse());
        string snow = await Post(client, "/plan/board", form, "Ticks=water-bottled-three-days", "Ticks=food-no-cook-three-days", "Ticks=cash-in-small-notes", "Ticks=headtorches");

        bare.ShouldContain("data-days-ready=\"1\"");
        bare.ShouldNotContain("<html", customMessage: "a fragment, not a page");
        snow.ShouldContain("Snow closes the road: what holds, what goes, and when");
        snow.ShouldContain("data-scenario=\"snow\"");
        snow.ShouldContain("data-days-ready=\"3\"", customMessage: "three days of tins before the road is wanted");
        snow.ShouldContain("data-turns-action=\"food-one-month\"", customMessage: "the month of food would turn cells");
        snow.ShouldContain("<ul hidden data-board-cards>", customMessage: "the chips in the hero take their figures from the fragment");
        snow.ShouldContain("data-card-id=\"snow\" data-days=\"3\" data-selected=\"true\"");
        snow.ShouldContain("<ul hidden data-board-tiers>");
        snow.ShouldContain("data-tier=\"ThisWeek\" data-days=\"");
        snow.ShouldNotContain("data-scenario-card", customMessage: "the cards left the fragment for the hero");
    }

    [Fact]
    public async Task A_dependency_gets_its_own_row_and_decides_the_headline()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Dependencies[0].Name"] = "Oxygen concentrator";
        form["Form.Dependencies[0].Kind"] = "PoweredDevice";
        form["Form.Dependencies[0].Watts"] = "300";
        form["Form.Dependencies[0].HoursPerDay"] = "24";
        form["Form.Dependencies[1].Name"] = "   ";

        string html = await Post(client, "/plan/board", form);
        string page = await Post(client, "/plan", form);

        html.ShouldContain("Oxygen concentrator");
        html.ShouldContain("data-days-ready=\"0.5\"", customMessage: "a concentrator with no source is lost on day one");
        html.ShouldContain("Lost, because the sources cannot carry it.");
        page.ShouldContain("name=\"Form.Dependencies[1].Name\" value=\"Oxygen concentrator\"", customMessage: "the plan page carries what was posted as hidden rows so a post without script keeps it");
        page.ShouldNotContain("name=\"Form.Dependencies[2].Name\"", customMessage: "the blank row is dropped");
    }

    [Theory]
    [InlineData("Form.Dependencies[0].Watts", "9000", "A device draws between 1 and 3,000 watts.")]
    [InlineData("Form.Dependencies[0].Name", "This name is far too long to be a sensible name for anything", "Give the thing you rely on a short name, up to forty characters.")]
    [InlineData("Form.Infants", "9", "The number of infants must be between zero and the number of children.")]
    [InlineData("Form.Floor", "99", "The floor must be between 0 for the ground floor and 40.")]
    public async Task A_bad_answer_in_the_newer_questions_comes_back_as_a_sentence(string field, string value, string sentence)
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> form = RobsHouse();
        form["Form.Dependencies[0].Name"] = "Pump";
        form["Form.Dependencies[0].Kind"] = "PoweredDevice";
        form["Form.Dependencies[0].Watts"] = "50";
        form["Form.Dependencies[0].HoursPerDay"] = "8";
        form[field] = value;

        using HttpResponseMessage fragment = await PostRaw(client, "/plan/board", form);
        string page = await Post(client, "/plan", form);

        fragment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string refusal = await fragment.Content.ReadAsStringAsync();
        refusal.ShouldBe(sentence, customMessage: "the fragment answers with the same sentence the form would show");
        page.ShouldContain(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(sentence));
        page.ShouldNotContain("field is required.");
        page.ShouldNotContain("is invalid.");
    }

    [Fact]
    public async Task Ticks_are_checked_before_they_reach_the_engine()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage hostile = await PostRaw(client, "/plan/board", RobsHouse(), "Ticks=<script>");
        using HttpResponseMessage tooMany = await PostRaw(client, "/plan/board", RobsHouse(), [.. Enumerable.Range(0, IndexModel.MaxTicks + 1).Select(i => $"Ticks=tick-{i}")]);
        using HttpResponseMessage unknown = await PostRaw(client, "/plan/board", RobsHouse(), "Ticks=not-a-real-action");

        hostile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hostile.Content.ReadAsStringAsync()).ShouldBe("One of the ticks is not a plan action.");
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadAsStringAsync()).ShouldBe("That is more ticks than the plan has actions.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK, "a well-formed id the engine does not know changes nothing");
    }

    [Fact]
    public async Task The_fragment_refuses_what_the_plan_page_would_and_redirects_a_get()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> badPostcode = RobsHouse();
        badPostcode["Form.Postcode"] = "ZE1 0ZZ";
        Dictionary<string, string> badPeople = RobsHouse();
        badPeople["Form.People"] = "99";

        using HttpResponseMessage notFound = await PostRaw(client, "/plan/board", badPostcode);
        using HttpResponseMessage invalid = await PostRaw(client, "/plan/board", badPeople);
        using HttpResponseMessage get = await client.GetAsync(new Uri("/plan/board", UriKind.Relative));

        notFound.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notFound.Content.ReadAsStringAsync()).ShouldBe("We couldn't find that postcode. Check it and try again.");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invalid.Headers.CacheControl!.NoStore.ShouldBeTrue("a fragment is never kept by a shared cache");
        (await invalid.Content.ReadAsStringAsync()).ShouldBe("A plan covers between one and twenty people.", customMessage: "the first of our own sentences, so a person sees why");
        get.StatusCode.ShouldBe(HttpStatusCode.OK, "redirected to the plan");
        get.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/plan");
    }

    [Fact]
    public async Task A_flat_is_told_why_the_battery_is_missing_and_its_water_is_pumped()
    {
        using HttpClient client = _factory.CreateClient();
        Dictionary<string, string> flat = Flat();
        flat["Form.Floor"] = "6";

        string html = await Post(client, "/plan", flat);

        html.ShouldContain("A home battery needs a roof of your own, so it is not among the sources for a flat.");
        html.ShouldContain("<days-ready value=\"0.5\"", customMessage: "the sixth floor loses its water with the power");
        html.ShouldContain("because the tap cannot carry it.");
    }

    [Fact]
    public void The_copy_helpers_say_whole_sentences()
    {
        ResilienceProfile rob = ResilienceProfile.FromProfile(Profiles.RobsHouse());
        Board board = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Winter);
        BoardRow food = board.Row("Food")!;
        BoardRow water = board.Row("DrinkingWater")!;
        BoardRow laundry = board.Row("Laundry")!;
        BoardRow warmth = board.Row("Warmth")!;
        BoardRow cooking = board.Row("Cooking")!;
        BoardRow summerWarmth = OutageBoard.Simulate(rob, new HashSet<string>(), Scenarios.Storm, Season.Summer).Row("Warmth")!;

        BoardCopy.Row(water).ShouldBe("Held for the whole stretch by the tap.");
        BoardCopy.Row(food).ShouldBe("Held for 1 day by the shops, then not enough: the shops gives some of it, because deliveries cannot carry it.");
        BoardCopy.Row(warmth).ShouldBe("Held in a reduced way by blankets, duvets and one room, because the gas boiler cannot carry it.");
        BoardCopy.Row(cooking).ShouldBe("Lost, because the electric hob cannot carry it.");
        BoardCopy.Row(laundry).ShouldBe("Held for 7 days by nothing in the house, then lost, because the washing machine cannot carry it.");
        BoardCopy.Row(summerWarmth).ShouldBe("Not something this house needs in this scenario.");
        BoardCopy.Cell(food, food.Cells[0]).ShouldBe("Food, a day: held throughout.");
        BoardCopy.Cell(food, food.Cells[1]).ShouldBe("Food, three days: held for 1 day, then lost.");
        BoardCopy.Cell(cooking, cooking.Cells[0]).ShouldBe("Cooking, a day: lost from the start.");
        BoardCopy.Name(null).ShouldBe("nothing in the house");
        BoardCopy.Name("the sources").ShouldBe("the sources", "a dependency's own prose passes through");
        BoardCopy.Days(1).ShouldBe("1 day");
        BoardCopy.Days(3).ShouldBe("3 days");
        BoardCopy.DaysReady(0.5m).ShouldBe("half a day");
        BoardCopy.DaysReady(1m).ShouldBe("1 day");
        BoardCopy.Unit(1m).ShouldBe("day");
        BoardCopy.Unit(0.5m).ShouldBe("days");
        BoardCopy.TierOutcome(4m).ShouldBe("With everything up to here ticked, the house stands for 4 days.");
        BoardCopy.TierOutcome(null).ShouldBe("");
        BoardCopy.Turns([new Selficient.Application.Resilience.NeedShift("Food", 1m, 7m, true)]).ShouldBe("With this: food from 1 day to 7 days.");
        BoardCopy.Turns([new Selficient.Application.Resilience.NeedShift("Warmth", 30m, 30m, true), new("Light", 0.5m, 30m, true), new("Cash", 1m, 3m, true), new("Food", 1m, 3m, true)]).ShouldBe("With this: warmth eased, though not for longer, light from half a day to 30 days, cash from 1 day to 3 days, and 1 more.");
        Should.Throw<ArgumentNullException>(() => BoardCopy.Turns(null!));
        BoardCopy.RungName(Rung.Function).ShouldBe("Keep going");
        BoardCopy.RungWhat(Rung.Survive).ShouldBe("Stay alive: water, food, warmth, medicines, and the people and animals who depend on you.");
        BoardCopy.RungMark(Rung.House).ShouldBe("source-house");
        Should.Throw<ArgumentNullException>(() => BoardCopy.RungSummary(null!));
        BoardCopy.DaysReady(14m).ShouldBe("14 days");
        BoardCopy.State(CellState.Rationed).ShouldBe("Not enough");
        Should.Throw<ArgumentNullException>(() => BoardCopy.Row(null!));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Cell(null!, food.Cells[0]));
        Should.Throw<ArgumentNullException>(() => BoardCopy.Cell(food, null!));
    }

    [Fact]
    public void The_form_maps_the_newer_answers_with_their_defaults()
    {
        PlanForm form = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm flat = new() { Postcode = "NP12 1GW", People = 1, HasCar = false, Car = CarKind.ElectricWithOutput, DwellingType = Selficient.Domain.Households.DwellingType.Flat, Floor = 5, Infants = 0 };
        form.Dependencies.Add(new DependencyForm { Name = " " });
        form.Dependencies.Add(new DependencyForm { Name = "Insulin", Kind = DependencyKind.Medicine, DaysOfStock = 28, MustStayCold = true });

        ResilienceProfile rob = form.ToResilienceProfile(Profiles.RobsHouse()).Value;
        ResilienceProfile flatProfile = flat.ToResilienceProfile(Profiles.Flat()).Value;

        rob.Car.ShouldBe(CarKind.Petrol, "a car with no kind is petrol");
        rob.Growing.ShouldBe(Growing.None);
        rob.MedicineDaysInStock.ShouldBe(7);
        rob.Floor.ShouldBe(0, "a house has no floor to speak of");
        rob.Dependencies.Count.ShouldBe(1, "the blank row is ignored");
        rob.Dependencies[0].MustStayCold.ShouldBeTrue();
        flatProfile.Car.ShouldBe(CarKind.None, "the plan said no car, whatever the kind field says");
        flatProfile.Floor.ShouldBe(5);
        flatProfile.WaterIsPumped.ShouldBeTrue();

        PlanForm electric = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, Car = CarKind.Electric, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm noKind = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, Car = CarKind.None, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        PlanForm badRow = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        badRow.Dependencies.Add(new DependencyForm { Name = "Pump", Kind = DependencyKind.PoweredDevice, Watts = 0m, HoursPerDay = 8m });
        PlanForm nullName = new() { Postcode = "NP12 1GW", People = 4, Children = 2, HasCar = true, DwellingType = Selficient.Domain.Households.DwellingType.Detached, Storeys = 3 };
        nullName.Dependencies.Add(new DependencyForm { Name = null, Kind = DependencyKind.Supply, DaysOfStock = 3 });

        electric.ToResilienceProfile(Profiles.RobsHouse()).Value.Car.ShouldBe(CarKind.Electric);
        noKind.ToResilienceProfile(Profiles.RobsHouse()).Value.Car.ShouldBe(CarKind.Petrol, "a car of no stated kind is petrol");
        badRow.ToResilienceProfile(Profiles.RobsHouse()).Error.Code.ShouldBe("dependency.power", "a device that draws nothing is refused by the domain");
        nullName.ToResilienceProfile(Profiles.RobsHouse()).Value.Dependencies.ShouldBeEmpty("a row with no name is a blank row");
        new DependencyForm { Name = null }.ToDependency().Error.Code.ShouldBe("dependency.name");
        IndexModel.Echo(form).ShouldContain((nameof(PlanForm.Car), "None"));
        IndexModel.Echo(form).ShouldContain((nameof(PlanForm.MedicineDaysInStock), "7"));
        IndexModel.FormFields.ShouldContain(nameof(PlanForm.Floor));
    }

    internal static Dictionary<string, string> RobsHouse() => new()
    {
        ["Form.Postcode"] = "NP12 1GW",
        ["Form.People"] = "4",
        ["Form.Children"] = "2",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "true",
        ["Form.DwellingType"] = "Detached",
        ["Form.Storeys"] = "3",
        ["Form.Heating"] = "GasBoiler",
        ["Form.Hob"] = "Electric",
        ["Form.HasCar"] = "true",
        ["Form.HasGarden"] = "true",
        ["Form.HasDecking"] = "true",
        ["Form.HasTallTreeNearHouse"] = "true",
        ["Form.FloodRisk"] = "VeryLow",
    };

    private static Dictionary<string, string> Flat() => new()
    {
        ["Form.Postcode"] = "NP12 1GW",
        ["Form.People"] = "1",
        ["Form.Children"] = "0",
        ["Form.HasVulnerablePerson"] = "false",
        ["Form.HasPets"] = "false",
        ["Form.RunsBusinessFromHome"] = "false",
        ["Form.DwellingType"] = "Flat",
        ["Form.Storeys"] = "1",
        ["Form.Heating"] = "Electric",
        ["Form.Hob"] = "Electric",
        ["Form.HasCar"] = "false",
        ["Form.HasGarden"] = "false",
        ["Form.HasDecking"] = "false",
        ["Form.HasTallTreeNearHouse"] = "false",
        ["Form.FloodRisk"] = "Unknown",
    };

    internal static async Task<string> Post(HttpClient client, string path, Dictionary<string, string> form, params string[] extra)
    {
        using HttpResponseMessage response = await PostRaw(client, path, form, extra);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostRaw(HttpClient client, string path, Dictionary<string, string> form, params string[] extra)
    {
        string wizard = await client.GetStringAsync(new Uri("/plan/start", UriKind.Relative));
        string token = Regex.Match(wizard, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"").Groups["token"].Value;
        List<KeyValuePair<string, string>> pairs = [new("__RequestVerificationToken", token), .. form];
        foreach (string pair in extra)
        {
            int at = pair.IndexOf('=', StringComparison.Ordinal);
            pairs.Add(new(pair[..at], pair[(at + 1)..]));
        }

        using FormUrlEncodedContent content = new(pairs);
        return await client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    public void Dispose() => _factory.Dispose();
}
