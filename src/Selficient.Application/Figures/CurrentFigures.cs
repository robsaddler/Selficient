using Selficient.Domain.Parameters;

namespace Selficient.Application.Figures;

/// <summary>The figures every plan built now is worked out from.</summary>
public interface ICurrentFigures
{
    /// <summary>The set in force: the shipped figures until something is published.</summary>
    ParameterSet Current { get; }

    /// <summary>When the set in force was published, or null while the shipped figures stand.</summary>
    DateTimeOffset? PublishedAt { get; }
}

/// <summary>The shipped figures and nothing else, for a host with no store and for tests.</summary>
public sealed class ShippedFigures : ICurrentFigures
{
    /// <inheritdoc />
    public ParameterSet Current => ParameterSet.Default;

    /// <inheritdoc />
    public DateTimeOffset? PublishedAt => null;
}

/// <summary>
/// The published figures, read from the store and held in memory so that a plan costs no query. It
/// is read at start and after every publication. A stored correction the code no longer accepts,
/// because a release removed the figure or narrowed its range, is dropped and named, never obeyed
/// and never allowed to stop the site: the plan is worked out from every correction that still stands.
/// </summary>
public sealed class CurrentFigures : ICurrentFigures
{
    private readonly IFigureStore _store;
    private volatile State _state = new(ParameterSet.Default, null, []);

    /// <summary>Creates it over the store, holding the shipped figures until the store has been read.</summary>
    public CurrentFigures(IFigureStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public ParameterSet Current => _state.Set;

    /// <inheritdoc />
    public DateTimeOffset? PublishedAt => _state.Published?.At;

    /// <summary>The publication in force, or null while the shipped figures stand.</summary>
    public PublishedFigures? Published => _state.Published;

    /// <summary>A sentence for each stored correction that was not applied, and why.</summary>
    public IReadOnlyList<string> Dropped => _state.Dropped;

    /// <summary>True once the store has been read at least once.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Reads the set in force from the store.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        PublishedFigures? published = await _store.CurrentAsync(cancellationToken);
        _state = published is null ? new State(ParameterSet.Default, null, []) : Make(published);
        IsLoaded = true;
    }

    private static State Make(PublishedFigures published)
    {
        Domain.Primitives.Result<ParameterSet> whole = ParameterSet.Create(published.Version, published.Corrections);
        if (whole.IsSuccess)
        {
            return new State(whole.Value, published, []);
        }

        // Something stored is no longer acceptable. Keep each correction that stands on its own and say which did not.
        Dictionary<string, decimal> kept = [];
        List<string> dropped = [];
        foreach ((string id, decimal value) in published.Corrections)
        {
            if (ParameterRegistry.Find(id) is { } parameter && parameter.Accepts(value))
            {
                kept[id] = value;
            }
            else
            {
                dropped.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The correction of \"{id}\" to {value} is no longer one the plan accepts, and was not applied."));
            }
        }

        Domain.Primitives.Result<ParameterSet> standing = ParameterSet.Create(published.Version, kept);
        if (standing.IsSuccess)
        {
            return new State(standing.Value, published, dropped);
        }

        // What is left contradicts itself, so none of it is applied: the shipped figures, under this set's number.
        dropped.Add($"{standing.Error.Message} No correction in this set was applied.");
        return new State(ParameterSet.Create(published.Version, new Dictionary<string, decimal>()).Value, published, dropped);
    }

    private sealed record State(ParameterSet Set, PublishedFigures? Published, IReadOnlyList<string> Dropped);
}
