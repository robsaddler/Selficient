using Microsoft.AspNetCore.Http;
using Selficient.Web.Diagnostics;
using Serilog.Events;

namespace Selficient.Web.Tests.Diagnostics;

/// <summary>
/// How loudly a finished request is logged. A browser that hung up in the middle of an upload is
/// an ordinary day; a request the server failed is not.
/// </summary>
public sealed class RequestLogTests
{
    [Fact]
    public void A_request_that_was_answered_is_information_and_one_the_server_failed_is_an_error()
    {
        DefaultHttpContext fine = new();
        fine.Response.StatusCode = StatusCodes.Status404NotFound;
        DefaultHttpContext failed = new();
        failed.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        RequestLog.Level(fine, 3, null).ShouldBe(LogEventLevel.Information);
        RequestLog.Level(failed, 3, null).ShouldBe(LogEventLevel.Error);
        RequestLog.Level(fine, 3, new InvalidOperationException("The database is away.")).ShouldBe(LogEventLevel.Error);
        Should.Throw<ArgumentNullException>(() => RequestLog.Level(null!, 3, null));
        Should.Throw<ArgumentNullException>(() => RequestLog.Abandoned(null!, new IOException()));
    }

    [Fact]
    public void A_browser_that_hung_up_mid_upload_is_not_an_error()
    {
        using CancellationTokenSource gone = new();
        gone.Cancel();
        DefaultHttpContext abandoned = new() { RequestAborted = gone.Token };
        DefaultHttpContext listening = new();

        RequestLog.Level(abandoned, 3, new IOException("Unexpected end of request content.")).ShouldBe(LogEventLevel.Information);
        RequestLog.Level(abandoned, 3, new OperationCanceledException()).ShouldBe(LogEventLevel.Information);
        RequestLog.Level(abandoned, 3, new InvalidOperationException()).ShouldBe(LogEventLevel.Error, "a real fault is a fault whether or not anyone stayed to hear of it");
        RequestLog.Level(listening, 3, new IOException("The disk is full.")).ShouldBe(LogEventLevel.Error, "the same exception with the browser still there is ours");
        RequestLog.Level(listening, 3, new OperationCanceledException()).ShouldBe(LogEventLevel.Error);
    }
}
