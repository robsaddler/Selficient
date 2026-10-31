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
| `Database:ConnectionString` | `Database__ConnectionString` | none, required | The SQL Server connection string for the platform database (offers, checks, clicks, runs; later the Data Protection keys and the cache). The host migrates the schema from its embedded scripts at start and refuses to start without a connection. |
| `Database:MigrateOnStart` | `Database__MigrateOnStart` | `true` | Runs the embedded migrations before the site answers. Leave on. |
| `Catalogue:WorkerEnabled` | `Catalogue__WorkerEnabled` | `false` | Runs the nightly verification on the clock. Set true on the vhost. |
| `Catalogue:VerifyAtHour` | `Catalogue__VerifyAtHour` | `2` | 0 to 23, server local time; the hour after which the night's run is due. |
| `Catalogue:WakeEveryMinutes` | `Catalogue__WakeEveryMinutes` | `30` | 1 to 1,440 between looks at the clock. |
| `Catalogue:PageBytes` | `Catalogue__PageBytes` | `524288` | The most of a shop page the checker reads. |
| `Operator:Enabled` | `Operator__Enabled` | `true` | Off, the reviewer at `/admin/catalogue` and its sign-in do not exist. |
| `Operator:SessionHours` | `Operator__SessionHours` | `8` | 1 to 24 before the password is asked for again, however much the reviewer is used. |
| `Operator:IdleMinutes` | `Operator__IdleMinutes` | `30` | 5 to 240 of not using the reviewer before the session ends. |
| `Operator:SecondFactorMinutes` | `Operator__SecondFactorMinutes` | `5` | 1 to 30 to finish typing the code after the password. |
| `Operator:BootstrapUsername` | `Operator__BootstrapUsername` | none | The first account, made at start-up only when no operator exists. |
| `Operator:BootstrapPassword` | `Operator__BootstrapPassword` | none | Its starting password, at least fourteen characters. The account must replace it and enrol an authenticator before it can review anything, so leaving these set is not a second way in. Remove them once you have signed in. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `OTEL_EXPORTER_OTLP_ENDPOINT` | none | A collector's OTLP address (gRPC 4317 or HTTP 4318). Set, the site exports traces, metrics and logs; unset, nothing leaves the box. `OTEL_EXPORTER_OTLP_HEADERS` carries a key where the collector wants one. |

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
