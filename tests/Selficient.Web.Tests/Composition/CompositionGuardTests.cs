using Selficient.Web.Composition;
using Selficient.Web.Endpoints;

namespace Selficient.Web.Tests.Composition;

public sealed class CompositionGuardTests
{
    [Fact]
    public void Every_extension_rejects_a_missing_argument()
    {
        Should.Throw<ArgumentNullException>(() => WebServiceCollectionExtensions.AddSelficientWeb(null!));
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
