# The engine's edge cases: what it knows, and what it does not yet

*30 October 2026. The survivalist's checklist for the v2 engine (`src/Selficient.Domain/Resilience`). Each line says how the engine handles the case and which test pins it. The last section is what is still open, for Rob to add to or strike out.*

## Handled and pinned

**Power**
- A gas, oil or LPG boiler is warmth only while its pump and controls have electricity: about 1.2 kWh a winter day. `Robs_house_with_nothing_done_stands_a_day_in_a_storm`
- A hardwired boiler cannot be plugged into a portable power station; it needs the boiler-on-a-plug job or a whole-house battery. `A_portable_station_needs_the_boiler_on_a_plug_to_help_with_warmth`
- A heat pump is 16 to 25 kWh a cold day; nothing short of the car or a generator carries it, and not for long. `Blankets_do_not_count_for_a_frail_person_or_a_baby`
- Energy is handed out rung by rung: the boiler before the hob, the lights before the fridge, the fridge before the television; a load that meets several needs (the boiler warms the house and heats the water) is charged once. `A_whole_house_battery_runs_the_boiler_before_the_hob_and_the_fridge_before_the_television`
- A source carries a load for the fraction of the day it can, so a half-empty station still runs the boiler for half a day rather than nothing. `Energy_sources_refill_only_where_they_can`
- Solar panels alone give nothing in a cut; only the battery-and-gateway provider exists as a source. The roof refills it at about 3 kWh a December day. Folding panels refill a station only where there is somewhere outdoors to put them. `Energy_sources_refill_only_where_they_can`
- A generator runs only outdoors (not in a flat without a garden), on twenty litres unless the forecourts are pumping. Same test.
- An electric car with vehicle-to-load is a 40 kWh source at 3 kW with 20 kWh kept back to drive; an electric car without it is a load. `The_car_powers_the_house_but_keeps_its_reserve`
- Power banks charge phones and nothing with a plug. `Power_banks_carry_the_phones_and_nothing_with_a_plug`
- Intermittent cuts (rolling four-hour cuts in a long crisis, evening cuts in a heatwave) are a fraction of the day, and a load on the mains for that fraction is "rationed", not held. `Household_dependencies_sit_on_their_rung_and_decide_the_headline`

**Water**
- Mains water stays on in a power cut, except in a flat above the third floor, where the block's pumps stop with the power. `A_fifth_floor_flat_loses_its_water_with_the_power`
- A water cut lasts three days here and comes back under a boil notice, so the tap is drinking water only once there is cooking. `Formula_depends_on_boiled_water_and_a_boil_notice_puts_the_tap_behind_the_hob`
- A water butt flushes but does not drink; a gravity filter turns butt, liner or can water into drinking water at about twenty litres a day, and is useless with nothing to filter. `Water_can_be_made_drinkable_from_a_butt_or_a_liner_only_with_a_filter`
- Sanitation is twelve litres a person a day when the taps are dry.

**Food and cooking**
- The shops count only while they are open, reachable and able to take payment; in a power cut that means cash, and two days of notes in the drawer is what most houses have. `This_weeks_shelf_takes_robs_house_to_a_week_and_the_cash_is_why`
- Deliveries count only while the roads are open and there is internet to order on. Same test.
- A gas hob lights with a match; an electric hob needs 2 kW from somewhere; a microwave needs 1.2 kW; a butane stove is outdoors only; a wood stove boils a kettle. `A_wood_stove_warms_the_house_for_as_long_as_the_logs_last_and_boils_a_kettle`
- A shut fridge is safe for four hours and a full freezer for 48; after that the food goes. `Robs_house_with_nothing_done_stands_a_day_in_a_storm`
- Raised beds and an allotment are flows: nothing until the lead time, more in the growing season, a few roots in winter; a full plot is about a third of a family's vegetables, not a larder. `Food_is_grown_after_a_lead_time_and_in_season`

