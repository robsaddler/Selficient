using Selficient.Application.Geography;
using Selficient.Application.Solar;
using Selficient.Domain.Primitives;
using Selficient.Domain.Solar;

namespace Selficient.Infrastructure.Solar;

/// <summary>
/// A deterministic yield: PVGIS's real answer for Rob's south-west roof at 35 degrees, scaled by
/// aspect and pitch the way PVGIS scales it. Development without network, and every test.
/// </summary>
public sealed class FakeSolarYieldProvider : ISolarYieldProvider
{
    /// <summary>PVGIS 5.2 for 51.6655, -3.2031, 35°, azimuth 45, 14% losses: 935.7 kWh/kWp.</summary>
    public static IReadOnlyList<decimal> SouthWestAt35 { get; } = [29.35m, 45.67m, 79.6m, 108.6m, 122.41m, 120.59m, 121.84m, 104.49m, 85.78m, 54.37m, 35.88m, 27.12m];

    /// <summary>A place where the fake reports the service down, for tests.</summary>
    public static Coordinates Unreachable { get; } = new(60.0, -1.0);

    /// <inheritdoc />
    public Task<Result<SolarYield>> YieldAsync(Coordinates coordinates, RoofAspect aspect, RoofPitch pitch, CancellationToken cancellationToken)
    {
        if (coordinates == Unreachable)
        {
            return Task.FromResult(Result.Failure<SolarYield>(DomainErrors.YieldUnavailable()));
        }

        decimal factor = AspectFactor(aspect) * PitchFactor(pitch.Degrees);
        // Winter suffers more than summer on a poor aspect, so the factor leans on the dark months.
        decimal[] months = new decimal[12];
        for (int i = 0; i < 12; i++)
        {
            bool winter = i is <= 1 or >= 10;
            decimal monthFactor = winter ? factor * factor : factor;
            months[i] = Math.Round(SouthWestAt35[i] * monthFactor, 2);
        }

        return Task.FromResult(SolarYield.Create(months));
    }

    private static decimal AspectFactor(RoofAspect aspect) => aspect switch
    {
        RoofAspect.South => 1.056m,
        RoofAspect.SouthWest or RoofAspect.SouthEast => 1.0m,
        RoofAspect.West or RoofAspect.East => 0.86m,
        RoofAspect.NorthWest or RoofAspect.NorthEast => 0.72m,
        _ => 0.62m,
    };

    private static decimal PitchFactor(int degrees) => 1.0m - (Math.Abs(degrees - RoofPitch.Typical) * 0.004m);
}

/// <summary>A roof survey for Rob's house, and a plausible one everywhere else.</summary>
public sealed class FakeRoofInsights : IRoofInsightsProvider
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<Result<RoofInsights>> DescribeAsync(Coordinates coordinates, CancellationToken cancellationToken)
    {
        if (coordinates == FakeSolarYieldProvider.Unreachable)
        {
            return Task.FromResult(Result.Failure<RoofInsights>(DomainErrors.LookupUnavailable()));
        }

        RoofInsights robs = new(
            [new RoofSegment(225, 35, 48.5), new RoofSegment(45, 35, 41.0), new RoofSegment(135, 35, 9.2)],
            MaxPanels: 14,
            PanelWatts: 430);
        return Task.FromResult(Result.Success(robs));
    }
}
