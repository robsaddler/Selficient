using System.Text.Json;
using System.Text.RegularExpressions;

namespace Selficient.Application.Accounts;

/// <summary>
/// Whether a body an account asks us to keep is a plan and nothing else. It mirrors the browser's
/// own guard (<c>isSnapshot</c> in <c>plan-store.js</c>) so that the two agree on what a plan is:
/// a flat map of short strings, bounded lists of kebab-case ids, figures in range, a pin in the
/// United Kingdom. The server never computes from a stored plan; this is here so that an account
/// is not sixty-four kilobytes of anything at all, and so that what one device stores is something
/// another device's guard will accept.
/// </summary>
public static partial class PlanGuard
{
    /// <summary>The largest body kept, in bytes of UTF-8. A whole plan is a few thousand.</summary>
    public static int MaxBytes => 64 * 1024;

    private static readonly string[] SnapshotFields = ["profile", "ticks", "planned", "skipped", "amounts", "assumptions", "house", "dependencies", "shortlist"];
    private static readonly string[] DependencyFields = ["name", "kind", "watts", "hoursPerDay", "daysOfStock", "mustStayCold"];
    private static readonly string[] Kinds = ["PoweredDevice", "Medicine", "Supply"];

    /// <summary>
    /// Whether text is a sync body: <c>{ "plan": snapshot, "detailsAt": milliseconds }</c> and no
    /// other member, within the cap.
    /// </summary>
    public static bool IsSyncBody(string? json)
    {
        if (string.IsNullOrEmpty(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return false;
        }

        bool valid = false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            valid = HasOnly(root, ["plan", "detailsAt"])
                && root.TryGetProperty("detailsAt", out JsonElement at) && IsNumber(at, 0, 9_999_999_999_999)
                && root.TryGetProperty("plan", out JsonElement plan) && IsSnapshot(plan);
        }
        catch (JsonException)
        {
            // Not JSON, or nested deeper than any plan is.
        }

        return valid;
    }

    /// <summary>Whether an element is a plan's snapshot.</summary>
    public static bool IsSnapshot(JsonElement plan) =>
        HasOnly(plan, SnapshotFields)
        && plan.TryGetProperty("profile", out JsonElement profile) && (profile.ValueKind == JsonValueKind.Null || IsProfile(profile))
        && plan.TryGetProperty("ticks", out JsonElement ticks) && IsIds(ticks)
        && Optional(plan, "planned", IsIds)
        && Optional(plan, "skipped", IsIds)
        && Optional(plan, "amounts", IsAmounts)
        && Optional(plan, "assumptions", IsAssumptions)
        && Optional(plan, "house", static house => house.ValueKind == JsonValueKind.Null || IsHouse(house))
        && Optional(plan, "dependencies", IsDependencies)
        && Optional(plan, "shortlist", IsShortlist);

    /// <summary>An object whose members are all among the names given. Anything else on it is not a plan's.</summary>
    private static bool HasOnly(JsonElement element, string[] names) =>
        element.ValueKind == JsonValueKind.Object && element.EnumerateObject().All(member => names.Contains(member.Name, StringComparer.Ordinal));

    private static bool Optional(JsonElement parent, string name, Func<JsonElement, bool> shape) =>
        !parent.TryGetProperty(name, out JsonElement value) || shape(value);

    /// <summary>The answers about a household: up to forty fields, each a short name and a short string.</summary>
    private static bool IsProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        int count = 0;
        foreach (JsonProperty field in profile.EnumerateObject())
        {
            count++;
            if (!FieldName().IsMatch(field.Name) || field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length > 40)
            {
                return false;
            }
        }

        return count is > 0 and <= 40;
    }

    /// <summary>Up to two hundred action ids.</summary>
    private static bool IsIds(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array && list.GetArrayLength() <= 200 && list.EnumerateArray().All(static id => id.ValueKind == JsonValueKind.String && id.GetString()!.Length <= 80 && Id().IsMatch(id.GetString()!));

    /// <summary>What was saved for later: up to a hundred offers, each named by its number and nothing else.</summary>
    private static bool IsShortlist(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array && list.GetArrayLength() <= 100 && list.EnumerateArray().All(static id => id.ValueKind == JsonValueKind.String && Offer().IsMatch(id.GetString()!));

    /// <summary>Up to twenty figures, each a whole number from nothing to ten thousand against an id.</summary>
    private static bool IsAmounts(JsonElement amounts) =>
        amounts.ValueKind == JsonValueKind.Object
        && amounts.EnumerateObject().Count() <= 20
        && amounts.EnumerateObject().All(static amount => Id().IsMatch(amount.Name) && IsWhole(amount.Value, 0, 10_000));

    /// <summary>The assumptions a household may change: the cash rate in whole pounds, and its own figures for the things it has.</summary>
    private static bool IsAssumptions(JsonElement assumptions) =>
        assumptions.ValueKind == JsonValueKind.Object
        && assumptions.EnumerateObject().All(static assumption => assumption.Name switch
        {
            "cashRate" => IsWhole(assumption.Value, 1, 100),
            "figures" => IsOwnFigures(assumption.Value),
            _ => false,
        });

    /// <summary>Up to twenty figures of the household's own, each a number from nothing to a million against a figure's id. Whether each is one the plan can use is the plan's business when it is built.</summary>
    private static bool IsOwnFigures(JsonElement figures) =>
        figures.ValueKind == JsonValueKind.Object
        && figures.EnumerateObject().Count() <= Selficient.Domain.Parameters.ParameterSet.MaxOwn
        && figures.EnumerateObject().All(static figure => figure.Name.Length <= 160 && Figure().IsMatch(figure.Name) && IsNumber(figure.Value, 0, 1_000_000));

    /// <summary>A pin in the United Kingdom and its waters.</summary>
    private static bool IsHouse(JsonElement house) =>
        HasOnly(house, ["lat", "lng"])
        && house.TryGetProperty("lat", out JsonElement lat) && IsNumber(lat, 49, 61)
        && house.TryGetProperty("lng", out JsonElement lng) && IsNumber(lng, -9, 2.5);

    /// <summary>Up to twelve things the household relies on, each within the bounds the plan form holds them to.</summary>
    private static bool IsDependencies(JsonElement list) =>
        list.ValueKind == JsonValueKind.Array && list.GetArrayLength() <= 12 && list.EnumerateArray().All(IsDependency);

    private static bool IsDependency(JsonElement dependency) =>
        HasOnly(dependency, DependencyFields)
        && dependency.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String && name.GetString()!.Length <= 40 && !string.IsNullOrWhiteSpace(name.GetString())
        && dependency.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && Kinds.Contains(kind.GetString(), StringComparer.Ordinal)
        && dependency.TryGetProperty("watts", out JsonElement watts) && IsNumber(watts, 0, 3000)
        && dependency.TryGetProperty("hoursPerDay", out JsonElement hours) && IsNumber(hours, 0, 24)
        && dependency.TryGetProperty("daysOfStock", out JsonElement days) && IsWhole(days, 0, 365)
        && dependency.TryGetProperty("mustStayCold", out JsonElement cold) && cold.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsNumber(JsonElement value, double least, double most) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) && number >= least && number <= most;

    private static bool IsWhole(JsonElement value, int least, int most) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= least && number <= most;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Id();

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Figure();

    [GeneratedRegex("^[0-9]{1,18}$", RegexOptions.CultureInvariant)]
    private static partial Regex Offer();

    [GeneratedRegex("^[A-Za-z]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldName();
}
