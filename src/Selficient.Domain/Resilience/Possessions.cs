using Selficient.Domain.Households;
using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Resilience;

/// <summary>A plan action that a measured stock counts towards, and how much of the stock it asks this household for.</summary>
public sealed record MeasuredAction(string ActionId, Func<ResilienceProfile, decimal> Target);

/// <summary>
/// A stock a household can put its own figure on: ten days of tins where the plan's rows say three
/// days and a month. The figure sets the stock of one provider, and the plan actions it counts
/// towards are held when the figure reaches what each asks for. Actions are listed smallest first.
/// </summary>
/// <param name="Id">A stable id for the figure, as the browser stores and posts it.</param>
/// <param name="ProviderId">The provider whose stock the figure sets.</param>
/// <param name="Actions">The plan actions the figure counts towards, smallest target first.</param>
/// <param name="Max">The largest figure accepted.</param>
/// <param name="ToStock">The figure in the provider's own unit for this household.</param>
/// <param name="ProratesCost">Whether an action part met costs only the part still missing. True for things bought by the unit; false where the first purchase is the kit itself.</param>
public sealed record MeasuredStock(
    string Id,
    string ProviderId,
    IReadOnlyList<MeasuredAction> Actions,
    int Max,
    Func<ResilienceProfile, decimal, decimal> ToStock,
    bool ProratesCost);

/// <summary>The stocks a household can measure for itself.</summary>
public static class MeasuredStocks
{
    /// <summary>The id of the food figure: days of meals for the whole household.</summary>
    public const string FoodDays = "food-days";

    /// <summary>The id of the bottled water figure, in litres.</summary>
    public const string BottledWaterLitres = "bottled-water-litres";

    /// <summary>The id of the figure for water in cans, in litres.</summary>
    public const string WaterCansLitres = "water-cans-litres";

    /// <summary>The id of the cash figure, in pounds.</summary>
    public const string CashPounds = "cash-pounds";

    /// <summary>The id of the stove cartridge figure.</summary>
    public const string StoveCartridges = "stove-cartridges";

    /// <summary>
    /// What a person's essentials cost a day when only cash will do, in pounds. An assumption of
    /// ours, not a published figure: it turns the pounds a household says it holds into the days
    /// the board counts in, and ten days of it for four people is £400.
    /// </summary>
    public static decimal CashPoundsPerPersonPerDay => 10m;

    /// <summary>Every measured stock.</summary>
    public static IReadOnlyList<MeasuredStock> All { get; } =
    [
        new(FoodDays, "stored-food", [new("food-no-cook-three-days", static _ => 3m), new("food-one-month", static _ => 30m)], 365, static (profile, days) => days * profile.Household.Size.Count, ProratesCost: true),
        new(BottledWaterLitres, "water-bottled-three-days", [new("water-bottled-three-days", static profile => 3m * profile.Household.Size.Count * Litres.DrinkingPerPersonPerDay)], 2000, static (_, litres) => litres, ProratesCost: true),
        new(WaterCansLitres, "water-cans", [new("water-cans", CanLitres)], 2000, static (_, litres) => litres, ProratesCost: true),
        new(CashPounds, "cash-in-small-notes", [new("cash-in-small-notes", static profile => 10m * profile.Household.Size.Count * CashPoundsPerPersonPerDay)], 10000, static (profile, pounds) => pounds / (profile.Household.Size.Count * CashPoundsPerPersonPerDay), ProratesCost: false),
        new(StoveCartridges, "butane-cartridge-stove", [new("butane-cartridge-stove", static _ => 10m)], 200, static (_, cartridges) => cartridges, ProratesCost: false),
    ];

    private static readonly Dictionary<string, MeasuredStock> ById = All.ToDictionary(static stock => stock.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, MeasuredStock> ByAction = All
        .SelectMany(static stock => stock.Actions.Select(action => (action.ActionId, Stock: stock)))
        .ToDictionary(static pair => pair.ActionId, static pair => pair.Stock, StringComparer.Ordinal);

    /// <summary>The measured stock with this id, or null.</summary>
    public static MeasuredStock? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>The measured stock this plan action counts towards, or null when the action is simply had or not.</summary>
    public static MeasuredStock? ForAction(string actionId) => ByAction.GetValueOrDefault(actionId);

    /// <summary>What this action asks this household for, in the stock's own figure.</summary>
    public static decimal Target(MeasuredStock stock, string actionId, ResilienceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(profile);
        return stock.Actions.First(action => action.ActionId == actionId).Target(profile);
    }

    /// <summary>Checks figures that arrived from a browser: each must name a measured stock and sit inside its range.</summary>
    public static Result<IReadOnlyDictionary<string, int>> Check(IReadOnlyDictionary<string, int> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        foreach ((string id, int amount) in amounts)
        {
            if (Find(id) is not { } stock)
            {
                return Result.Failure<IReadOnlyDictionary<string, int>>(new Fault("plan.amounts", "One of the amounts is for something the plan does not measure."));
            }

            if (amount < 0 || amount > stock.Max)
            {
                return Result.Failure<IReadOnlyDictionary<string, int>>(new Fault("plan.amounts", "One of the amounts is below zero or more than the plan can count."));
            }
        }

        return Result.Success(amounts);
    }

    // The cans the plan asks for: one a person in a house, one between two in a flat, 25 litres each.
    private static decimal CanLitres(ResilienceProfile profile)
    {
        int people = profile.Household.Size.Count;
        return 25m * (profile.Dwelling.Type == DwellingType.Flat ? Math.Max(1, (people + 1) / 2) : people);
    }
}

/// <summary>How far a household's own figure takes it towards a plan action: what it has, what the action asks, and the share of the action's cost still to spend.</summary>
public sealed record Progress(string ActionId, string StockId, decimal Amount, decimal Target, decimal CostRemaining);

/// <summary>
/// What a household has, as the engine needs it: the plan actions it holds and the figures it put
/// on its measured stocks. A figure overrides the tick for every action it counts towards: ten
/// days of food holds the three-day shelf and not the month's, whatever was ticked. Adding an
/// action to a measured stock raises the figure to what the action asks for, never past it, so
/// "what would this action do" tops the shelf up rather than stacking a second shelf on it.
/// </summary>
public sealed class Possessions
{
    private readonly ResilienceProfile _profile;
    private readonly HashSet<string> _have;
    private readonly Dictionary<string, decimal> _amounts;

