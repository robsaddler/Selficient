# Configuration

Everything the application reads, with its default and where it matters. There are no `appsettings` files: every default lives in the option class beside its validation, and anything else arrives as an environment variable with `__` standing in for `:` (so `Anthropic:ApiKey` is `Anthropic__ApiKey`), machine-level on the vhost and the runner. Nothing secret lives in the repository or a workflow. Every option class validates at startup and the application refuses to start on a bad value, so a typo shows as a failed health check rather than a quiet default.

## Required for launch

| Setting | Environment variable | Default | What it does |
|---|---|---|---|
| `Upstreams:UseFakes` | `Upstreams__UseFakes` | `false` | Deterministic stand-ins for every upstream. Refused in Production. |

Nothing else is required: with no keys set the site runs with the map on its fallback panel, no aerial roof survey and photo help switched off, and says so on each page.

## Keys

| Setting | Environment variable | Default | What it does |
|---|---|---|---|
| `Google:SolarApiKey` | `Google__SolarApiKey` | none | Enables the aerial roof survey consent panel on `/plan/solar`. Sent as a header, never in a URL. |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | none | Enables photo help and the plan narrative on our key. Must look like a key (`sk-ant-…`). |
| `Anthropic:Model` | `Anthropic__Model` | `claude-opus-5` | The model the site uses. |
| `Anthropic:BaseUrl` | `Anthropic__BaseUrl` | `https://api.anthropic.com/` | HTTPS, or loopback for a stub. |
| `Anthropic:TimeoutSeconds` | `Anthropic__TimeoutSeconds` | `60` | 5 to 180. |
| `Affiliate:AmazonTag` | `Affiliate__AmazonTag` | none | Appended once to Amazon links on an exact host match. |

## Limits and costs

| Setting | Environment variable | Default | Range |
|---|---|---|---|
| `Ai:FreeAnalysesPerMonth` | `Ai__FreeAnalysesPerMonth` | `5` | 0 to 1,000 per household per month |
| `Ai:DailyGlobalCap` | `Ai__DailyGlobalCap` | `500` | 1 to 1,000,000 per day for the site |
| `RateLimits:PlanPerMinute` | `RateLimits__PlanPerMinute` | `30` | 1 to 10,000 per client address |
| `RateLimits:BoardPerMinute` | `RateLimits__BoardPerMinute` | `120` | 1 to 10,000 per client address; the board fragment, one redraw per tick |
| `RateLimits:ApiPerMinute` | `RateLimits__ApiPerMinute` | `60` | 1 to 10,000 |
| `RateLimits:AiPerMinute` | `RateLimits__AiPerMinute` | `6` | 1 to 1,000, posts only |
| `RateLimits:TilesPerMinute` | `RateLimits__TilesPerMinute` | `600` | 1 to 100,000 |

## Upstreams and the map

| Setting | Environment variable | Default | What it does |
|---|---|---|---|
| `Upstreams:PostcodesBaseUrl` | `Upstreams__PostcodesBaseUrl` | `https://api.postcodes.io/` | Geocoding |
| `Upstreams:ElevationBaseUrl` | `Upstreams__ElevationBaseUrl` | `https://api.open-meteo.com/` | Elevation |
| `Upstreams:PvgisBaseUrl` | `Upstreams__PvgisBaseUrl` | `https://re.jrc.ec.europa.eu/` | Solar yield |
| `Upstreams:GoogleSolarBaseUrl` | `Upstreams__GoogleSolarBaseUrl` | `https://solar.googleapis.com/` | Roof survey |
| `Upstreams:TimeoutSeconds` | `Upstreams__TimeoutSeconds` | `5` | 1 to 30, every upstream |
| `Map:MbTilesPath` | `Map__MbTilesPath` | `tiles/os-open-zoomstack.mbtiles` | Relative to the content root. Absent is allowed: the map falls back and the status is re-probed. |
| `Map:RetryAfterSeconds` | `Map__RetryAfterSeconds` | `60` | 5 to 3,600 between probes while the archive is absent |

## Hosting

| Setting | Environment variable | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | same | `Production` on the vhost. `Development` (the `dotnet run` profile) turns on the developer exception page and the dev certificate on port 7310; set `Upstreams__UseFakes=true` alongside it to run without the network. The tests use `Testing`. |
| `ASPNETCORE_URLS` | same | Not needed under IIS in-process hosting. |

Logs go to the console and to `logs/selficient-.log` under the content root, rolled daily, fourteen kept. The deploy's robocopy excludes `logs`, `App_Data` and `tiles` so they survive a release.
