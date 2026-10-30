# Handover: what Rob needs to do

*Started 30 October 2026 and appended as the build proceeds. Everything here is something only you can do, or something you should verify yourself before launch. Each item says why and what "done" looks like.*

## 1. Accounts, keys and names

| # | Task | Why | Done when |
|---|---|---|---|
| 1 | Register `selficient.co.uk`, `selficient.uk` and `selficient.app` | All three were free on 29 Oct; the .com was taken ten months ago | Domains in your registrar; `.app` pointed at the vhost, the other two redirecting to it |
| 2 | Manual UK trade mark search at trademarks.ipo.gov.uk and TMview, then file SELFICIENT in classes 9 and 42 | Automated search was blocked; £170 + £50 | Filing receipt |
| 3 | Rotate the Syncfusion key committed in `VanillaUIBaseApp/.mcp.json` and `AlacrityBaseApp/.mcp.json` and strip it from both repos | Found during the template review; it is in git history | New key issued, old revoked, file removed |
| 4 | Google Cloud project with the Solar API enabled; set `Selficient__Google__SolarApiKey` on the runner and the vhost | F5 stage B (roof segments). Until set, the app uses the compass picker and the fake panel | The solar page shows live roof segments for NP12 1GW |
| 5 | Anthropic API key; set `Selficient__Anthropic__ApiKey` on the runner and the vhost | F7 (photo inventory, plan narrative). Until set, the AI features show the "not available yet" state and the fakes answer in Development | A cupboard photo pre-ticks items on the live site |
| 6 | Decide the monthly free AI allowance per household (default in code: 5) | Cost control; see spec F7 | Value set in `Selficient__Ai__FreeAnalysesPerMonth` or the default accepted |

## 1a. Git and GitHub

| # | Task | Why | Done when |
|---|---|---|---|
| A | Push `main` to `https://github.com/robsaddler/Selficient` (`git push -u origin main`) | You named this remote mid-build; the local `origin` already points at it, but the session's permission guard blocked the push, so commits after M1 are local only | `git status` says the branch is up to date with origin |
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

## 4. Decisions still open (defaults chosen; change if you disagree)

*Appended as they arise.*

- Days Ready baseline assumptions (half a day from kettle water, fridge food and phone torches) and the three dimensions water, food and light. Heating and power are not yet dimensions; they arrive with the power-station tick in a later refinement.
- Benchmark allocation target revised from 10 KB to 50 KB per plan (`docs/benchmarks.md`).
