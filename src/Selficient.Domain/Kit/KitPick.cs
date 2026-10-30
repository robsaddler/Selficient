using Selficient.Domain.Measures;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Kit;

/// <summary>One product someone could buy for a plan action, with the price as it was seen and when.</summary>
public sealed record KitPick
{
    /// <summary>Longest name or note accepted, so a catalogue entry can never dominate a page.</summary>
    public static int MaxTextLength => 160;

    private KitPick(ActionId action, string name, string retailer, Money price, DateOnly seen, Uri link, string note)
    {
        Action = action;
        Name = name;
        Retailer = retailer;
        Price = price;
        Seen = seen;
        Link = link;
        Note = note;
    }

    /// <summary>The plan action this is for.</summary>
    public ActionId Action { get; }

    /// <summary>What it is.</summary>
    public string Name { get; }

    /// <summary>Who sells it.</summary>
    public string Retailer { get; }

    /// <summary>The price seen.</summary>
    public Money Price { get; }

    /// <summary>When the price was seen.</summary>
    public DateOnly Seen { get; }

    /// <summary>Where to find it. Always absolute https.</summary>
    public Uri Link { get; }

    /// <summary>A sentence on why this one, or empty.</summary>
    public string Note { get; }

    /// <summary>Creates a pick, refusing anything that could not be shown safely.</summary>
    public static Result<KitPick> Create(string actionId, string name, string retailer, decimal pricePounds, DateOnly seen, Uri? link, string? note)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxTextLength || string.IsNullOrWhiteSpace(retailer) || retailer.Length > MaxTextLength || (note?.Length ?? 0) > MaxTextLength)
        {
            return Result.Failure<KitPick>(DomainErrors.KitPickTextOutOfRange());
        }

        if (link is null || !link.IsAbsoluteUri || link.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure<KitPick>(DomainErrors.KitPickLinkNotHttps());
        }

        ActionId action;
        try
        {
            action = ActionId.From(actionId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<KitPick>(DomainErrors.KitPickActionInvalid());
        }

        return Money.FromPounds(pricePounds).Map(price => new KitPick(action, name.Trim(), retailer.Trim(), price, seen, link, (note ?? string.Empty).Trim()));
    }
}
