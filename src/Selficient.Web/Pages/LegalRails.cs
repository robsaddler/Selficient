using System.Globalization;

namespace Selficient.Web.Pages;

/// <summary>
/// What the two rails beside a legal page's prose carry: the contents, and the page said plainly.
/// Built here rather than in the view so both pages read from the same shape and the sentences are
/// pinned by tests.
/// </summary>
/// <param name="Sections">Each section's anchor and its title, in the order they appear.</param>
/// <param name="AsideHeading">The rail's heading.</param>
/// <param name="AtAGlance">The page in a handful of sentences.</param>
/// <param name="AsideNote">The line under the rail, saying what the rail is.</param>
/// <param name="Reviewed">When the page was last reviewed, as it is written.</param>
public sealed record LegalRails(
    IReadOnlyList<(string Id, string Title)> Sections,
    string AsideHeading,
    IReadOnlyList<(string Heading, string Sentence)> AtAGlance,
    string AsideNote,
    string Reviewed)
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>The rails for the privacy page.</summary>
    public static LegalRails Privacy { get; } = new(
        [
            ("stays", "What stays in your browser"),
            ("leaves", "What leaves your browser"),
            ("account", "If you make an account"),
            ("cookies", "Cookies"),
            ("server", "What our server sees"),
            ("rights", "Your rights"),
        ],
        "The short version",
        LegalCopy.PrivacyAtAGlance,
        "The page beside this is the whole of it. This column is the same thing said plainly, and it has no legal force of its own.",
        LegalCopy.Reviewed.ToString("d MMMM yyyy", Gb));

    /// <summary>The rails for the terms page.</summary>
    public static LegalRails Terms { get; } = new(
        [
            ("advice", "Guidance, not advice"),
            ("affiliate", "Affiliate disclosure"),
            ("ai", "Photo readings and the narrative"),
            ("warranty", "No warranty"),
            ("licences", "Licences and attribution"),
            ("contact", "Contact"),
        ],
        "The short version",
        LegalCopy.TermsAtAGlance,
        "The page beside this is the whole of it. This column is the same thing said plainly, and it has no legal force of its own.",
        LegalCopy.Reviewed.ToString("d MMMM yyyy", Gb));
}
