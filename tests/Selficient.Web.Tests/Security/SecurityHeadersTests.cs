using System.Net;
using System.Text.RegularExpressions;
using Selficient.TestKit;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

public sealed partial class SecurityHeadersTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    [Theory]
    [InlineData("/")]
    [InlineData("/styleguide")]
    [InlineData("/health")]
    [InlineData("/api/tiles/status")]
    [InlineData("/api/tiles/1/0/0.pbf")]
    [InlineData("/does-not-exist")]
    [InlineData("/css/app.css")]
    public async Task Every_response_carries_the_full_header_set(string path)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Header(response, "Content-Security-Policy").ShouldContain("frame-ancestors 'none'");
        Header(response, "Content-Security-Policy").ShouldContain("object-src 'none'");
        Header(response, "Content-Security-Policy").ShouldMatch("script-src 'self' 'nonce-[A-Za-z0-9_-]+'");
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Permissions-Policy").ShouldContain("geolocation=()");
        Header(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        Header(response, "Cross-Origin-Resource-Policy").ShouldBe("same-origin");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public async Task The_nonce_in_the_policy_is_the_nonce_on_the_scripts_and_differs_per_request()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(new Uri("/", UriKind.Relative));
        using HttpResponseMessage second = await client.GetAsync(new Uri("/", UriKind.Relative));
        string firstNonce = NonceFrom(Header(first, "Content-Security-Policy"));
        string secondNonce = NonceFrom(Header(second, "Content-Security-Policy"));
        string html = await first.Content.ReadAsStringAsync();

        firstNonce.ShouldNotBe(secondNonce);
        html.ShouldContain($"nonce=\"{firstNonce}\"");
        ScriptTags().Count(html).ShouldBeGreaterThan(0);
        foreach (Match script in ScriptTags().Matches(html))
        {
            script.Value.ShouldContain($"nonce=\"{firstNonce}\"", customMessage: "Every script tag must carry the request nonce.");
        }
    }

    [Fact]
    public async Task HSTS_is_set_outside_development_and_absent_in_development()
    {
        using HttpClient production = _factory.CreateClient();
        using SelficientWebApplicationFactory devFactory = new SelficientWebApplicationFactory().ForEnvironment("Development");
        using HttpClient development = devFactory.CreateClient();

        using HttpResponseMessage prodResponse = await production.GetAsync(new Uri("/", UriKind.Relative));
        using HttpResponseMessage devResponse = await development.GetAsync(new Uri("/", UriKind.Relative));

        prodResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        prodResponse.Headers.Contains("Strict-Transport-Security").ShouldBeTrue();
        devResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        devResponse.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public void The_policy_text_is_pinned()
    {
        SecurityHeaders.ContentSecurityPolicy("abc").ShouldBe(
            "default-src 'self'; script-src 'self' 'nonce-abc'; style-src 'self'; img-src 'self' data: blob:; " +
            "font-src 'self'; connect-src 'self'; manifest-src 'self'; worker-src 'self'; frame-ancestors 'none'; " +
            "base-uri 'self'; form-action 'self'; object-src 'none'; upgrade-insecure-requests");
    }

    [Fact]
    public void Rejects_a_missing_application_builder()
    {
        Should.Throw<ArgumentNullException>(() => SecurityHeaders.UseSecurityHeaders(null!));
    }

    public void Dispose() => _factory.Dispose();

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;

    private static string NonceFrom(string policy) => NoncePattern().Match(policy).Groups["nonce"].Value;

    [GeneratedRegex("'nonce-(?<nonce>[A-Za-z0-9_-]+)'")]
    private static partial Regex NoncePattern();

    [GeneratedRegex("<script[^>]*>")]
    private static partial Regex ScriptTags();
}
