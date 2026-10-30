using System.Globalization;
using Selficient.Application.Solar;
using Selficient.Domain.Measures;
using Selficient.Domain.Solar;
using Selficient.Web.Formatting;

namespace Selficient.Web.Pages.Plan;

/// <summary>Compass wording.</summary>
public static class CompassCopy
{
    /// <summary>The one or two letters on the rose.</summary>
    public static string Letters(RoofAspect aspect) => aspect switch
    {
        RoofAspect.North => "N",
        RoofAspect.NorthEast => "NE",
        RoofAspect.East => "E",
        RoofAspect.SouthEast => "SE",
        RoofAspect.South => "S",
        RoofAspect.SouthWest => "SW",
        RoofAspect.West => "W",
        RoofAspect.NorthWest => "NW",
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Not a roof aspect."),
    };
}

/// <summary>The estimate shaped for the page: sentences, formatted money, and the chart's bars.</summary>
public sealed class SolarEstimateView
{
    private const int ChartHeight = 160;

    /// <summary>Builds the view.</summary>
    public SolarEstimateView(SolarReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
        SolarEstimate estimate = report.Estimate;
        decimal tallest = Math.Max(estimate.MonthlyGeneration.Max(), estimate.MonthlyDemand.Max());
        Bars = Enumerable.Range(0, 12).Select(month => new ChartBar(
            Months.Abbreviated[month],
            estimate.MonthlyGeneration[month],
            estimate.MonthlyDemand[month],
            Scale(estimate.MonthlyGeneration[month], tallest),
            Scale(estimate.MonthlyDemand[month], tallest))).ToList();
    }

    /// <summary>The report.</summary>
    public SolarReport Report { get; }

    /// <summary>The estimate.</summary>
    public SolarEstimate Estimate => Report.Estimate;

    /// <summary>The twelve bars.</summary>
    public IReadOnlyList<ChartBar> Bars { get; }

    /// <summary>"£1,440" with no pence; a credit is shown as a saving all the same.</summary>
    public string AnnualSaving => MoneyFormat.Amount(RoundToPound(Estimate.Best.AnnualSaving));

    /// <summary>The eyebrow: "13 panels facing south-west, 13.5 kWh battery, whole-home backup".</summary>
    public string Eyebrow
    {
        get
        {
            SolarScenario s = Estimate.Scenario;
            string panels = $"{s.Array.Panels} panels facing {Report.Aspect.Words()}";
            string battery = s.Battery.IsPresent ? $", {s.Battery.KilowattHours.ToString("0.#", CultureInfo.InvariantCulture)} kWh battery" : ", no battery";
            string backup = s.Battery.Backup switch
            {
                BackupLevel.WholeHome => ", whole-home backup",
                BackupLevel.ProtectedCircuits => ", protected circuits",
                _ => string.Empty,
            };
            return panels + battery + backup;
        }
    }

    /// <summary>The lede: payback and cost in one sentence, with the backup promise where it is earned.</summary>
    public string Lede
    {
        get
        {
            string cost = MoneyFormat.Range(Estimate.Cost.Total);
            string payback = Estimate.Payback.To is { } to
                ? $"paid back in {Estimate.Payback.From.ToString("0", CultureInfo.InvariantCulture)} to {to.ToString("0", CultureInfo.InvariantCulture)} years"
                : "with no payback at these prices";
            string backup = Estimate.Scenario.Battery.Backup switch
            {
                BackupLevel.WholeHome => " In a power cut the whole house keeps running for a day or more while the panels recharge the battery.",
                BackupLevel.ProtectedCircuits => " In a power cut the lights, fridge, router and boiler keep running.",
                _ when Estimate.Scenario.Battery.IsPresent => " In a power cut this system shuts down with the grid, so it adds nothing to your Days Ready.",
                _ => " Panels alone shut down with the grid, so this adds nothing to your Days Ready.",
            };
            return $"That is against {cost} installed at today's zero VAT, {payback} on the best tariff you qualify for.{backup}";
        }
    }

    /// <summary>Kilowatt-hours as "5,147".</summary>
    public static string Kwh(decimal kwh) => Math.Round(kwh).ToString("N0", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>Whole pounds.</summary>
    public static Money RoundToPound(Money money) => Money.FromPence((long)Math.Round(money.Pence / 100m, MidpointRounding.AwayFromZero) * 100);

    /// <summary>A bill, or "a credit of £x" when export outweighs import.</summary>
    public static string Bill(Money money) => money.Pence < 0
        ? $"a credit of {MoneyFormat.Amount(RoundToPound(Money.FromPence(-money.Pence)))}"
        : MoneyFormat.Amount(RoundToPound(money));

    // Demand is at least 500 kWh a year, so the tallest bar is never zero.
    private static int Scale(decimal value, decimal tallest) => (int)Math.Round(value / tallest * ChartHeight);
}

/// <summary>One month on the chart.</summary>
/// <param name="Label">Three letters.</param>
/// <param name="Generated">Kilowatt-hours made.</param>
/// <param name="Demanded">Kilowatt-hours used.</param>
/// <param name="GeneratedHeight">Bar height in chart units.</param>
/// <param name="DemandedHeight">Bar height in chart units.</param>
public sealed record ChartBar(string Label, decimal Generated, decimal Demanded, int GeneratedHeight, int DemandedHeight);
