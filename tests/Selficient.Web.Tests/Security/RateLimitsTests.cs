using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

public sealed class RateLimitsTests
{
    private static DefaultHttpContext Context(IPAddress? address)
    {
        ServiceCollection services = new();
        services.AddSingleton(Options.Create(new RateLimitOptions { PlanPerMinute = 3, ApiPerMinute = 5, BoardPerMinute = 4 }));
        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };
        context.Connection.RemoteIpAddress = address;
        return context;
    }

    [Fact]
    public void Partitions_are_per_client_address_with_a_shared_bucket_when_unknown()
    {
        RateLimits.ClientKey(Context(IPAddress.Parse("203.0.113.9"))).ShouldBe("203.0.113.9");
        RateLimits.ClientKey(Context(null)).ShouldBe("unknown");
    }

    [Fact]
    public async Task Each_policy_permits_its_configured_number_in_a_window()
    {
        HttpContext context = Context(IPAddress.Parse("203.0.113.9"));

        RateLimitPartition<string> plan = RateLimits.PlanPartition(context);
        RateLimitPartition<string> planAgain = RateLimits.PlanPartition(context);
        RateLimitPartition<string> api = RateLimits.ApiPartition(context);
        RateLimitPartition<string> apiAgain = RateLimits.ApiPartition(context);

        plan.PartitionKey.ShouldBe("203.0.113.9");
        planAgain.PartitionKey.ShouldBe(plan.PartitionKey);
        api.PartitionKey.ShouldBe(apiAgain.PartitionKey);

        using RateLimiter planLimiter = plan.Factory(plan.PartitionKey);
        for (int i = 0; i < 3; i++)
        {
            (await planLimiter.AcquireAsync()).IsAcquired.ShouldBeTrue();
        }

        (await planLimiter.AcquireAsync()).IsAcquired.ShouldBeFalse();

        using RateLimiter apiLimiter = api.Factory(api.PartitionKey);
        for (int i = 0; i < 5; i++)
        {
            (await apiLimiter.AcquireAsync()).IsAcquired.ShouldBeTrue();
        }

        (await apiLimiter.AcquireAsync()).IsAcquired.ShouldBeFalse();

        RateLimitPartition<string> board = RateLimits.BoardPartition(context);
        board.PartitionKey.ShouldBe(plan.PartitionKey, "the same address, its own bucket");
        using RateLimiter boardLimiter = board.Factory(board.PartitionKey);
        for (int i = 0; i < 4; i++)
        {
            (await boardLimiter.AcquireAsync()).IsAcquired.ShouldBeTrue();
        }

        (await boardLimiter.AcquireAsync()).IsAcquired.ShouldBeFalse("the board has its own window, so ticking never spends the plan's");
    }
}
