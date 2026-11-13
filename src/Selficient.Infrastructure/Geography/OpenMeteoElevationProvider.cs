using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Selficient.Application.Geography;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Geography;

/// <summary>Open-Meteo elevation adapter. Coordinates are formatted invariantly to four decimals.</summary>
public sealed class OpenMeteoElevationProvider : IElevationProvider
{
    private readonly HttpClient _http;

    /// <summary>Creates the adapter over a typed client whose base address is fixed by options.</summary>
    public OpenMeteoElevationProvider(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public async Task<Result<Elevation>> ElevationAtAsync(Coordinates coordinates, CancellationToken cancellationToken)
    {
        string lat = coordinates.Latitude.ToString("0.0000", CultureInfo.InvariantCulture);
        string lon = coordinates.Longitude.ToString("0.0000", CultureInfo.InvariantCulture);
        Uri path = new($"v1/elevation?latitude={lat}&longitude={lon}", UriKind.Relative);

        try
        {
            // Whatever goes wrong upstream is a fault, including the pipeline's own timeout, which is no HttpRequestException;
            // a caller who has already gone is not answered at all.
            cancellationToken.ThrowIfCancellationRequested();
            using HttpResponseMessage response = await _http.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<Elevation>(DomainErrors.LookupUnavailable());
            }

            ElevationEnvelope? envelope = await response.Content.ReadFromJsonAsync(UpstreamJson.Default.ElevationEnvelope, cancellationToken);
            return envelope?.Elevation is { Count: > 0 } values
                ? Elevation.Create(Math.Round((decimal)values[0], 1))
                : Result.Failure<Elevation>(DomainErrors.LookupUnavailable());
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Elevation>(DomainErrors.LookupUnavailable());
        }
    }
}
