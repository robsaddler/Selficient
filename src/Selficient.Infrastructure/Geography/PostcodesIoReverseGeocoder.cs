using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>
/// The nearest postcode to a point, from postcodes.io. The point arrives rounded to four decimal
/// places (about ten metres), goes out as a query, and is held nowhere; the client has no logger.
/// </summary>
public sealed class PostcodesIoReverseGeocoder : IReverseGeocoder
{
    private readonly HttpClient _http;

    /// <summary>Creates the adapter over the typed client.</summary>
    public PostcodesIoReverseGeocoder(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public async Task<Result<Postcode>> NearestAsync(Coordinates point, CancellationToken cancellationToken)
    {
        string lat = Math.Round(point.Latitude, 4).ToString("0.####", CultureInfo.InvariantCulture);
        string lon = Math.Round(point.Longitude, 4).ToString("0.####", CultureInfo.InvariantCulture);
        Uri path = new($"postcodes?lon={lon}&lat={lat}&limit=1&radius=2000", UriKind.Relative);

        try
        {
            // Whatever goes wrong upstream is a fault, including the pipeline's own timeout, which is no HttpRequestException;
            // a caller who has already gone is not answered at all.
            cancellationToken.ThrowIfCancellationRequested();
            using HttpResponseMessage response = await _http.GetAsync(path, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Result.Failure<Postcode>(DomainErrors.LookupUnavailable());
            }

            NearestEnvelope? envelope = await response.Content.ReadFromJsonAsync(UpstreamJson.Default.NearestEnvelope, cancellationToken);
            string? nearest = envelope?.Result is { Count: > 0 } found ? found[0].Postcode : null;
            if (nearest is null)
            {
                return Result.Failure<Postcode>(DomainErrors.NoPostcodeHere());
            }

            Result<Postcode> postcode = Postcode.Create(nearest);
            return postcode.IsSuccess ? postcode : Result.Failure<Postcode>(DomainErrors.NoPostcodeHere());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Postcode>(DomainErrors.LookupUnavailable());
        }
    }
}

/// <summary>The slice of a postcodes.io reverse lookup that is used: the list of nearest postcodes, or null when there are none.</summary>
public sealed record NearestEnvelope([property: JsonPropertyName("result")] IReadOnlyList<NearestResult>? Result);

/// <summary>One nearby postcode.</summary>
public sealed record NearestResult([property: JsonPropertyName("postcode")] string? Postcode);
