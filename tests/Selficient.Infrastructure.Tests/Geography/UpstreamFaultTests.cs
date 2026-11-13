using Polly.Timeout;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Solar;
using Selficient.Infrastructure.Geography;
using Selficient.Infrastructure.Solar;

namespace Selficient.Infrastructure.Tests.Geography;

/// <summary>
/// A slow upstream is a fault and never a server error. The resilience pipeline ends a request that
/// has run too long with its own exception, which is not an HttpRequestException; five adapters
/// caught only the kinds they knew, and on a loaded machine a plan request came back as a 500.
/// </summary>
public sealed class UpstreamFaultTests
{
    private static readonly Coordinates Blackwood = new(51.66549, -3.20314);

    private static HttpClient Rejecting() => new(new RejectingHandler()) { BaseAddress = new Uri("https://upstream.test/") };

    [Fact]
    public async Task The_pipelines_own_timeout_is_a_fault_in_every_adapter()
    {
        (await new PostcodesIoGeocoder(Rejecting()).LocateAsync(Postcode.Create("NP12 1GW").Value, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await new OpenMeteoElevationProvider(Rejecting()).ElevationAtAsync(Blackwood, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await new PostcodesIoReverseGeocoder(Rejecting()).NearestAsync(Blackwood, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new GoogleSolarRoofInsights(Rejecting()).DescribeAsync(Blackwood, CancellationToken.None)).Error.Code.ShouldBe("lookup.unavailable");
        (await new PvgisYieldProvider(Rejecting()).YieldAsync(Blackwood, RoofAspect.South, RoofPitch.Create(35).Value, CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_caller_who_has_gone_is_not_answered_by_any_adapter()
    {
        using CancellationTokenSource gone = new();
        await gone.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new PostcodesIoGeocoder(Rejecting()).LocateAsync(Postcode.Create("NP12 1GW").Value, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new OpenMeteoElevationProvider(Rejecting()).ElevationAtAsync(Blackwood, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new PostcodesIoReverseGeocoder(Rejecting()).NearestAsync(Blackwood, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new GoogleSolarRoofInsights(Rejecting()).DescribeAsync(Blackwood, gone.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => new PvgisYieldProvider(Rejecting()).YieldAsync(Blackwood, RoofAspect.South, RoofPitch.Create(35).Value, gone.Token));
    }

    /// <summary>A transport that ends every request the way the resilience pipeline ends one that has run too long.</summary>
    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TimeoutRejectedException("The request ran past the total the pipeline allows.");
    }
}
