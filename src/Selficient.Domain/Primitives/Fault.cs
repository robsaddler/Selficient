namespace Selficient.Domain.Primitives;

/// <summary>
/// An expected failure, described for the person reading it. The code is stable for tests and
/// logs; the message is a whole sentence in the interface's voice.
/// </summary>
public readonly record struct Fault(string Code, string Message)
{
    /// <summary>The absence of an error, carried by successful results.</summary>
    public static Fault None => new(string.Empty, string.Empty);
}
