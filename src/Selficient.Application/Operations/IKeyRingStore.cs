namespace Selficient.Application.Operations;

/// <summary>
/// Where the web layer's signing keys are kept: the platform database, so a signed cookie survives
/// a recycle and a redeploy. The keys are opaque text to this port; the web layer owns their shape
/// and their protection at rest.
/// </summary>
public interface IKeyRingStore
{
    /// <summary>Every key kept, oldest first.</summary>
    IReadOnlyList<string> ReadAll();

    /// <summary>Keeps a key under its name; a name is written once.</summary>
    void Write(string name, string xml);
}
