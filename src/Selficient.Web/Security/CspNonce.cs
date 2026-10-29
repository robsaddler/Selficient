using System.Buffers.Text;
using System.Security.Cryptography;

namespace Selficient.Web.Security;

/// <summary>
/// One cryptographically random nonce per request. The Content Security Policy allows only
/// scripts carrying it, so an injected script tag can never run.
/// </summary>
/// <remarks>
/// URL-safe base64 is used deliberately: Razor's attribute encoder rewrites <c>+</c>, <c>/</c> and
/// <c>=</c>, which would make the attribute on the page differ from the header. CSP level 3 accepts
/// the <c>-</c> and <c>_</c> alphabet.
/// </remarks>
public sealed class CspNonce
{
    /// <summary>The nonce for this request: 128 random bits, URL-safe base64, no padding.</summary>
    public string Value { get; } = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
}
