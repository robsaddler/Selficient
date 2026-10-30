# Handover: what Rob needs to do

*Started 30 October 2026 and appended as the build proceeds. Everything here is something only you can do, or something you should verify yourself before launch. Each item says why and what "done" looks like.*

## 1. Accounts, keys and names

| # | Task | Why | Done when |
|---|---|---|---|
| 1 | Register `selficient.co.uk`, `selficient.uk` and `selficient.app` | All three were free on 29 Oct; the .com was taken ten months ago | Domains in your registrar; `.app` pointed at the vhost, the other two redirecting to it |
| 2 | Manual UK trade mark search at trademarks.ipo.gov.uk and TMview, then file SELFICIENT in classes 9 and 42 | Automated search was blocked; £170 + £50 | Filing receipt |
| 3 | Rotate the Syncfusion key committed in `VanillaUIBaseApp/.mcp.json` and `AlacrityBaseApp/.mcp.json` and strip it from both repos | Found during the template review; it is in git history | New key issued, old revoked, file removed |
| 4 | Google Cloud project with the Solar API enabled, the key restricted to the Solar API and the VPS address; set `Google__SolarApiKey` on the vhost (the app reads `Google:SolarApiKey`) | F5 stage B. Until set, the solar page shows no consent panel and the compass picker is the whole answer; in Development with the fakes a stand-in survey answers for every postcode | `/plan/solar` offers "Let an aerial survey measure the roof" and NP12 1GW comes back facing south-west |
| 5 | Anthropic API key with a monthly spend limit; set `Anthropic__ApiKey` on the vhost (the app reads `Anthropic:ApiKey`; the model defaults to `claude-opus-5` and can be changed with `Anthropic__Model`) | F7: the three photo moments and the plan narrative. Until set, the photos page says photo help is not switched on and still offers the household's own key; in Development the fakes answer | A cupboard photo on the live site lists what is there and offers the ticks |
| 6 | Decide the free analyses per household per month (default 5) and the site's daily cap (default 500); set `Ai__FreeAnalysesPerMonth` and `Ai__DailyGlobalCap` if different | Cost control: at roughly 1.5p a photo and 4p a narrative, the defaults cap the site at about £20 a day | Values set or the defaults accepted |
| 13 | Confirm the three legal placeholders in `src/Selficient.Web/Pages/LegalCopy.cs`: the contact mailbox `hello@selficient.app` (create it, or change the constant), the "made in Blackwood, South Wales" line (add the company name if you want it on the page), and whether the business needs ICO registration (it holds no personal data, so probably not, but the fee is small) | The privacy and terms pages and the structured data all read from that one file | The mailbox answers, and the lines say what you want |

## 1a. Git and GitHub

| # | Task | Why | Done when |
|---|---|---|---|
| A | Check `https://github.com/robsaddler/Selficient` holds `main` with every milestone commit through M10 | The push went through from the M4 commit onwards (the earlier permission block lifted), so this is a check rather than a task | The latest commit on GitHub matches `git log -1` locally and the M10 Actions run is green on the build job |
| B | Delete or archive the earlier `Dev-Studios-Ltd/Selficient` repository | It was created before you named the personal one and holds the plan and M0 only | Repository gone or archived |
| C | Optionally allow `git push` in the Claude Code settings for this folder | So later sessions can push without stopping | A `Bash(git push:*)` allow rule in `.claude/settings.local.json` |

## 2. Hosting

