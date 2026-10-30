namespace Selficient.Web.Security;

/// <summary>
/// What one request is allowed to connect to beyond this origin. Empty for every page but the one
/// that lets a household use their own Anthropic key from the browser, and even there only the one
/// host. The header is written when the response starts, after the page has had its say.
/// </summary>
public sealed class CspPolicy
{
    private readonly List<string> _connect = [];

    /// <summary>The extra connect sources, in the order added.</summary>
    public IReadOnlyList<string> ConnectSources => _connect;

    /// <summary>Allows the browser to connect to one more https origin for this response only.</summary>
    public void AllowConnect(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A connect source must be an absolute https origin.", nameof(origin));
        }

        string source = origin.GetLeftPart(UriPartial.Authority);
        if (!_connect.Contains(source, StringComparer.Ordinal))
        {
            _connect.Add(source);
        }
    }
}