    private Possessions(ResilienceProfile profile, HashSet<string> have, Dictionary<string, decimal> amounts)
    {
        _profile = profile;
        _have = have;
        _amounts = amounts;
    }

    /// <summary>The plan actions held, after the figures have had their say.</summary>
    public IReadOnlySet<string> Have => _have;

    /// <summary>
    /// What the household has. The figures must already have passed <see cref="MeasuredStocks.Check"/>;
    /// one that names no measured stock is a programming error here and throws.
    /// </summary>
    public static Possessions Of(ResilienceProfile profile, IReadOnlySet<string> have, IReadOnlyDictionary<string, int>? amounts = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(have);
        Possessions possessions = new(profile, new HashSet<string>(have, StringComparer.Ordinal), new Dictionary<string, decimal>(StringComparer.Ordinal));
        foreach ((string id, int amount) in amounts ?? new Dictionary<string, int>())
        {
            MeasuredStock stock = MeasuredStocks.Find(id) ?? throw new ArgumentException("A figure names a stock the plan does not measure.", nameof(amounts));
            possessions._amounts[id] = amount;
            possessions.Settle(stock);
        }

        return possessions;
    }

    /// <summary>A copy that can be added to without changing this one.</summary>
    public Possessions Copy() => new(_profile, new HashSet<string>(_have, StringComparer.Ordinal), new Dictionary<string, decimal>(_amounts, StringComparer.Ordinal));

    /// <summary>
    /// Adds an action as if it were done. For an action a stated figure counts towards, the figure
    /// rises to what the action asks; for any other, the action is simply held.
    /// </summary>
    public void Add(string actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        if (MeasuredStocks.ForAction(actionId) is { } stock && _amounts.TryGetValue(stock.Id, out decimal amount))
        {
            _amounts[stock.Id] = Math.Max(amount, MeasuredStocks.Target(stock, actionId, _profile));
            Settle(stock);
            return;
        }

        _have.Add(actionId);
    }

    /// <summary>
    /// The providers in play for the engine: the actions held, with each stated stock standing in
    /// for the actions it counts towards. With no figures given it is the actions held and nothing
    /// is copied, which is every household that has not counted anything.
    /// </summary>
    public IReadOnlySet<string> Held
    {
        get
        {
            if (_amounts.Count == 0)
            {
                return _have;
            }

            HashSet<string> held = new(_have, StringComparer.Ordinal);
            foreach ((string id, decimal amount) in _amounts)
            {
                MeasuredStock stock = MeasuredStocks.Find(id)!;
                foreach (MeasuredAction action in stock.Actions)
                {
                    held.Remove(action.ActionId);
                }

                if (amount > 0m)
                {
                    held.Add(stock.ProviderId);
                }
            }

            return held;
        }
    }

    /// <summary>The stock each stated figure gives its provider, in the provider's own unit.</summary>
    public IReadOnlyDictionary<string, decimal> Stock
    {
        get
        {
            Dictionary<string, decimal> stocks = new(StringComparer.Ordinal);
            foreach ((string id, decimal amount) in _amounts)
            {
                MeasuredStock stock = MeasuredStocks.Find(id)!;
                stocks[stock.ProviderId] = stock.ToStock(_profile, amount);
            }

            return stocks;
        }
    }

    /// <summary>How far the household's figure takes it towards an action, or null when no figure has been put on that action's stock.</summary>
    public Progress? ProgressOn(string actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        if (MeasuredStocks.ForAction(actionId) is not { } stock || !_amounts.TryGetValue(stock.Id, out decimal amount))
        {
            return null;
        }

        decimal target = MeasuredStocks.Target(stock, actionId, _profile);
        decimal missing = Math.Max(0m, target - amount) / target;
        return new Progress(actionId, stock.Id, amount, target, stock.ProratesCost ? missing : Math.Ceiling(missing));
    }

    // The figure decides which of its actions are held: every one it reaches, and none it does not.
    private void Settle(MeasuredStock stock)
    {
        decimal amount = _amounts[stock.Id];
        foreach (MeasuredAction action in stock.Actions)
        {
            if (amount >= action.Target(_profile))
            {
                _have.Add(action.ActionId);
            }
            else
            {
                _have.Remove(action.ActionId);
            }
        }
    }
}
