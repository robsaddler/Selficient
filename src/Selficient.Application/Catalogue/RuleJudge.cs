using System.Text.RegularExpressions;

namespace Selficient.Application.Catalogue;

/// <summary>
/// The first judge of a page, without a model: a robot wall, a dead answer, a shop that says it
/// found nothing, or a page that names nothing the pick names. What passes here may still be asked
/// of the model judge; what fails here is not worth a model's time.
/// </summary>
public static partial class RuleJudge
{
    private const string Name = "rule";

    private static readonly string[] Stopwords = ["with", "and", "the", "for", "pack", "of", "litre", "litres", "x", "a", "an", "on", "to", "kit", "home", "cells", "metre", "still"];

    /// <summary>Judges the page for the offer by its text alone.</summary>
    public static Judgement Judge(Offer offer, PageRead page)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(page);

        string head = page.Text.Length > 6000 ? page.Text[..6000] : page.Text;
        if (page.Status == 0)
        {
            return new Judgement(Verdict.Dead, "The page did not answer.", null, Name);
        }

        if (RobotWall().IsMatch(head.Length > 3000 ? head[..3000] : head) || page.Status == 503 || page.Status == 403)
        {
            return new Judgement(Verdict.RobotWall, "The shop showed its robot page to the check.", null, Name);
        }

        if (page.Status >= 400)
        {
            return new Judgement(Verdict.Dead, $"The page answered {page.Status}.", null, Name);
        }

        bool counted = ResultsCounted().IsMatch(page.Title) || ResultsCounted().IsMatch(head.Length > 3000 ? head[..3000] : head);
        if (!counted && (NoResults().IsMatch(head) || NoResults().IsMatch(page.Title)))
        {
            return new Judgement(Verdict.NoResults, "The shop said it found nothing.", null, Name);
        }

        string[] words = Words(offer.Name);
        if (words.Length > 0 && words.All(word => !page.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return new Judgement(Verdict.WrongItem, $"Nothing on the page names the item (looked for {string.Join(", ", words)}).", null, Name);
        }

        return new Judgement(Verdict.Landed, "The page answered and names the item.", null, Name);
    }

    /// <summary>The distinctive words of a pick's name: the first three that are not filler or numbers.</summary>
    public static string[] Words(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Split([' ', ',', '+', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('.', '(', ')'))
            .Where(word => word.Length > 2 && !Stopwords.Contains(word, StringComparer.OrdinalIgnoreCase) && !word.All(char.IsDigit))
            .Take(3)
            .ToArray();
    }

    [GeneratedRegex(@"not a robot|enter the characters you see|human verification|access denied|verify you are human|unusual traffic", RegexOptions.IgnoreCase)]
    private static partial Regex RobotWall();

    [GeneratedRegex(@"\b[1-9]\d* (results?|items?|products?)( found)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResultsCounted();

    [GeneratedRegex(@"no results for|no results found|\b0 results|couldn.t find any|didn.t find any|nothing found|no matches|no products found|0 items|0 products|page not found|we can.t find", RegexOptions.IgnoreCase)]
    private static partial Regex NoResults();
}
