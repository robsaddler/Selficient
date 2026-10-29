using Microsoft.Net.Http.Headers;

namespace Selficient.Web.Security;

/// <summary>
/// Browser-side defence in depth. Each header closes a class of attack on its own; none relies on
/// another being present. Verified on every route by the Web tests.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// The policy with the nonce substituted. No inline anything, no third-party origins; the
    /// Google panel in the solar estimator will add its origins on that page alone.
    /// </summary>
    internal static string ContentSecurityPolicy(string nonce) =>
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "manifest-src 'self'; " +
        "worker-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'; " +
        "upgrade-insecure-requests";

    /// <summary>Adds the security headers to every response.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static (context, next) =>
        {
            CspNonce nonce = context.RequestServices.GetRequiredService<CspNonce>();
            IHeaderDictionary headers = context.Response.Headers;

            headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy(nonce.Value);
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(self), geolocation=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["X-Frame-Options"] = "DENY";
            headers.Remove(HeaderNames.XPoweredBy);

            return next(context);
        });
    }
}