| # | Task | Why | Done when |
|---|---|---|---|
| 7 | Create the Plesk vhost `selficient.app` with app pool `selficient.app(domain)(4.0)(pool)`, deploy path `C:\inetpub\vhosts\selficient.app\httpdocs`, .NET 10 hosting bundle | The deploy workflow expects these names exactly | `https://selficient.app/health` answers once deployed |
| 8 | Install a GitHub Actions self-hosted runner on that VPS with labels `self-hosted-selficient` and `windows` | The deploy job is queued until a runner with that label exists | The queued deploy job picks up and goes green |
| 9 | Set the environment variables from `docs/configuration.md` on the runner (machine level) | No secrets live in the repo or workflows | Startup checks pass on the vhost (the app refuses to start with missing required config) |
| 10 | Allow outbound HTTPS from the vhost to api.postcodes.io, api.open-meteo.com, re.jrc.ec.europa.eu, solar.googleapis.com, api.anthropic.com | The adapters call these and nothing else | The plan page shows elevation for a real postcode |
| 11 | Fetch the OS Open Zoomstack basemap into `httpdocs\tiles` (the deploy workflow's guarded last step does this on first run; it takes up to an hour and is allowed to fail) | F5 stage A map | Map tiles render on the plan page |
| 12 | Optional, for running the map locally: set `Map__MbTilesPath` to `D:\Source\Repos\sitewise.app\tiles\os-open-zoomstack.mbtiles` (SiteWise's copy), or let it default to `src\Selficient.Web\tiles\os-open-zoomstack.mbtiles`, which is git-ignored | Without it the map degrades to the plain panel; the tests build their own tiny archives | The plan page draws the street at NP12 1GW |

## 2a. Running the checks yourself

All from the repository root in Windows PowerShell. Each exits non-zero on a miss, so any of them can gate a merge.

| Command | What it proves | Time |
|---|---|---|
| `./build/run-tests.ps1` | 628 tests across Domain, Application, Infrastructure, Web and Architecture, 100% lines and branches on every gated assembly, then `tsc` and ESLint | about 3 minutes |
| `./build/run-e2e.ps1` | Twenty-three browser journeys in a real Chromium at phone and desktop widths with axe on every public page, and a screenshot of every page in both themes into `artifacts/screens` (installs Chromium for Playwright on first run) | about 1 minute |
| `./build/run-load.ps1 20` | The five NBomber scenarios for twenty seconds against the targets in `docs/benchmarks.md`; report in `artifacts/load`. `./build/run-load.ps1 10 0.05` runs the same mix at a twentieth of the rate to cost one request | about 1 minute |
| `npm run check` | Types and lint on the hand-written front end | seconds |

Lighthouse is not scripted because its scores are simulated and it needs a running site: `dotnet run --project src/Selficient.Web -c Release`, then Chrome DevTools, Lighthouse, mobile, against `https://localhost:7310/`. The numbers from 30 Oct are in `docs/benchmarks.md`. Stop the running site before building again; it holds the exe.

GitHub Actions: both workflows now run the gate and the E2E journeys on `ubuntu-latest`. Until M10 a workflow-level concurrency group cancelled every build while the M4 deploy sat queued waiting for a runner; the group now applies to the deploy job alone, so builds complete and only the deploy waits. The M10 push is the first full CI build since M4: check the Actions tab for the run, and if the Linux build fails on SkiaSharp's native library or Playwright's system packages, say so and it is a one-line fix in the workflow.

## 3. Before launch: your own testing

Run these on the live site, in this order. Each should take a few minutes.

1. **Your house.** Postcode NP12 1GW, 4 people, 2 children, business from home, detached, 3 storeys, gas boiler, electric hob, car, garden, decking, tall tree. The plan should match `plans/household-resilience-plan.md` tier by tier, with elevation about 232 m and Wales as the nation.
2. **A flat.** 1 person, flat, electric heating. No solar actions, scaled-down water, a heating fallback.
3. **A flood-zone postcode** (any Severn-side town works) with flood risk set to High. Flood actions appear at the top of this week.
4. **Ticks.** Tick the bottled water and the headtorches and watch Days Ready move; reload; the ticks are still there; open a second tab and tick something; the first tab updates.
5. **Export and import.** Export the plan as a file, start again, import it. Everything is back.
6. **Offline.** Install to the home screen on your phone, open the plan, switch to flight mode, reload. The plan still shows.
7. **Phone width.** Every page at 390 px wide: nothing scrolls sideways, every tap target is reachable.
8. **The error pages.** Visit `/nothing-here` and `/error/429`. Whole sentences, no stack traces.
9. **Headers.** `curl -I https://selficient.app/` shows the CSP, HSTS and the rest; no `Server` or `X-Powered-By`.
10. **Rate limits.** Hammer `/api/geo/postcode` from one IP for a minute; it should start answering 429 with the polite page.
11. **The map.** On the plan page the street draws under "Where your house stands" with the OS attribution. Tap your roof: the pin moves and "Pin saved in this browser." appears. Reload: the pin is still there. "Back to the postcode" returns it. Switch to dark and the map follows. If the basemap is still downloading on the server, the panel says the map is not available yet and nothing spins.
12. **The solar estimator.** From the plan, follow "Estimate what solar and a battery would make". NP12 1GW, south-west, 35°, 13 panels, 13.5 kWh, whole-home, 3 storeys, 4 people, 5,000 kWh: the saving should land between £1,250 and £1,650 a year with a 9 to 15 year payback and Green Homes Wales listed. Tick the consent box and press "Measure the roof": with the Google key set, the real roof segments appear and the compass moves to south-west.
13. **Photos and the narrative.** On `/plan/photos`, photograph your kit cupboard: the items are listed with counts and "Tick them on my plan" moves Days Ready on the plan page. Photograph the boiler plate and the consumer unit for their sentences. On the plan page, "Explain my plan in plain words" produces three to five paragraphs under a disclosure. Then do the same with your own key under "Advanced" and watch the network panel: the photo goes to api.anthropic.com from the browser and never to the site. After five analyses in a month on our key, the page says so and offers the key route.
14. **Search and sharing.** Paste `https://selficient.app/` into a WhatsApp or Slack message and check the card shows the share image and the question. Fetch `https://selficient.app/sitemap.xml` and `robots.txt`. Search Console, if you set it up, should report the FAQ structured data on the home page and no indexed `/plan` pages.
15. **Moving devices.** On a phone that has never seen the site, open `/plan`: the empty state offers "Import a file" as well as "Start your plan". Import the file you exported in step 5 and the plan rebuilds with its ticks.
16. **Both themes on a phone.** Switch to dark with the toggle in the header: the Days Ready figure is the bright lantern, the green "Start your plan" button has a dark label, and the footer links read clearly. In light, the figure and the counted numbers are a deep amber. The "Install the app" button, where the browser offers it, is in the footer.
17. **Compression.** `curl -sI -H "Accept-Encoding: br" https://selficient.app/ | findstr -i content-encoding` shows `br`. If IIS dynamic compression is on for the vhost, turn it off for this site so there is one encoder.
18. **The runner.** After the first green deploy, `https://selficient.app/health` answers `ok` and the Actions run shows the basemap step either fetching the archive or warning and carrying on.
19. **The board.** On your plan, the hero says 1 day for "A storm takes the power out" with nothing ticked. Tap "The water stops": the heading, the figure and the lede change without a reload. Tap "Power banks" then "Summer": the sheet redraws for each and the closing sentence names both. Tick the bottled water, the tins and the cash on the plan: the storm figure rises and the actions that would turn cells say so under their price ("Turns Light at a week"). Open a row: the reason is a whole sentence. On a phone each row keeps its cells on one line under the horizon labels.
21. **The look.** The landing page opens on the night: the figure glows and counts up, the four bars under it draw in turn, and the kitchen table sits to the right with the lantern as the only light. The figure and the bars are the engine's for a typical house, so if the engine's assumptions change the landing page follows. On the plan, the panel on the left is the same night; tick the tins and the food bar grows as the figure moves and the lantern pulses once. Every row on the board and every source pill has a small drawing before its name. Switch to light: the night stays night, the sheet goes to paper, and the larder and the hill redraw in slate. With "reduce motion" on in the system settings, nothing moves and everything is already in place.
20. **What you rely on.** Under the board, add "Oxygen concentrator", a device, 300 watts for 24 hours, and press "Add it to the board". A new row appears on the Survive rung, Lost from day one, and the figure drops to 0.5 days. Reload: the row is still there. Export the plan: the file carries `"selficient": 2` and the dependency; import it on another device and the row comes back. Remove it with its button and the figure returns. Try a thirteenth: the page says twelve is the most it will carry.

## 4. Decisions still open (defaults chosen; change if you disagree)

*Appended as they arise.*

- Days Ready baseline assumptions (half a day from kettle water, fridge food and phone torches) and the three dimensions water, food and light. Heating and power are not yet dimensions; they arrive with the power-station tick in a later refinement.
- Benchmark allocation target revised from 10 KB to 50 KB per plan (`docs/benchmarks.md`).
- The solar model's documented assumptions (30 percent of generation used as it is made, a 90 percent daily battery cycle, Ofgem typical consumption by household size, the four tariffs in `TariffBook`, the cost bands in `SolarEstimator`). They reproduce the committed review for your roof; change them in one place if an installer's quote says otherwise.
- The Google roof panel shows the segments as a table rather than over aerial imagery. Imagery would need Google's map script and its origins in the CSP, which the design direction argued against; the figures the estimator needs are all there.
- Lantern is a fill and focus colour only; lantern as text is a new token, `--c-lantern-ink`, deep amber in daylight and the lantern itself in the dark, because the bright value fails the contrast ratio on the light ground. The dark-theme pine is lighter than the design direction's hex and the primary button's label is dark on it in the dark. Both were found by axe and Lighthouse, not by eye; if you prefer different values, `tokens.css` holds them in one place and the E2E suite will tell you if they fail.
- The fonts are Latin-1 WOFF2 subsets with the variable axes narrowed to what the stylesheet uses (`build/subset-fonts.mjs`), a quarter of the weight on the wire. A character outside Latin-1 and the common punctuation falls back to the system face; the list in the script says exactly what is covered.
- The install button moved from the header to the footer so that its appearing never pushes the page down (it was the only layout shift Lighthouse found). If you want it more prominent, the home page hero can carry a second one.
- Response compression is on over HTTPS (Brotli, then gzip). The reasoning for why the compression side channel does not apply here is in `docs/threat-models/launch.md`, case 4; it should be revisited if a page ever carries a secret next to reflected input.
- HSTS is declared with `preload` but the domain is not submitted to the preload list; submit it at hstspreload.org once the certificate and the redirects from the other two domains are proven, because it cannot easily be undone.
- A web application firewall or bot management is not in place and not needed at launch: there is no account, no payment and no server-side data. Revisit when accounts arrive.
- **Days Ready, second definition (your option A, 30 Oct):** the first day anything on the Survive rung is rationed or lost in the scenario shown. Met-by-a-lesser-means (blankets, candles) counts as surviving; for a household with a frail person or an infant, blankets count as lost. Since M13 the plan page's hero is the board's figure for the chosen scenario; the v1 calculator is no longer shown anywhere and can be removed when nothing else references it.
- **The board's what-if sources** are the providers with a placement the house can take (`Providers.cs`): power banks, a 1 kWh and a 3 kWh power station, rooftop solar with a battery, a petrol generator, and an electric car's output where the plan says the car has one. Adding a source to the list is a provider with a placement, not a change to the page.
- **The engine's assumptions are data with sources** in `src/Selficient.Domain/Resilience/Providers.cs` and `Scenarios.cs`: mast batteries, exchange batteries, shop and forecourt behaviour in a cut, appliance energy, store sizes, yields. Each line says where the figure came from. If you know better for South Wales (your DNO's storm record, your water company), change the figure and the tests will tell you what moved.
- **Open edge cases** a survivalist would raise are listed in `docs/design/edge-cases.md`, in the order I would add them. Strike out the ones you do not want and add any I have missed; each is a provider, a demand rule or a scenario phase, not a change to the engine.
