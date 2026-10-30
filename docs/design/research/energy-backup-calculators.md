# How consumer backup products explain "how long will this keep my things running"

*Researcher's report, 30 October 2026. Tesla, the Enphase estimator and Jackery's calculator blocked automated reading and are described from owner guides and forums; everything else is from the vendor page.*

## Product by product

**Tesla Powerwall.** Marketing is binary ("whole home" or "partial home" backup) with appliance lists and no numbers. The app does the work: a live four-node flow diagram, a battery percentage, a Backup Reserve slider, Storm Watch (Met Office warnings in the UK) and "Back-up Time Remaining" in hours derived from recent average consumption, which owners find opaque. Good: the reserve slider makes a trade-off tangible; one headline number. Bad: the hours are a black box.

**Enphase IQ PowerPack estimator** (https://enphase.com/homeowners/portable-energy). The best appliance picker found: tabbed scenarios, clickable appliance cards each showing watts, a running count, "Run Time (approx.)" in hours, and explicit overload states ("AC overload detected. Reduce appliances to proceed"). The battery pages avoid hours and sell load control. Good: overload feedback teaches watts versus watt-hours without saying so. Bad: no duration input.

**EcoFlow** (https://www.ecoflow.com/ca/calculators/delta-pro-ultra). Icon grid in four categories, each preset carrying watts and hours per day (lights 10 W / 16 h, router 10 W / 24 h, fridge 200 W / 24 h, kettle 1,500 W / 15 min). Output is runtime and a recommended battery count. Their blog is honest: runtime = usable kWh ÷ daily kWh × 24, "a worksheet result, not a runtime promise", with tiered loads (fridge, medical, router first). Good: watts × hours a day is the right model.

**Bluetti** (https://www.bluettipower.com/pages/portable-power-station). Model dropdown labelled "2,600 W | 2,073.6 Wh", one watts field, output in hours, hard stop "Exceeds rated output". Formula published: capacity × 90% depth × 90% inverter ÷ (load + 10 W self-draw). Tables mix units usefully: phones as "33 charges", fridge "14 hours". Bad: one appliance at a time.

**Jackery, Anker SOLIX.** Wh × efficiency ÷ W with 85% default; Anker's UK guide gives the F3800 (3,840 Wh, 6,000 W) as fridge 14.9 h, TV 20.1 h, router 40.1 h, microwave 4.3 h.

**Generac** (https://www.generac.com/home-standby-generators/find-the-right-generator/). Five-step wizard: floor area, heating fuel, EV, "Whole Home" or "Select Circuits", then "Basics Only (lights, refrigerator, medical devices)" or "Basics Plus (garage door, washer, cooking)". Output is a kW size. Good: the Basics / Basics Plus tiering is a clean mental model. Bad: no duration.

**Ford F-150 Lightning Intelligent Backup Power.** 131 kWh at 9.6 kW, "up to three days" at 30 kWh a day, "up to 10 days" with rationing or solar; the app lets you set the reserve as miles you must keep. Good: days, not hours, and a reserve in the user's own unit.

**Nissan Leaf / Indra V2H / Wallbox Quasar 2.** Indra: 6 kW CHAdeMO, trial-only in 2026. Wallbox: "up to 12 kW", backup "up to three days" footnoted as 25 kWh a day and a 99.8 kWh battery, stops at 20%, Kia EV9 only, UK launch 2026, about £6,100. Both are tariff stories first.

**Octopus Energy.** Five-step solar wizard with an aerial roof overlay, four illustrated time-of-day flow diagrams (morning, midday, evening, night), savings and payback; the app shows half-hourly bars with chatty copy. Power Pack is vehicle-to-grid, not to home. Good: the four time-of-day panels are the best "what happens over a day" explainer found.

**GivEnergy All in One.** 13.5 kWh, 6 kW continuous, whole-home backup via a gateway; the app rated above Tesla by UK installers; no hours or appliance content.

**myenergi libbi.** 5 to 20 kWh at 3.68 or 5 kW; backup is one dedicated circuit. The one memorable UK line: "prioritise essentials so your milk stays cold, and the Wi-Fi keeps the kids happy."

**Sunsave, Energy Saving Trust.** Postcode and bill in, "Save £36 a month" out, with sliders for an EV or heat pump. EST's progressive form answers early with area defaults and refines later: worth borrowing.

## Data

**UK appliance draws** (Centre for Sustainable Energy table, July 2026; DECC Household Electricity Survey for daily energy): immersion 3,000 W; kettle 3,000 W; oven 2,100 W; washing machine 2,100 W (0.35 to 0.51 kWh an eco cycle); induction rings 1,700 to 3,000 W; microwave 1,000 W; fridge-freezer 300 W nameplate but 1.17 kWh a day measured; fridge 0.44 kWh a day; upright freezer 0.90, chest freezer 0.99; electric blanket 60 to 150 W; TV 80 to 120 W; laptop 50 W; router 10 W. Gas boiler 35 to 123 W firing, under 3 W standby, plus a 2 to 60 W pump (manufacturers' manuals). Heat pump 1 to 3 kW running, 16 to 25 kWh a day in a three-bed semi from December to February, 6,200 kWh a year. CPAP 20 to 40 W without humidifier, 60 to 90 with; 160 to 720 Wh a night. A phone charge 15 to 18 Wh. LED bulb 5 to 10 W. EV charging 7 kW wallbox or 2.3 kW three-pin.

**Capacities.** Power banks: 10,000 mAh about 37 Wh, 20,000 about 74 Wh, 27,000 about 100 Wh (the airline limit). Power stations: small 256 to 288 Wh / 300 W; mid 1,024 to 1,070 Wh / 1,500 to 1,800 W; large 2,042 to 2,074 Wh / 2,600 to 3,000 W; home grade 3,600 to 4,096 Wh / 3,600 to 4,000 W, and 3,840 Wh / 6,000 W. Home batteries: Enphase 5P 5 kWh / 3.84 kW; libbi 5 to 20 kWh / 3.68 to 5 kW; GivEnergy 13.5 kWh / 6 kW; Powerwall 3 13.5 kWh / up to 11 kW. EVs: Leaf 52/75 kWh, ID.3 58/79, Model Y 60 to 79, EV3 58 to 81. UK discharge in 2026: vehicle-to-load 2.2 to 3.6 kW through a three-pin adapter on Hyundai, Kia, MG, BYD and Volvo; Indra vehicle-to-home 6 kW CHAdeMO trial only; Wallbox Quasar 2 up to 12 kW on the Kia EV9, launching. Petrol generators: Honda EU22i 1.8 kW rated, 3.5 h per 3.6 litre tank at full load; open-frame 2.5 to 3.2 kW; 5 to 8 kW site units.

**Food** (Food Standards Agency via gov.uk): a fridge "should stay cold for up to 4 hours"; once at 8 °C or above, high-risk food must be eaten within 4 hours or binned; a freezer "up to 48 hours if full, 24 hours if half full"; keep doors shut.

## Three patterns worth borrowing

**The stacked bar with a horizon marker.** Pick a duration and a source; one horizontal bar is usable energy; appliances appear as stacked segments sized by watts × hours in that horizon in priority order; the first that does not fit is hatched and named ("The kettle does not fit at 48 hours"); the FSA 4 h / 24 h / 48 h marks sit on the time axis so "fridge safe" reads directly.

**The appliance grid with a live two-number header.** Cards with icon, name, typical watts and an hours-a-day stepper; the header always shows "Peak 2.4 kW of 3.6 kW" and "Lasts 31 hours"; a card goes amber when it alone exceeds the source's ceiling.

**Basics / Basics Plus / Everything with a reserve slider.** Three preset bundles written for a UK house; each shows days for the chosen source as a wide figure; a reserve slider on car sources ("keep 60 miles to drive") visibly shortens the bar; copy in outcomes, not watts.

## Recommended data model

Appliance {id, name, category, typicalWatts, surgeWatts, hoursPerDayDefault, dutyCycle, priority, isEssential, note}; Source {kind, nominalWh, usableFraction (0.85 station, 0.9 LFP home, 0.8 EV with reserve), maxContinuousW, maxSurgeW, inverterEfficiency, selfDrawW, refuelable, reserveSetting}; Horizon {hours, label, foodMarkers}; a deterministic function in the rules engine returning fitsPower, fitsEnergy, hoursUntilEmpty, firstDropped and the stack segments; every seed value stored with its source URL.
