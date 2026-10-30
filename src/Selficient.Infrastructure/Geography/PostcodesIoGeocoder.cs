using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>postcodes.io adapter. One GET per lookup; the postcode is the only variable part of the URL.</summary>
public sealed class PostcodesIoGeocoder : IGeocoder
{
    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options.</summary>
    public PostcodesIoGeocoder(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public async Task<Result<Locality>> LocateAsync(Postcode postcode, CancellationToken cancellationToken)
    {
        Uri path = new("postcodes/" + Uri.EscapeDataString(postcode.Value), UriKind.Relative);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Failure<Locality>(DomainErrors.PostcodeNotFound());
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<Locality>(DomainErrors.LookupUnavailable());
            }

            PostcodeEnvelope? envelope = await response.Content.ReadFromJsonAsync(UpstreamJson.Default.PostcodeEnvelope, cancellationToken);
            return envelope?.Result is { } result && result.Latitude is double lat && result.Longitude is double lon
                ? Result.Success(new Locality(new Coordinates(lat, lon), ParseNation(result.Country), result.Outcode ?? postcode.Outcode, result.Region ?? string.Empty))
                : Result.Failure<Locality>(DomainErrors.LookupUnavailable());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<Locality>(DomainErrors.LookupUnavailable());
        }
    }

    internal static Nation ParseNation(string? country) => country switch
    {
        "England" => Nation.England,
        "Wales" => Nation.Wales,
        "Scotland" => Nation.Scotland,
        "Northern Ireland" => Nation.NorthernIreland,
        _ => Nation.Unknown,
    };
}

/// <summary>The slice of the postcodes.io response that is used. Unknown members are ignored.</summary>
public sealed record PostcodeEnvelope([property: JsonPropertyName("result")] PostcodeResult? Result);

/// <summary>One postcode's facts.</summary>
public sealed record PostcodeResult(
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("outcode")] string? Outcode);

/// <summary>Open-Meteo's elevation response.</summary>
public sealed record ElevationEnvelope([property: JsonPropertyName("elevation")] IReadOnlyList<double>? Elevation);

/// <summary>Source-generated serializer context so deserialisation is trim-safe and reflection-free.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PostcodeEnvelope))]
[JsonSerializable(typeof(ElevationEnvelope))]
public sealed partial class UpstreamJson : JsonSerializerContext;
