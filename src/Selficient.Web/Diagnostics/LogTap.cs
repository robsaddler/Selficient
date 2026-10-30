using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Selficient.Web.Diagnostics;

/// <summary>
/// A fan-out point for log events so tests can assert what the application logs (for example that
/// a postcode never appears). Empty in production, where it costs one emptiness check per event.
/// </summary>
public static class LogTap
{
    private static readonly ConcurrentDictionary<ILogEventSink, byte> Sinks = new();

    /// <summary>Starts receiving every event the application logs.</summary>
    public static void Attach(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sinks[sink] = 0;
    }

    /// <summary>Stops receiving events.</summary>
    public static void Detach(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sinks.TryRemove(sink, out _);
    }

    /// <summary>The sink Program wires into Serilog.</summary>
    public static ILogEventSink Sink { get; } = new FanOut();

    private sealed class FanOut : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            if (Sinks.IsEmpty)
            {
                return;
            }

            foreach (ILogEventSink sink in Sinks.Keys)
            {
                sink.Emit(logEvent);
            }
        }
    }
}
