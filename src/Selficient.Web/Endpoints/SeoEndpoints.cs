using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace Selficient.Web.Endpoints;

/// <summary>
/// The sitemap: the public pages, by hand, so a private page can never appear in it by accident.
/// The host is fixed to the production origin because a sitemap is for crawlers, not for the test
/// server.
/// </summary>
public static class SeoEndpoints
{
    /// <summary>The site's public origin.</summary>
    public const string Origin = "https://selficient.app";

    /// <summary>The pages a crawler should know about, in the order they matter.</summary>
    public static IReadOnlyList<(string Path, string ChangeFrequency, decimal Priority)> PublicPages { get; } =
    [
        ("/", "weekly", 1.0m),
        ("/plan/start", "monthly", 0.9m),
        ("/deals", "daily", 0.8m),
        ("/plan/solar", "monthly", 0.7m),
        ("/plan/photos", "monthly", 0.6m),
        ("/privacy", "yearly", 0.3m),
        ("/terms", "yearly", 0.3m),
    ];

    /// <summary>Maps <c>GET /sitemap.xml</c>.</summary>
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/sitemap.xml", Sitemap).WithName("Sitemap").ExcludeFromDescription();

        return endpoints;
    }

    private static ContentHttpResult Sitemap(HttpContext context)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset",
                PublicPages.Select(page => new XElement(ns + "url",
                    new XElement(ns + "loc", Origin + page.Path),
                    new XElement(ns + "changefreq", page.ChangeFrequency),
                    new XElement(ns + "priority", page.Priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))))));

        context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=86400";
        StringBuilder xml = new();
        xml.Append(document.Declaration).Append('\n').Append(document.Root);
        return TypedResults.Text(xml.ToString(), "application/xml; charset=utf-8");
    }
}
