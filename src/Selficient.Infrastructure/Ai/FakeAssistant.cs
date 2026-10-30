using Selficient.Application.Ai;
using Selficient.Domain.Plans;
using Selficient.Domain.Primitives;

namespace Selficient.Infrastructure.Ai;

/// <summary>
/// Deterministic answers for development without a key and for every test. The cupboard holds
/// what Rob's would after the first week; the boiler is the one on the EPC; the board is modern.
/// An image whose first pixel row is all black stands for "the model could not answer".
/// </summary>
public sealed class FakeAssistant : IPhotoAnalyst, IPlanNarrator
{
    /// <summary>A sanitised image this large is treated as unreadable, so the failure path can be exercised.</summary>
    public const int UnreadableWidth = 1599;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<Result<KitInventory>> KitAsync(SanitisedImage image, IReadOnlyList<KitCandidate> candidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(candidates);
        if (image.Width == UnreadableWidth)
        {
            return Task.FromResult(Result.Failure<KitInventory>(DomainErrors.AiUnavailable()));
        }

        HashSet<string> known = [.. candidates.Select(static c => c.Id.Value)];
        List<KitItem> items =
        [
            new("Two-litre bottles of water", 18, Pick(known, "water-bottled-three-days")),
            new("Head torches", 2, Pick(known, "headtorches")),
            new("Lithium AA batteries", 12, Pick(known, "radio-dab-fm-with-crank")),
            new("Tins of beans and soup", 14, Pick(known, "food-no-cook-three-days")),
            new("A box of tea lights", 1, null),
        ];
        return Task.FromResult(Result.Success(new KitInventory(items, "You already have three days of water and the start of a no-cook larder; light and a radio are the gaps.")));
    }

    /// <inheritdoc />
    public Task<Result<BoilerReading>> BoilerAsync(SanitisedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.FromResult(image.Width == UnreadableWidth
            ? Result.Failure<BoilerReading>(DomainErrors.AiUnavailable())
            : Result.Success(new BoilerReading("Worcester Bosch", "Greenstar 30i", 120, "A 120 W boiler runs happily from a 1 kWh power station for ten hours of firing. Ask an electrician to fit a plug and socket on its fused spur; the manual allows it.")));
    }

    /// <inheritdoc />
    public Task<Result<ConsumerUnitReading>> ConsumerUnitAsync(SanitisedImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Task.FromResult(image.Width == UnreadableWidth
            ? Result.Failure<ConsumerUnitReading>(DomainErrors.AiUnavailable())
            : Result.Success(new ConsumerUnitReading(2, "a modern RCBO board", "Two spare ways is enough for a small backup board feeding the lights, fridge and router through a changeover. An electrician can do it in a morning.")));
    }

    /// <inheritdoc />
    public Task<Result<PlanNarrative>> NarrateAsync(Plan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        int actions = plan.Actions.Count();
        return Task.FromResult(Result.Success(new PlanNarrative(
        [
            $"Your house sits where the weather arrives first, so the plan starts with the power. {plan.Risks.Ranked[0].Rationale}",
            $"The first stretch is about the basics that cost least and matter most: water you can drink, food that needs no cooking, light that is not a candle, and a way to hear the news. Together they are what three days without the grid looks like in practice.",
            $"The later actions buy comfort and independence rather than survival, and they are where the money goes. Each one is there because of a risk above, not because a list said so. There are {actions} actions in all.",
            "None of it works if the water is stale and the batteries are flat, so the calendar matters as much as the shopping. Put the clock-change reminders in and the plan looks after itself.",
        ])));
    }

    private static ActionId? Pick(HashSet<string> known, string id) => known.Contains(id) ? ActionId.From(id) : null;
}
