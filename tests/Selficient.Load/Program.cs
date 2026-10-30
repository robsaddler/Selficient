using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using Selficient.TestKit;

// The load run from the build plan, section 6: the plan POST with a cached geocode at 200 a
// second, the plan POST at 50 a second, the tile status probe, and the solar estimate. The
// application is hosted here on Kestrel against the fakes with the rate limits lifted, so the
// numbers are the application's own and not the limiter's. Targets are checked at the end and
// the process exits non-zero when one is missed, so the script can fail a build.

using SelficientWebApplicationFactory factory = new SelficientWebApplicationFactory()
    .WithSetting("RateLimits:PlanPerMinute", "10000")
    .WithSetting("RateLimits:ApiPerMinute", "10000")
    .WithSetting("RateLimits:TilesPerMinute", "100000")
    .WithSetting("RateLimits:AiPerMinute", "1000");
string origin = factory.StartOnKestrel();
Console.WriteLine($"Selficient on {origin}");

int seconds = args.Length > 0 && int.TryParse(args[0], CultureInfo.InvariantCulture, out int given) ? given : 20;
TimeSpan duration = TimeSpan.FromSeconds(seconds);

// One client per scenario copy would be realistic; one shared client with cookies is enough to
// measure the server, and the antiforgery pair is read once.
using HttpClient client = new(new SocketsHttpHandler { UseCookies = true, PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { BaseAddress = new Uri(origin) };
string wizard = await client.GetStringAsync("/plan/start");
string token = Regex.Match(wizard, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"").Groups["token"].Value;
if (string.IsNullOrEmpty(token))
{
    Console.Error.WriteLine("No antiforgery token on the wizard.");
    return 2;
}

Dictionary<string, string> robsHouse = new()
{
    ["__RequestVerificationToken"] = token,
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

Dictionary<string, string> robsRoof = new()
{
    ["__RequestVerificationToken"] = token,
    ["Form.Postcode"] = "NP12 1GW",
    ["Form.Aspect"] = "SouthWest",
    ["Form.PitchDegrees"] = "35",
    ["Form.Panels"] = "13",
    ["Form.BatteryKilowattHours"] = "13.5",
    ["Form.Backup"] = "WholeHome",
    ["Form.Storeys"] = "3",
    ["Form.People"] = "4",
    ["Form.HasElectricCar"] = "false",
    ["Form.HasHeatPump"] = "false",
    ["Form.AnnualKilowattHours"] = "5000",
};

ScenarioProps plan = Scenario.Create("plan_post_200_per_second", async context =>
    {
        using FormUrlEncodedContent body = new(robsHouse);
        using HttpResponseMessage response = await client.PostAsync("/plan", body);
        string html = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.OK && html.Contains("Your house could stand on its own for", StringComparison.Ordinal)
            ? Response.Ok(sizeBytes: html.Length)
            : Response.Fail(message: response.StatusCode.ToString());
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: duration));

ScenarioProps solar = Scenario.Create("solar_post_50_per_second", async context =>
    {
        using FormUrlEncodedContent body = new(robsRoof);
        using HttpResponseMessage response = await client.PostAsync("/plan/solar", body);
        string html = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.OK && html.Contains("Solar and a battery could save you about", StringComparison.Ordinal)
            ? Response.Ok(sizeBytes: html.Length)
            : Response.Fail(message: response.StatusCode.ToString());
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: duration));

ScenarioProps tiles = Scenario.Create("tiles_status_100_per_second", async context =>
    {
        using HttpResponseMessage response = await client.GetAsync("/api/tiles/status");
        return response.StatusCode == HttpStatusCode.OK ? Response.Ok() : Response.Fail(message: response.StatusCode.ToString());
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: duration));

ScenarioProps landing = Scenario.Create("landing_get_100_per_second", async context =>
    {
        using HttpResponseMessage response = await client.GetAsync("/");
        return response.StatusCode == HttpStatusCode.OK ? Response.Ok() : Response.Fail(message: response.StatusCode.ToString());
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: duration));

NodeStats stats = NBomberRunner
    .RegisterScenarios(plan, solar, tiles, landing)
    .WithReportFolder(Path.Combine("artifacts", "load"))
    .WithReportFormats(ReportFormat.Md)
    .Run();

// The targets from the build plan. A miss is a non-zero exit so the script can fail.
(string Scenario, double P95Ms)[] targets =
[
    ("plan_post_200_per_second", 150),
    ("solar_post_50_per_second", 200),
    ("tiles_status_100_per_second", 50),
    ("landing_get_100_per_second", 100),
];

int failures = 0;
foreach ((string name, double target) in targets)
{
    ScenarioStats scenario = stats.ScenarioStats.First(s => s.ScenarioName == name);
    double p95 = scenario.Ok.Latency.Percent95;
    int failed = scenario.Fail.Request.Count;
    string verdict = p95 <= target && failed == 0 ? "ok" : "MISSED";
    Console.WriteLine($"{name}: p95 {p95:0.0} ms (target {target} ms), p99 {scenario.Ok.Latency.Percent99:0.0} ms, mean {scenario.Ok.Latency.MeanMs:0.0} ms, {scenario.Ok.Request.Count} ok, {failed} failed: {verdict}");
    if (verdict == "MISSED")
    {
        failures++;
    }
}

return failures == 0 ? 0 : 1;
