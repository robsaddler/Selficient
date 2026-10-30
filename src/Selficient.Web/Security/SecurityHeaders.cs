using Microsoft.Net.Http.Headers;

namespace Selficient.Web.Security;

/// <summary>
/// Browser-side defence in depth. Each header closes a class of attack on its own; none relies on
/// another being present. Verified on every route by the Web tests.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// The policy with the nonce substituted. No inline anything, no third-party origins, except the
    /// one connect source the photos page adds when a household uses their own key.
    /// </summary>
    internal static string ContentSecurityPolicy(string nonce, IReadOnlyList<string> extraConnect)
    {
        ArgumentNullException.ThrowIfNull(extraConnect);
        string connect = extraConnect.Count == 0 ? "'self'" : "'self' " + string.Join(' ', extraConnect);
        return "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            "style-src 'self'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self'; " +
            $"connect-src {connect}; " +
            "manifest-src 'self'; " +
            "worker-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "object-src 'none'; " +
            "upgrade-insecure-requests";
    }

    /// <summary>Adds the security headers to every response.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(self), geolocation=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["X-Frame-Options"] = "DENY";
            headers.Remove(HeaderNames.XPoweredBy);

            // The CSP is written as the response starts, so a page can add its one connect source first.
            context.Response.OnStarting(static state =>
            {
                HttpContext http = (HttpContext)state;
                CspNonce nonce = http.RequestServices.GetRequiredService<CspNonce>();
                CspPolicy policy = http.RequestServices.GetRequiredService<CspPolicy>();
                http.Response.Headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy(nonce.Value, policy.ConnectSources);
                return Task.CompletedTask;
            }, context);

            return next(context);
        });
    }
}
