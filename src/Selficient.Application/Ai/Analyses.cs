using Selficient.Domain.Plans;

namespace Selficient.Application.Ai;

/// <summary>The three photo moments. There is no general upload.</summary>
public enum AnalysisKind
{
    /// <summary>The kit cupboard: what is already there, counted.</summary>
    Kit,

    /// <summary>The boiler's data plate: make, model, electrical load, plug feasibility.</summary>
    Boiler,

    /// <summary>The consumer unit: spare ways for a backup board.</summary>
    ConsumerUnit,
}

/// <summary>One thing seen in the cupboard.</summary>
/// <param name="Name">What it is, in the model's words, capped and encoded on the page.</param>
/// <param name="Count">How many.</param>
/// <param name="Action">The plan action it satisfies, if any; only ids from the candidate list are accepted.</param>
public sealed record KitItem(string Name, int Count, ActionId? Action);

/// <summary>What the cupboard holds.</summary>
/// <param name="Items">Everything recognised, most useful first.</param>
/// <param name="Summary">One sentence for the page.</param>
public sealed record KitInventory(IReadOnlyList<KitItem> Items, string Summary)
{
    /// <summary>The plan actions the photo satisfies, without duplicates.</summary>
    public IReadOnlyList<ActionId> Satisfied => Items.Where(static i => i.Action is not null).Select(static i => i.Action!.Value).Distinct().ToList();
}

/// <summary>What the boiler plate says.</summary>
/// <param name="Make">The maker.</param>
/// <param name="Model">The model.</param>
/// <param name="Watts">Electrical load in watts, if printed.</param>
/// <param name="Advice">One or two sentences on running it from a power station.</param>
public sealed record BoilerReading(string Make, string Model, int? Watts, string Advice);

/// <summary>What the consumer unit shows.</summary>
/// <param name="SpareWays">Empty ways with no breaker, if visible.</param>
/// <param name="Kind">Plain words for the board: split-load, RCBO, old fuse box.</param>
/// <param name="Advice">One or two sentences on fitting a backup board.</param>
public sealed record ConsumerUnitReading(int? SpareWays, string Kind, string Advice);

/// <summary>The plan in plain words.</summary>
/// <param name="Paragraphs">Three to six paragraphs, each a few sentences.</param>
public sealed record PlanNarrative(IReadOnlyList<string> Paragraphs);
