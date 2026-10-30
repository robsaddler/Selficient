| 13 | Confirm the three legal placeholders in `src/Selficient.Web/Pages/LegalCopy.cs`: the contact mailbox `hello@selficient.app` (create it, or change the constant), the "made in Blackwood, South Wales" line (add the company name if you want it on the page), and whether the business needs ICO registration (it holds no personal data, so probably not, but the fee is small) | The privacy and terms pages and the structured data all read from that one file | The mailbox answers, and the lines say what you want |
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

## 1a. Git and GitHub

| # | Task | Why | Done when |
|---|---|---|---|
| A | Check `https://github.com/robsaddler/Selficient` holds `main` with every milestone commit | The push went through from the M4 commit onwards (the earlier permission block lifted), so this is a check rather than a task | The latest commit on GitHub matches `git log -1` locally |
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

## 4. Decisions still open (defaults chosen; change if you disagree)

*Appended as they arise.*

- Days Ready baseline assumptions (half a day from kettle water, fridge food and phone torches) and the three dimensions water, food and light. Heating and power are not yet dimensions; they arrive with the power-station tick in a later refinement.
- Benchmark allocation target revised from 10 KB to 50 KB per plan (`docs/benchmarks.md`).
- The solar model's documented assumptions (30 percent of generation used as it is made, a 90 percent daily battery cycle, Ofgem typical consumption by household size, the four tariffs in `TariffBook`, the cost bands in `SolarEstimator`). They reproduce the committed review for your roof; change them in one place if an installer's quote says otherwise.
- The Google roof panel shows the segments as a table rather than over aerial imagery. Imagery would need Google's map script and its origins in the CSP, which the design direction argued against; the figures the estimator needs are all there.