**Warmth**
- Blankets and one closed room are "reduced" warmth and count as surviving for most; for a frail person or a baby they count as lost, and the figure falls to half a day. `Blankets_do_not_count_for_a_frail_person_or_a_baby`
- No warmth is needed in summer or in a heatwave whatever the calendar says. `The_summer_board_differs_from_the_winter_one`
- A wood stove's log store lasts about three weeks; the log merchant keeps it up while the roads are open. `A_wood_stove_warms_the_house_for_as_long_as_the_logs_last_and_boils_a_kettle`

**Health and the people who depend on you**
- A household adds its own devices, medicines and supplies, each on its own rung. A 300 W oxygen concentrator is 7.2 kWh a day: a 3 kWh station runs it a third of a day, a whole-house battery runs it for a day. `Household_dependencies_sit_on_their_rung_and_decide_the_headline`
- A medicine that must stay cold is lost when the fridge is lost. Same test.
- Medicines come from the pharmacy while it dispenses, then from the stock in the house; a rationed pharmacy and a stock stretch each other. `A_medicine_dependency_comes_from_the_pharmacy_while_it_dispenses_and_a_supply_from_the_shops`
- Formula needs boiled water, so an infant's care depends on cooking; pet food is a stock. `Formula_depends_on_boiled_water_and_a_boil_notice_puts_the_tap_behind_the_hob`

**Information and contact**
- Mobile masts last an hour or so on battery; the exchange a few hours more. Phones are a day of charge; a wind-up radio is information for ever; contact is lost on day two of a storm unless there is satellite internet with a source that can give 75 W. `A_shared_stock_counted_in_days_serves_every_need_it_meets_on_the_same_day`
- A digital landline dies with the router; a corded phone is no longer a backup.

**Time and season**
- A cell is the worst day in its stretch, with the number of days it was fully held, so "held for four days then lost" is drawn as such. `Short_horizons_cut_the_board_to_what_was_simulated`
- Winter and summer differ in heating demand, lit hours and the roof's output. `The_summer_board_differs_from_the_winter_one`

**Invariants, for any household FsCheck invents**
- Days Ready never falls when kit is added. A longer horizon never rewrites the days already simulated. With every utility up nothing is lost or rationed. An unknown tick changes nothing. Every cell is the worst of its days and the headline is the first Survive failure. `OutageBoardProperties`

## Open, for Rob

Things a survivalist would raise that the engine does not yet model, in the order I would add them. Each is a provider, a demand rule or a scenario phase; none changes the engine's shape.

1. **Freezer food as a food source**: the thawing freezer is a day or two of meals that should be eaten first. Today it is cold storage only.
2. **Cooking fuel for the gas hob under a gas outage**: MainsGas never fails in any scenario yet. A "gas supply interrupted" scenario (rare but it happened in Falkirk in 2018) would make the hob and boiler fail together.
3. **Heat in a heatwave**: for a frail person the need is a cool room (a fan is 50 W; air conditioning is not a UK fixture). Today Health demand does not rise with heat.
4. **Water for pets** is a flat two litres; it should scale with the animals. Pet food is a stock with no size question.
5. **Stairlifts and lifts**: a block's lift stops with the power, which is mobility for someone on the ninth floor. Today Mobility is the car only.
6. **Fuel in the tank as a phone charger and a warm place**: a petrol car's 12 V socket and cabin heater are a source the engine does not count.
7. **Petrol storage limits**: the law allows 30 litres at home in approved containers; the generator provider assumes twenty.
8. **Sump pumps and private water supplies**: rural houses on boreholes lose water with the power like a high flat does.
9. **Cash as pounds rather than days**: the Cash need is "days of essentials"; a household with a big weekly shop runs through notes faster.
10. **Medicine review dates**: a 28-day supply is the NHS norm; a repeat due in three days is three days of stock. The question should ask "when is your next repeat due".
11. **Psychological resilience and routine** (Finland lists it as a supply): books and games are the only nod.
12. **Evacuation**: the flood scenario is deliberately absent because a flooded house is left, not stood in; a "grab bag" need would be the way to represent it.
