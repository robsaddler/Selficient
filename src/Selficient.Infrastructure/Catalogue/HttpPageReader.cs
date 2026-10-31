using System.Net;
using System.Text.RegularExpressions;
using Selficient.Application.Catalogue;

namespace Selficient.Infrastructure.Catalogue;

/// <summary>
/// Reads a shop page over plain HTTP, as a browser without script would, and flattens it to text.
/// Shops that render their results by script or wall off anything that is not a person show up as
/// a robot wall or a page with nothing on it, which the judges report rather than misread.
/// </summary>
public sealed partial class HttpPageReader : IPageReader
{
    private readonly HttpClient _http;
    private readonly int _pageBytes;

    /// <summary>Creates the reader over its client.</summary>
    public HttpPageReader(HttpClient http, int pageBytes)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageBytes, 1024);
        _http = http;
        _pageBytes = pageBytes;
    }

    /// <inheritdoc />
    public async Task<PageRead> ReadAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-GB,en;q=0.9");
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            string html = await ReadBoundedAsync(response, cancellationToken);
            return new PageRead((int)response.StatusCode, Title(html), Flatten(html), FinalAddress(response, url));
        }
        catch (HttpRequestException)
        {
            // Refused, unresolvable, reset: the shop is not answering, which the judges report as dead.
            return new PageRead(0, string.Empty, string.Empty, url);
        }
        catch (TaskCanceledException)
        {
            // The client's timeout; a shop that takes longer than the limit is not one a reader would wait for either.
            return new PageRead(0, string.Empty, string.Empty, url);
        }
    }

    /// <summary>Where the page was read from after any redirects, or the address asked for when the response does not say.</summary>
    internal static Uri FinalAddress(HttpResponseMessage response, Uri asked) => response.RequestMessage?.RequestUri ?? asked;

    /// <summary>The page's title, decoded.</summary>
    internal static string Title(string html)
    {
        Match match = TitleTag().Match(html);
        return match.Success ? Whitespace().Replace(WebUtility.HtmlDecode(match.Groups["t"].Value), " ").Trim() : string.Empty;
    }

    /// <summary>The page as text: scripts and styles gone, tags gone, entities decoded, whitespace folded.</summary>
    internal static string Flatten(string html)
    {
        string without = ScriptsAndStyles().Replace(html, " ");
        string text = Tags().Replace(without, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < _pageBytes && (read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, _pageBytes - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    [GeneratedRegex(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<(script|style|noscript|svg)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptsAndStyles();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
