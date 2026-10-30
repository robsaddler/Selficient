using Selficient.Application.Resilience;
using Selficient.Domain.Resilience;

namespace Selficient.Web.Pages.Plan;

/// <summary>
/// The illustration system's small marks, one per need and one per source, drawn once in
/// <c>img/marks.svg</c> and referenced by symbol id. A board row or a source pill asks here for its id,
/// so a key the sprite does not know gets the generic mark rather than an empty box.
/// </summary>
public static class Marks
{
    /// <summary>The sprite's path, relative to the site root.</summary>
    public const string Sprite = "/img/marks.svg";

    /// <summary>The mark for a thing the household added: a plug with a cross, because most are devices.</summary>
    public const string Dependency = "need-dependency";

    /// <summary>The mark for the house itself as a source.</summary>
    public const string House = "source-house";

    /// <summary>The symbol id for a board row key: a need's id, or the generic mark for a dependency row.</summary>
    public static string ForRow(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Enum.TryParse(key, ignoreCase: false, out NeedId need) && Enum.IsDefined(need)
            ? ForNeed(need)
            : Dependency;
    }

    /// <summary>The symbol id for a need.</summary>
    public static string ForNeed(NeedId need) => $"need-{need.ToString().ToLowerInvariant()}";

    /// <summary>The symbol id for a source on the board.</summary>
    public static string ForSource(string sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        return sourceId == BoardSet.HouseSource ? House : $"source-{sourceId}";
    }

    /// <summary>Every source id the sprite must carry, so a test can hold the drawing to the catalogue.</summary>
    public static IReadOnlyList<string> SourceIds { get; } =
        ["power-banks", "portable-power-station", "large-power-station", "rooftop-solar-and-battery", "ev-output", "petrol-generator"];
}
