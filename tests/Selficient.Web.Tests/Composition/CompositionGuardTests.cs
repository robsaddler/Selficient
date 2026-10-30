using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Web.Composition;
using Selficient.Web.Endpoints;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Composition;

public sealed class CompositionGuardTests
{
    [Fact]
    public void Every_extension_rejects_a_missing_argument()
    {
        Should.Throw<ArgumentNullException>(() => WebServiceCollectionExtensions.AddSelficientWeb(null!, new ConfigurationBuilder().Build()));
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddSelficientWeb(null!));
        Should.Throw<ArgumentNullException>(() => Selficient.Web.Security.RateLimits.AddSelficientRateLimiting(null!, new ConfigurationBuilder().Build()));
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddSelficientRateLimiting(null!));
        Should.Throw<ArgumentNullException>(() => WebApplicationExtensions.UseSelficientPipeline(null!));
        Should.Throw<ArgumentNullException>(() => HealthEndpoint.MapHealthEndpoint(null!));
    }

    [Fact]
    public void The_culture_is_british_english()
    {
        SelficientCulture.EnGb.Name.ShouldBe("en-GB");
        SelficientCulture.EnGb.NumberFormat.CurrencySymbol.ShouldBe("£");
    }
}
