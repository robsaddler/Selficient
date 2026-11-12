using Serilog.Events;

namespace Selficient.Web.Diagnostics;

/// <summary>
/// How loudly a finished request is logged. A request that threw, or that the server failed, is
/// an error. A request the browser gave up on is not: a page that navigates away in the middle of
/// sending its plan to the account hangs up on the upload, the read of its body throws, and that
/// is an ordinary day, not something for the error log.
/// </summary>
public static class RequestLog
{
    /// <summary>The level for one request's line.</summary>
    public static LogEventLevel Level(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (exception is not null)
        {
            return Abandoned(context, exception) ? LogEventLevel.Information : LogEventLevel.Error;
        }

        return context.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error : LogEventLevel.Information;
    }

    /// <summary>
    /// Whether an exception is only the sound of the browser hanging up: the connection has been
    /// abandoned, and what was thrown is a cancellation or a body that ended early.
    /// </summary>
    public static bool Abandoned(HttpContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException or IOException;
    }
}
