namespace Selficient.Domain.Resilience;

/// <summary>How a need stood on one day: its state, what carried it, and what failed it.</summary>
public sealed record DayState(CellState State, string? CarriedBy, string? FailedBy)
{
    /// <summary>A day on which the need did not arise.</summary>
    public static DayState NotNeeded { get; } = new(CellState.Held, null, null);
}

/// <summary>A cell on the board: how a need stood over a whole horizon, and for how many days from the start it was fully held.</summary>
public sealed record HorizonCell(Horizon Horizon, CellState State, int HeldDays);

/// <summary>A row on the board: a need, its days, and its cells.</summary>
public sealed record BoardRow(string Key, string Name, Rung Rung, bool Needed, IReadOnlyList<DayState> Days, IReadOnlyList<HorizonCell> Cells)
{
    /// <summary>The first day the need was rationed or lost, or null if it never was.</summary>
    public int? FirstRationedDay
    {
        get
        {
            for (int i = 0; i < Days.Count; i++)
            {
                if (Days[i].State >= CellState.Rationed)
                {
                    return i + 1;
                }
            }

            return null;
        }
    }
}

/// <summary>The board for one household in one scenario and season.</summary>
public sealed record Board(Scenario Scenario, Season Season, int DaysSimulated, IReadOnlyList<BoardRow> Rows)
{
    /// <summary>
    /// The headline: the first day on which anything on the Survive rung is rationed or lost. Half a
    /// day if that is the first day; the whole stretch if nothing is. A need met by a lesser means
    /// (blankets, candles) counts as surviving, except where the household cannot live on it, which
    /// the rows already decide.
    /// </summary>
    public decimal DaysReady
    {
        get
        {
            int? first = null;
            foreach (BoardRow row in Rows)
            {
                if (row.Rung == Rung.Survive && row.Needed && row.FirstRationedDay is { } day && (first is null || day < first))
                {
                    first = day;
                }
            }

            return first switch
            {
                null => DaysSimulated,
                1 => 0.5m,
                _ => first.Value - 1,
            };
        }
    }

    /// <summary>A row by its key, or null.</summary>
    public BoardRow? Row(string key) => Rows.FirstOrDefault(row => row.Key == key);
}

