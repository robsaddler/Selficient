using Selficient.Application.Geography;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;

namespace Selficient.Application.Solar;

/// <summary>Finds what a kilowatt-peak makes on a roof at a place, month by month.</summary>
public interface ISolarYieldProvider
{
    /// <summary>The yield, or why it could not be found.</summary>
    Task<Result<SolarYield>> YieldAsync(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch, CancellationToken cancellationToken);
}

/// <summary>One slope of a roof as an aerial survey sees it.</summary>
/// <param name="Bearing">Degrees clockwise from north the slope faces.</param>
/// <param name="PitchDegrees">Degrees from horizontal.</param>
/// <param name="AreaSquareMetres">Its area.</param>
public sealed record RoofSegment(double Bearing, double PitchDegrees, double AreaSquareMetres)
{
    /// <summary>The nearest of the eight aspects.</summary>
    public RoofAspect Aspect => RoofAspects.FromBearing(Bearing);
}

/// <summary>What an aerial survey says about a roof.</summary>
/// <param name="Segments">Each slope, largest first.</param>
/// <param name="MaxPanels">How many panels the whole roof could take.</param>
/// <param name="PanelWatts">The panel size the survey assumed.</param>
public sealed record RoofInsights(IReadOnlyList<RoofSegment> Segments, int MaxPanels, int PanelWatts)
{
    /// <summary>The largest slope that faces anywhere south of east-west, or the largest slope at all.</summary>
    public RoofSegment? Best => Segments.FirstOrDefault(static s => s.Bearing is >= 90 and <= 270) ?? (Segments.Count > 0 ? Segments[0] : null);
}

/// <summary>Describes a roof from the air, where a provider is configured and the household has asked.</summary>
public interface IRoofInsightsProvider
{
    /// <summary>False for the null object, so the page never offers what it cannot do.</summary>
    bool IsAvailable { get; }

    /// <summary>The roof nearest the point, or why not.</summary>
    Task<Result<RoofInsights>> DescribeAsync(Coordinates coordinates, CancellationToken cancellationToken);
}

/// <summary>No aerial provider configured. The compass picker is the whole answer.</summary>
public sealed class NoRoofInsights : IRoofInsightsProvider
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<Result<RoofInsights>> DescribeAsync(Coordinates coordinates, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable()));
}
