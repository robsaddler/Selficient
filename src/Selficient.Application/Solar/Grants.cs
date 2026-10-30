using Selficient.Application.Geography;

namespace Selficient.Application.Solar;

/// <summary>Help with the money, in the interface's voice, with a date so it is reviewed.</summary>
/// <param name="Title">What it is called.</param>
/// <param name="Sentence">What it does for a household, in one sentence.</param>
/// <param name="Link">Where to apply or read more.</param>
/// <param name="Seen">When the detail was last checked.</param>
public sealed record Grant(string Title, string Sentence, Uri Link, DateOnly Seen);

/// <summary>
/// What each nation offers towards solar and batteries. Reviewed twice a year; a test fails when
/// any entry is older than 180 days.
/// </summary>
public static class GrantBook
{
    private static readonly DateOnly Reviewed = new(2026, 10, 29);

    private static readonly Grant ZeroVat = new(
        "Zero-rate VAT until 31 March 2027",
        "Panels, batteries and the fitting are VAT-free across the United Kingdom until 31 March 2027, when the rate returns to 5 percent, so an install invoiced before then is about 5 percent cheaper.",
        new Uri("https://www.gov.uk/guidance/vat-on-energy-saving-materials-and-heating-equipment-notice-7086"),
        Reviewed);

    private static readonly Grant ExportGuarantee = new(
        "Smart Export Guarantee",
        "Any licensed supplier must pay you for what you export once the system has its MCS certificate; rates run from 12p to 24p a unit and you can take the export tariff from a different supplier to your import.",
        new Uri("https://www.ofgem.gov.uk/environmental-and-social-schemes/smart-export-guarantee-seg"),
        Reviewed);

    private static readonly Grant GreenHomesWales = new(
        "Green Homes Wales 0 percent loan",
        "The Development Bank of Wales lends £1,000 to £25,000 at 0 percent over up to ten years to owner-occupiers for solar and batteries, with no means test; the fund is limited, so register early.",
        new Uri("https://developmentbank.wales/green-homes-wales"),
        Reviewed);

    private static readonly Grant HomeEnergyScotland = new(
        "Home Energy Scotland grant and loan",
        "Scottish owner-occupiers can get an interest-free loan towards solar panels and a battery through Home Energy Scotland, with a grant element for some measures.",
        new Uri("https://www.homeenergyscotland.org/find-funding/grants-loans/"),
        Reviewed);

    private static readonly Grant WarmHomes = new(
        "Warm Homes: Local Grant",
        "In England, households on lower incomes or in less efficient homes can get solar fitted free or at low cost through their council's Warm Homes scheme; check what your council is running.",
        new Uri("https://www.gov.uk/apply-warm-homes-local-grant"),
        Reviewed);

    private static readonly Grant NorthernIreland = new(
        "Northern Ireland schemes",
        "There is no national solar grant in Northern Ireland at present; the zero VAT rate and the export tariffs from NI suppliers still apply, and NI Energy Advice can tell you what is current.",
        new Uri("https://www.nidirect.gov.uk/articles/energy-advice"),
        Reviewed);

    /// <summary>Every grant the book knows, for the review test.</summary>
    public static IReadOnlyList<Grant> All { get; } = [ZeroVat, ExportGuarantee, GreenHomesWales, HomeEnergyScotland, WarmHomes, NorthernIreland];

    /// <summary>The grants for a nation, the universal ones first.</summary>
    public static IReadOnlyList<Grant> For(Nation nation) => nation switch
    {
        Nation.Wales => [ZeroVat, ExportGuarantee, GreenHomesWales],
        Nation.Scotland => [ZeroVat, ExportGuarantee, HomeEnergyScotland],
        Nation.England => [ZeroVat, ExportGuarantee, WarmHomes],
        Nation.NorthernIreland => [ZeroVat, ExportGuarantee, NorthernIreland],
        _ => [ZeroVat, ExportGuarantee],
    };
}