/// <summary>
/// The simulation. One deterministic walk through the days of a scenario, handing out what the
/// house has in ladder order, so the board, the headline and every "what would change it" are the
/// same arithmetic.
/// </summary>
public static class OutageBoard
{
    /// <summary>Simulates the scenario to its longest horizon.</summary>
    public static Board Simulate(ResilienceProfile profile, IReadOnlySet<string> held, Scenario scenario, Season season)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return Simulate(profile, held, scenario, season, scenario.LongestHorizon);
    }

    /// <summary>Simulates the scenario to the given horizon.</summary>
    public static Board Simulate(ResilienceProfile profile, IReadOnlySet<string> held, Scenario scenario, Season season, Horizon upTo)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(scenario);
        Run run = new(profile, held, NoFigures, scenario, season, (int)upTo);
        return run.Execute();
    }

    /// <summary>Simulates the scenario to its longest horizon for a household that has put its own figures on some of its stocks.</summary>
    public static Board Simulate(ResilienceProfile profile, Possessions possessions, Scenario scenario, Season season)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(possessions);
        ArgumentNullException.ThrowIfNull(scenario);
        Run run = new(profile, possessions.Held, possessions.Stock, scenario, season, (int)scenario.LongestHorizon);
        return run.Execute();
    }

    private static readonly Dictionary<string, decimal> NoFigures = [];

    private sealed class Run
    {
        private readonly ResilienceProfile _profile;
        private readonly Scenario _scenario;
        private readonly Season _season;
        private readonly int _days;
        private readonly List<Provider> _inPlay;
        private readonly HashSet<string> _inPlayIds;
        private readonly Dictionary<string, decimal> _stock = [];
        private readonly Dictionary<string, decimal> _energy = [];
        private readonly Dictionary<string, decimal> _drawnToday = [];
        private readonly Dictionary<string, decimal> _ranToday = [];
        private readonly Dictionary<NeedId, DayState?[]> _builtin = [];
        private readonly Dictionary<NeedId, List<(Provider Provider, Provision Provision)>> _byNeed = [];
        private readonly Dictionary<NeedId, bool> _needed = [];
        private readonly List<Provider> _stores = [];
        private readonly List<Provider> _flows = [];
        private readonly List<DayState[]> _dependencies = [];
        private decimal _electricity;

        public Run(ResilienceProfile profile, IReadOnlySet<string> held, IReadOnlyDictionary<string, decimal> figures, Scenario scenario, Season season, int days)
        {
            _profile = profile;
            _scenario = scenario;
            _season = season;
            _days = days;
            IReadOnlySet<string> fixtures = profile.Fixtures;
            _inPlay = Providers.All.Where(p => (fixtures.Contains(p.Id) || held.Contains(p.Id)) && profile.CanHave(p.Where)).ToList();
            _inPlayIds = _inPlay.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (Need need in Needs.All)
            {
                _builtin[need.Id] = new DayState?[days];
                _byNeed[need.Id] = [];
                _needed[need.Id] = false;
            }

            // Everything the hot loop needs is laid out once here, so a day costs no allocation.
            foreach (Provider provider in _inPlay)
            {
                if (provider.Stock is { } stock)
                {
                    // The household's own figure where it gave one, already in the stock's unit; the plan's otherwise.
                    _stock[provider.Id] = figures.TryGetValue(provider.Id, out decimal counted)
                        ? counted
                        : provider.Id == "medicine-stock"
                            ? profile.MedicineDaysInStock
                            : stock.Quantity * (stock.PerPerson ? profile.Household.Size.Count : 1);
                }
                else if (provider.Flow is not null)
                {
                    _stock[provider.Id] = 0m;
                    _flows.Add(provider);
                }

                if (provider.Store is { } store)
                {
                    _energy[provider.Id] = store.UsableWh - store.ReserveWh;
                    _stores.Add(provider);
                }

                foreach (Provision provision in provider.Meets)
                {
                    _byNeed[provision.Need].Add((provider, provision));
                }
            }

            for (int i = 0; i < profile.Dependencies.Count; i++)
            {
                _dependencies.Add(new DayState[days]);
                Dependency dependency = profile.Dependencies[i];
                if (dependency.Kind != DependencyKind.PoweredDevice)
                {
                    _stock[DependencyKey(i)] = dependency.DaysOfStock;
                }
            }
        }

        public Board Execute()
        {
            for (int day = 1; day <= _days; day++)
            {
                _electricity = _scenario.Availability(Utility.Electricity, day);
                _drawnToday.Clear();
                _ranToday.Clear();
                Refill(day);

                // Rung by rung, the household's own dependencies taking their place on their rung,
                // so an oxygen concentrator is fed before the lights and the lights before the fridge.
                foreach (Rung rung in Enum.GetValues<Rung>())
                {
                    foreach (Need need in Needs.All)
                    {
                        if (need.Rung == rung)
                        {
                            StateOf(need.Id, day);
                        }
                    }

                    for (int i = 0; i < _profile.Dependencies.Count; i++)
                    {
                        if (_profile.Dependencies[i].Rung == rung)
                        {
                            _dependencies[i][day - 1] = EvaluateDependency(i, day);
                        }
                    }
                }
            }

            List<BoardRow> rows = [];
            foreach (Need need in Needs.All)
            {
                DayState?[] computed = _builtin[need.Id];
                DayState[] days = new DayState[computed.Length];
                for (int i = 0; i < computed.Length; i++)
                {
                    days[i] = computed[i]!;
                }

                rows.Add(new BoardRow(need.Id.ToString(), need.Name, need.Rung, _needed[need.Id], days, Cells(days)));
            }

            for (int i = 0; i < _profile.Dependencies.Count; i++)
            {
                Dependency dependency = _profile.Dependencies[i];
                rows.Add(new BoardRow(DependencyKey(i), dependency.Name, dependency.Rung, Needed: true, _dependencies[i], Cells(_dependencies[i])));
            }

            return new Board(_scenario, _season, _days, rows);
        }

        private static string DependencyKey(int index) => $"dependency-{index}";

        /// <summary>A need's state today, worked out once and remembered, so a dependency can ask for it early.</summary>
        private DayState StateOf(NeedId need, int day)
        {
            DayState? state = _builtin[need][day - 1];
            if (state is null)
            {
                state = Evaluate(need, day);
                _builtin[need][day - 1] = state;
            }

            return state;
        }

        private List<HorizonCell> Cells(DayState[] days)
        {
            List<HorizonCell> cells = [];
            foreach (Horizon horizon in _scenario.Horizons)
            {
                int span = Math.Min((int)horizon, _days);
                if (span < (int)horizon)
                {
                    continue;
                }

                // The cell is the worst day in the stretch; the held days say how far in it got.
                int heldDays = 0;
                while (heldDays < span && days[heldDays].State == CellState.Held)
                {
                    heldDays++;
                }

                CellState state = CellState.Held;
                for (int i = 0; i < span; i++)
                {
                    if (days[i].State > state)
                    {
                        state = days[i].State;
                    }
                }

                cells.Add(new HorizonCell(horizon, state, heldDays));
            }

            return cells;
        }

        private void Refill(int day)
        {
            foreach (Provider provider in _flows)
            {
                if (day > provider.Flow!.LeadTimeDays)
                {
                    _stock[provider.Id] += provider.Flow.PerDay(_season);
                }
            }

            foreach (Provider provider in _stores)
            {
                EnergyStore store = provider.Store!;
                if (store.Refill == RefillSource.None)
                {
                    continue;
                }

                decimal refill = store.Refill switch
                {
                    RefillSource.Roof => store.RefillWh(_season),
                    RefillSource.Panels => _profile.CanHave(Placement.OutdoorsOnly) ? store.RefillWh(_season) : 0m,
                    _ => store.RefillWh(_season) * _scenario.Availability(Utility.Fuel, day),
                };
                _energy[provider.Id] = Math.Min(store.UsableWh - store.ReserveWh, _energy[provider.Id] + refill);
            }
        }

        private decimal Availability(Utility utility, int day)
        {
            decimal availability = _scenario.Availability(utility, day);
            if (utility == Utility.MainsWater && _profile.WaterIsPumped)
            {
                availability = Math.Min(availability, _electricity);
            }

            // A home on its own supply has no main to burst: what it asks of the water main it asks of its own tap, which the tap's fixture answers.
            if (utility == Utility.MainsWater && _profile.Profile.HasOwnWater)
            {
                availability = 1m;
            }

            return availability;
        }

        private decimal UtilityFactor(Provider provider, int day)
        {
            decimal factor = 1m;
            foreach (Utility utility in provider.NeedsUtilities)
            {
                factor = Math.Min(factor, Availability(utility, day));
            }

            return factor;
        }

        /// <summary>
        /// Carries a load when the grid is short, from the stores that can take its burst, in the
        /// order the needs are evaluated. Returns the share of the day the load ran: all of it on
        /// the mains, the mains' share plus whatever the stores could add otherwise.
        /// </summary>
        private decimal Carry(Load load)
        {
            if (_electricity >= 1m)
            {
                return 1m;
            }

            decimal wh = load.WhPerDay(_season) * (1m - _electricity);
            decimal drawn = 0m;
            bool plugged = _inPlayIds.Contains("boiler-plug-and-socket");

            // The fullest eligible store first, then the next, without sorting or allocating:
            // there are never more than a handful of stores in a house.
            while (drawn < wh)
            {
                Provider? fullest = null;
                foreach (Provider candidate in _stores)
                {
                    EnergyStore store = candidate.Store!;
                    bool eligible = store.CeilingW >= load.BurstW && (!load.Hardwired || store.WholeHouse || (plugged && load.BoilerPlugFrees)) && _energy[candidate.Id] > 0m;
                    if (eligible && (fullest is null || _energy[candidate.Id] > _energy[fullest.Id]))
                    {
                        fullest = candidate;
                    }
                }

                if (fullest is null)
                {
                    break;
                }

                decimal take = Math.Min(wh - drawn, _energy[fullest.Id]);
                _energy[fullest.Id] -= take;
                drawn += take;
            }

            return _electricity + (1m - _electricity) * (drawn / wh);
        }

        private DayState Evaluate(NeedId need, int day)
        {
            decimal demand = Demand.For(_profile, need, _scenario, _season, day);
            if (demand <= 0m)
            {
                return DayState.NotNeeded;
            }

            _needed[need] = true;
            decimal full = 0m;
            decimal partial = 0m;
            string? carried = null;
            string? failed = null;
            foreach ((Provider provider, Provision provision) in _byNeed[need])
            {
                if (full >= demand)
                {
                    break;
                }

                if (!Prerequisites(provider, provision, need, day))
                {
                    failed ??= provider.Id;
                    continue;
                }

                decimal gap = demand - full;
                decimal amount = Supply(provider, provision, gap, day);
                if (amount <= 0m)
                {
                    failed ??= provider.Id;
                    continue;
                }

                carried ??= provider.Id;
                if (provision.Partial)
                {
                    partial += amount;
                }
                else
                {
                    full += amount;
                }
            }

            if (need == NeedId.Warmth && _profile.NeedsFullWarmth)
            {
                partial = 0m;
            }

            CellState state = full >= demand ? CellState.Held
                : full + partial >= demand ? CellState.Reduced
                : full + partial > 0m ? CellState.Rationed
                : CellState.Lost;
            return new DayState(state, state == CellState.Lost ? null : carried, failed);
        }

        private bool Prerequisites(Provider provider, Provision provision, NeedId need, int day)
        {
            if (provision.RequiresHeld is { } other && StateOf(other, day).State != CellState.Held)
            {
                return false;
            }

            if (provision.RequiresAnyProvider is { } any)
            {
                bool present = false;
                foreach (string id in any)
                {
                    present |= _inPlayIds.Contains(id);
                }

                if (!present)
                {
                    return false;
                }
            }

            // Context: in a wide outage the sewers stop with the power, so water in a cistern is no way to
            // flush. Only something that needs no sewer, a bag in the bowl, meets sanitation on those days.
            // A home with a tank of its own is not on the sewers, so water is still a way to flush there.
            if (need == NeedId.Sanitation && _scenario.SewersStopWithThePower && !_profile.Profile.Circumstances.OwnDrains && !provision.WorksWithoutSewers && _scenario.Availability(Utility.Electricity, day) < 1m)
            {
                return false;
            }

            // Context: a boil notice makes tap water depend on cooking; formula depends on boiled water too.
            if (need == NeedId.DrinkingWater && provider.Id == "mains-tap" && _scenario.BoilWaterNotice && StateOf(NeedId.Cooking, day).State != CellState.Held)
            {
                return false;
            }

            if (need == NeedId.Dependants && _profile.Infants > 0 && StateOf(NeedId.Cooking, day).State != CellState.Held)
            {
                return false;
            }

            return true;
        }

        private decimal Supply(Provider provider, Provision provision, decimal gap, int day)
        {
            decimal factor = UtilityFactor(provider, day);
            if (factor <= 0m)
            {
                return 0m;
            }

            decimal cap = provision.PerDay > 0m ? Math.Min(provision.PerDay, gap) : gap;
            if (provider.Stock is { Shared: true })
            {
                // One day of a shared stock serves every need it meets; only the extra is spent.
                decimal already = _drawnToday.GetValueOrDefault(provider.Id);
                decimal take = Math.Min(_stock[provider.Id] + already, cap * factor);
                decimal extra = Math.Max(0m, take - already);
                _stock[provider.Id] -= extra;
                _drawnToday[provider.Id] = Math.Max(already, take);
                return take;
            }

            if (provider.Stock is not null || provider.Flow is not null)
            {
                decimal take = Math.Min(_stock[provider.Id], cap * factor);
                _stock[provider.Id] -= take;
                return take;
            }

            // A fixture's load is carried once a day, however many needs it meets: the boiler's
            // pump warms the house and heats the water on the same energy.
            decimal ran = 1m;
            if (provider.Load is { } load)
            {
                if (!_ranToday.TryGetValue(provider.Id, out ran))
                {
                    ran = Carry(load);
                    _ranToday[provider.Id] = ran;
                }
            }

            return cap * factor * ran;
        }

        private DayState EvaluateDependency(int index, int day)
        {
            Dependency dependency = _profile.Dependencies[index];
            if (dependency.MustStayCold && StateOf(NeedId.ColdStorage, day).State == CellState.Lost)
            {
                return new DayState(CellState.Lost, null, "fridge-freezer");
            }

            if (dependency.Kind == DependencyKind.PoweredDevice)
            {
                decimal ran = Carry(new Load(dependency.WhPerDay, dependency.WhPerDay, dependency.Watts));
                return ran >= 1m
                    ? new DayState(CellState.Held, "the mains or a source", null)
                    : ran > 0m ? new DayState(CellState.Rationed, "the mains, for part of the day", "the sources")
                    : new DayState(CellState.Lost, null, "the sources");
            }

            // A medicine comes from the pharmacy while it dispenses; a supply from the shops or deliveries; then the stock.
            Utility outside = dependency.Kind == DependencyKind.Medicine ? Utility.Pharmacy : Utility.Shops;
            decimal fromOutside = Math.Min(Availability(outside, day), Availability(Utility.Roads, day));
            if (fromOutside >= 1m)
            {
                return new DayState(CellState.Held, outside == Utility.Pharmacy ? "pharmacy" : "shops", null);
            }

            string key = DependencyKey(index);
            decimal take = Math.Min(_stock[key], 1m - fromOutside);
            _stock[key] -= take;
            decimal total = fromOutside + take;
            return total >= 1m ? new DayState(CellState.Held, "the stock in the house", null)
                : total > 0m ? new DayState(CellState.Rationed, "the stock in the house", outside == Utility.Pharmacy ? "pharmacy" : "shops")
                : new DayState(CellState.Lost, null, "the stock in the house");
        }
    }
}
