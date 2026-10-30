using System.Text.RegularExpressions;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Measures;

/// <summary>
/// A UK postcode, normalised to upper case with a single space before the inward code. The only
/// personal datum the server ever handles, so it is validated here before anything touches it.
/// </summary>
public readonly partial record struct Postcode
{
    private Postcode(string value) => Value = value;

    /// <summary>The normalised postcode, for example "NP12 1GW".</summary>
    public string Value { get; }

    /// <summary>The outward code, for example "NP12". Safe to log and to show.</summary>
    public string Outcode => Value[..Value.IndexOf(' ', StringComparison.Ordinal)];

    /// <summary>The one or two letters that name the postcode area, for example "NP".</summary>
    public string Area => new(Value.TakeWhile(char.IsLetter).ToArray());

    /// <summary>Normalises and validates. Anything that is not a UK postcode is a fault.</summary>
    public static Result<Postcode> Create(string? raw)
    {
        if (raw is null || raw.Length > 16)
        {
            return Result.Failure<Postcode>(DomainErrors.InvalidPostcode());
        }

        string compact = Whitespace().Replace(raw, string.Empty).ToUpperInvariant();
        if (compact.Length is < 5 or > 7)
        {
            return Result.Failure<Postcode>(DomainErrors.InvalidPostcode());
        }

        string normalised = string.Concat(compact.AsSpan(0, compact.Length - 3), " ", compact.AsSpan(compact.Length - 3));
        return UkPostcode().IsMatch(normalised)
            ? Result.Success(new Postcode(normalised))
            : Result.Failure<Postcode>(DomainErrors.InvalidPostcode());
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // The standard BS 7666 shape: area letters, district, a space, sector digit, unit letters.
    [GeneratedRegex(@"^(GIR 0AA|[A-PR-UWYZ][A-HK-Y]?[0-9][0-9A-HJKPS-UW]? [0-9][ABD-HJLNP-UW-Z]{2})$")]
    private static partial Regex UkPostcode();
}
