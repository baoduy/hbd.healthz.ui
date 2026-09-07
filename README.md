# HBD.HealthZ.UI

A self-hosted dashboard that polls the health endpoints of your services and shows their
status and recent history, with every page and API route locked behind Microsoft Entra ID
sign-in.

## ✨ Why use it?

- **One board for many services.** You list the `/healthz` endpoints in configuration; the
  dashboard polls them on a timer and renders liveness plus a short history per endpoint.
  No code, no per-service dashboard.
- **Never anonymously reachable.** A global authorization policy requires an authenticated
  user, so the UI *and* its JSON API are unreachable without an Entra ID sign-in. There is
  no anonymous mode and no API key to leak.
- **History survives restarts when you want it to.** Point it at SQL Server, PostgreSQL,
  MySQL or SQLite and the execution history is persisted; leave the connection string out
  and it runs entirely in memory.
- **Alerts without a monitoring stack.** Webhook notifications fire when an endpoint goes
  unhealthy and again when it recovers, so a Teams/Slack incoming webhook is enough.
- **Configured entirely from the environment.** Every setting below has an environment
  variable form, so the published container image runs unmodified.

## 🚀 Quick Start

The image is published to Docker Hub as **`baoduy2412/healthz-ui:latest`**.

The smallest useful deployment — one monitored endpoint, in-memory history, Entra ID
sign-in:

```bash
docker run -d --name healthz-ui \
  -p 8080:8080 \
  -e ASPNETCORE_HTTP_PORTS=8080 \
  -e AzureAd__TenantId=00000000-0000-0000-0000-000000000000 \
  -e AzureAd__ClientId=11111111-1111-1111-1111-111111111111 \
  -e HealthChecksUI__DbType=Memory \
  -e HealthChecksUI__HealthChecks__0__Name=orders-api \
  -e HealthChecksUI__HealthChecks__0__Uri=https://orders.example.com/healthz \
  baoduy2412/healthz-ui:latest
```

Browse to `http://localhost:8080/`. You are redirected to Microsoft Entra ID, and after
sign-in the dashboard is served at `/`.

Before that first sign-in works, the Entra ID app registration must have:

- a **Web** platform redirect URI of `https://<your-public-host>/signin-oidc` (the
  `AzureAd:CallbackPath` below), and
- **ID tokens (used for implicit and hybrid flows)** enabled — the app signs users in with
  an `id_token` response and does not request access tokens for any downstream API. No
  client secret is needed for this.

The monitored endpoints must return the Health Checks UI JSON format, i.e. they are
ASP.NET Core services mapping their health endpoint with
`AspNetCore.HealthChecks.UI.Client`'s `UIResponseWriter.WriteHealthCheckUIResponse`. A
plain `Healthy`/`Unhealthy` text endpoint will not parse.

`Memory` storage is fine for a first run, but see
**Storage engines** and **Gotchas & limits**
before using it for anything you care about.

## 🧩 Features

### The dashboard and its routes

Route paths are fixed in code, not configurable:

| Path | What it serves |
|---|---|
| `/` | The dashboard UI. Page title: *Application Health Monitoring* |
| `/api` | The JSON the UI reads (current status plus history) |
| `/ui/resources` | Static UI assets |
| `/healthchecks-webhooks` | Webhook metadata endpoint used by the UI |
| `/signin-oidc` | Entra ID sign-in callback (see `AzureAd:CallbackPath`) |
| `/signout-callback-oidc` | Entra ID sign-out callback (see `AzureAd:SignedOutCallbackPath`) |

Every route above except the two OIDC callbacks requires an authenticated user.

### Polling and history

Each configured endpoint is polled every `HealthChecksUI:EvaluationTimeInSeconds` and the
result is written to the configured storage. The `/api` response — and therefore the
dashboard — exposes the most recent history entries per endpoint, set by
`HealthChecksUI:MaximumExecutionHistoriesPerEndpoint`. Default is **10**; any positive
integer is honoured. A non-positive or unparseable value clamps back to `10` with a startup
warning (`HBD.HealthZ.UI/Configs/AddHealthzUICofig.cs`).

### Webhook notifications

Each entry in `HealthChecksUI:Webhooks` is an HTTP `POST` sent when an endpoint transitions
to unhealthy (`Payload`) and again when it recovers (`RestoredPayload`). Repeat
notifications for a still-failing endpoint are suppressed for
`MinimumSecondsBetweenFailureNotifications`.

Three placeholders are substituted into both payload strings, each JavaScript-string
encoded so the result stays valid JSON:

| Placeholder | Replaced with |
|---|---|
| `[[LIVENESS]]` | The `Name` of the health check that changed |
| `[[FAILURE]]` | The name of the failing entry inside that check |
| `[[DESCRIPTIONS]]` | The failure descriptions reported by the endpoint |

A Microsoft Teams incoming webhook, as environment variables:

```bash
-e HealthChecksUI__Webhooks__0__Name=teams \
-e HealthChecksUI__Webhooks__0__Uri=https://outlook.office.com/webhook/... \
-e 'HealthChecksUI__Webhooks__0__Payload={"text":"[[LIVENESS]] is unhealthy: [[DESCRIPTIONS]]"}' \
-e 'HealthChecksUI__Webhooks__0__RestoredPayload={"text":"[[LIVENESS]] has recovered"}'
```

### Storage engines

`HealthChecksUI:DbType` selects where execution history is written. The accepted values are
exactly the members of the `DbTypes` enum (`HBD.HealthZ.UI/Configs/DbTypes.cs`) — the value
is matched by name, case-insensitively.

| `DbType` | Requires | Notes |
|---|---|---|
| `Memory` | nothing | History lives in process memory and is **discarded on every restart**. |
| `SqlServer` | `ConnectionStrings:DbConn` — a SQL Server connection string | Schema is created/migrated on startup. |
| `NpgSql` | `ConnectionStrings:DbConn` — a PostgreSQL connection string | Schema is created/migrated on startup. |
| `MySql` | `ConnectionStrings:DbConn` — a MySQL connection string | Schema is created/migrated on startup. |
| `SqLite` | `ConnectionStrings:DbConn` — e.g. `Data Source=/app/Db/healthz.db` | Put the file on a mounted volume, otherwise it dies with the container. |

Redis is **not** a supported engine, despite appearing in older configuration comments —
no Redis storage package is referenced by this project.

Two situations fall back to in-memory storage instead of failing, each with a startup
warning on the console:

- the value is not one of the five names above (including an in-range number with no
  matching member, such as `3`), or
- a persistent engine is selected but `ConnectionStrings:DbConn` is empty.

Both log at `Warning`, ending with `Health history will not survive a restart.` — grep your
container logs for that line if history keeps disappearing. See **Gotchas & limits** below.

## ⚙️ Configuration reference

Settings are read by the standard ASP.NET Core configuration stack, so any of these work:
`appsettings.json` baked into the image, `appsettings.<Environment>.json`, command-line
arguments, and environment variables.

**Environment variable form:** replace each `:` in the key with a double underscore `__`,
and use a zero-based index for array elements.

| Configuration key | Environment variable |
|---|---|
| `HealthChecksUI:DbType` | `HealthChecksUI__DbType` |
| `ConnectionStrings:DbConn` | `ConnectionStrings__DbConn` |
| `HealthChecksUI:HealthChecks[0]:Uri` | `HealthChecksUI__HealthChecks__0__Uri` |
| `ForwardedHeaders:KnownProxies[0]` | `ForwardedHeaders__KnownProxies__0` |

Defaults below are the value the image actually starts with. Where the shipped
`appsettings.json` overrides the library default, both are given.

### `AzureAd` — Microsoft Entra ID sign-in (required)

The whole section is bound to `MicrosoftIdentityOptions`, so any OpenID Connect option that
type exposes can be set here. The ones that matter:

| Key | Type | Default | Effect |
|---|---|---|---|
| `Instance` | URL | `https://login.microsoftonline.com` | Entra ID cloud endpoint. Change only for a sovereign cloud. |
| `TenantId` | GUID or domain | *(empty — you must set it)* | The directory that owns the app registration. |
| `ClientId` | GUID | *(empty — you must set it)* | Application (client) ID of the registration. |
| `Domain` | string | *(unset)* | Tenant domain name. Only needed by flows that resolve the authority from a domain. |
| `ClientSecret` | string | *(unset)* | **Not required.** The app signs users in only and never calls a downstream API, so no confidential-client credential is used. |
| `CallbackPath` | path | `/signin-oidc` | Where Entra ID posts the sign-in response. Must exactly match a redirect URI on the app registration. |
| `SignedOutCallbackPath` | path | `/signout-callback-oidc` | Where Entra ID returns after a sign-out. |
| `RedirectUri` | absolute URL | *(unset)* | Optional override of the sign-in redirect address. **No longer necessary** — see below. Honoured when present. |
| `PostLogoutRedirectUri` | absolute URL | *(unset)* | Optional override of the post-sign-out address. **No longer necessary.** Honoured when present. |
| `WithSpaAuthCode` | bool | `false` | Returns a SPA auth code alongside sign-in. Not needed by this dashboard. |

**On `RedirectUri` / `PostLogoutRedirectUri`:** these used to have to be pinned to absolute
`https://…` addresses, because behind a TLS-terminating proxy the app saw plain HTTP and
built `http://` redirect addresses that Entra ID rejects. The application now derives both
addresses from the forwarded request instead, provided your proxy is named in the
[`ForwardedHeaders`](#forwardedheaders--running-behind-a-reverse-proxy) section. Existing
deployments that set them keep working unchanged — the configured value still wins — but
new deployments should leave them unset and configure `ForwardedHeaders` instead.

### `ForwardedHeaders` — running behind a reverse proxy

Controls which upstream hops the app will believe when it reads `X-Forwarded-For`,
`X-Forwarded-Proto` and `X-Forwarded-Host`. Trusting your proxy is what lets the app see
that the original request was HTTPS, and therefore build correct `https://` sign-in and
sign-out addresses without pinning them.

| Key | Type | Default | Effect |
|---|---|---|---|
| `KnownProxies` | array of IP addresses, e.g. `["10.20.0.7"]` | `[]` | Individual proxy addresses whose forwarded headers are accepted. Entries are **added to** the framework's built-in loopback trust, never replacing it. |
| `KnownNetworks` | array of CIDR ranges, e.g. `["10.20.0.0/24"]` | `[]` | Address ranges whose forwarded headers are accepted — a Kubernetes pod or service CIDR, for instance. Also additive. |
| `ForwardLimit` | int | `1` | How many forwarded-header entries to process, counting back from the nearest hop. Raise only if you genuinely have that many trusted proxies chained. |

> **Security.** The image ships trusting **nobody but loopback**: with both lists empty, a
> forwarded header from any real ingress is ignored, and the app logs a startup warning
> saying so. You must name your own proxy's address or network for HTTPS sign-in addresses
> to be derived correctly. Only list addresses you control — anything reachable from an
> untrusted network can then spoof `X-Forwarded-Proto` / `X-Forwarded-Host` and steer the
> sign-in addresses the app generates, which is an open-redirect and phishing primitive.
> **Never configure a blanket range such as `0.0.0.0/0` or `::/0`.**

An unparseable address or CIDR here fails the application at startup rather than being
skipped — a typo cannot quietly leave you trusting nothing.

### `HealthChecksUI` — what to poll and where to store it

The section name `HealthChecks-UI` is accepted as a fallback if `HealthChecksUI` is absent.

| Key | Type | Default | Effect |
|---|---|---|---|
| `DbType` | `Memory` \| `SqlServer` \| `NpgSql` \| `MySql` \| `SqLite` | `SqlServer` (from `appsettings.json`) | Storage engine for execution history; matched by name, case-insensitively. An unrecognised value falls back to in-memory with a warning. See **Storage engines** above. |
| `HealthChecks` | array of `{ Name, Uri }` | *(empty)* | The endpoints to poll. `Name` is the label in the UI; `Uri` should be the absolute URL of a Health-Checks-UI-format health endpoint. |
| `Webhooks` | array of `{ Name, Uri, Payload, RestoredPayload }` | *(empty)* | Failure/recovery notifications. See **Webhook notifications** above. |
| `EvaluationTimeInSeconds` | int | `60` (from `appsettings.json`; library default is `10`) | Seconds between polling rounds. |
| `MinimumSecondsBetweenFailureNotifications` | int | `60` (from `appsettings.json`; library default is `600`) | Suppression window for repeat webhook alerts on the same endpoint. |
| `DisableMigrations` | bool | `false` | Skip creating/updating the history schema on startup. Set this if the database user has no DDL rights and you apply the schema yourself. |
| `ApiMaxActiveRequests` | int | `3` | Concurrent requests `/api` will serve. Requests beyond the limit get `429 Too Many Requests` immediately — they are not queued. Must be greater than `0`, or startup throws. |
| `HeaderText` | string | `Health Checks Status` | Heading shown above the status list. |
| `NotifyUnHealthyOneTimeUntilChange` | bool | `false` | When `true`, send one webhook per failure episode rather than repeating every `MinimumSecondsBetweenFailureNotifications`. |
| `MaximumExecutionHistoriesPerEndpoint` | int | `10` | Number of history entries `/api` returns per endpoint. Any positive integer is honoured; a non-positive or unparseable value clamps back to `10` with a startup warning. |

### `ConnectionStrings`

| Key | Type | Default | Effect |
|---|---|---|---|
| `DbConn` | connection string | *(unset)* | Connection string for the engine chosen by `HealthChecksUI:DbType`. Ignored when `DbType` is `Memory`. Leaving it unset with any other `DbType` silently degrades to in-memory storage. |

### `Logging`

Standard ASP.NET Core logging configuration. Logs go to stdout, so `docker logs` /
your container platform's log pipeline is the place to read them.

| Key | Type | Default | Effect |
|---|---|---|---|
| `Logging:LogLevel:Default` | `Trace` \| `Debug` \| `Information` \| `Warning` \| `Error` \| `Critical` \| `None` | `Information` | Minimum level for categories with no more specific rule. |
| `Logging:LogLevel:Microsoft.AspNetCore` | same values | `Warning` | Framework request logging. Set to `Information` to see per-request logs while debugging. |

### `AllowedHosts`

| Key | Type | Default | Effect |
|---|---|---|---|
| `AllowedHosts` | `;`-separated host list, or `*` | `*` | Host header filtering. Set it to your real hostname (e.g. `health.example.com`) to reject requests arriving with any other `Host`. Leaving it as `*` (or empty) logs a startup warning. |

### Hosting settings

Not application settings, but you will need them:

| Environment variable | Effect |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | Port the container listens on. Set it explicitly so the published port mapping is deterministic; the examples here use `8080`. |
| `ASPNETCORE_ENVIRONMENT` | Selects `appsettings.<Environment>.json`. Leave it unset (or `Production`) in deployment — `Development` loads a config file containing a sample tenant and localhost redirect addresses. |

### Worked example — behind a TLS-terminating reverse proxy

The realistic shape: a proxy at `10.20.0.7` terminates TLS for
`https://health.example.com` and forwards plain HTTP to the container, history is kept in
PostgreSQL, and sign-in addresses are derived from the forwarded request rather than pinned.

```bash
docker run -d --name healthz-ui \
  --restart unless-stopped \
  -p 8080:8080 \
  -e ASPNETCORE_HTTP_PORTS=8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  \
  -e AzureAd__Instance=https://login.microsoftonline.com \
  -e AzureAd__TenantId=00000000-0000-0000-0000-000000000000 \
  -e AzureAd__ClientId=11111111-1111-1111-1111-111111111111 \
  -e AzureAd__CallbackPath=/signin-oidc \
  -e AzureAd__SignedOutCallbackPath=/signout-callback-oidc \
  \
  -e ForwardedHeaders__KnownProxies__0=10.20.0.7 \
  -e ForwardedHeaders__ForwardLimit=1 \
  \
  -e AllowedHosts=health.example.com \
  \
  -e HealthChecksUI__DbType=NpgSql \
  -e 'ConnectionStrings__DbConn=Host=pg.internal;Database=healthz;Username=healthz;Password=***' \
  -e HealthChecksUI__EvaluationTimeInSeconds=60 \
  -e HealthChecksUI__HealthChecks__0__Name=orders-api \
  -e HealthChecksUI__HealthChecks__0__Uri=https://orders.example.com/healthz \
  -e HealthChecksUI__HealthChecks__1__Name=billing-api \
  -e HealthChecksUI__HealthChecks__1__Uri=https://billing.example.com/healthz \
  \
  baoduy2412/healthz-ui:latest
```

The proxy must forward the original scheme, and pass the original host through unchanged:

```nginx
location / {
    proxy_pass         http://127.0.0.1:8080;
    proxy_set_header   Host              $host;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
}
```

Entra ID app registration for this deployment: Web platform, redirect URI
`https://health.example.com/signin-oidc`, front-channel logout URL
`https://health.example.com/signout-callback-oidc`, ID tokens enabled.

If sign-in fails with a redirect-URI mismatch showing an `http://` address, the proxy is
not being trusted — check `ForwardedHeaders__KnownProxies` names the address the container
actually sees the proxy connecting from, which in a bridged Docker network is the gateway
address, not the proxy host's LAN address.

## ⚠️ Gotchas & limits

- **A persistent `DbType` with no connection string silently becomes in-memory storage.**
  If `ConnectionStrings:DbConn` is empty, the app logs a warning and starts with in-memory
  storage regardless of `DbType`. The dashboard works, nothing errors — and every restart
  throws the history away. If you configured a database and history keeps resetting, this is
  why: look for `Health history will not survive a restart.` in the startup log.
- **A `DbType` typo does not fail the app either.** An unrecognised engine name — or a bare
  number — resolves to in-memory storage with the same warning. `Redis` is the classic case:
  it appears in older configuration comments but no Redis storage provider is referenced.
- **`SqLite` needs a volume.** With `Data Source=Db/healthz.db` the database file lives
  inside the container's writable layer and disappears with the container. Mount a volume
  and point the connection string at it.
- **Forwarded headers are ignored until you name your proxy.** With `KnownProxies` and
  `KnownNetworks` both empty — the shipped state — only loopback is trusted, so behind a
  real ingress the app cannot tell that the original request was HTTPS and builds `http://`
  sign-in addresses that Entra ID rejects. The startup log warns about exactly this. Fix it
  by naming your proxy, never by trusting everything.
- **`AzureAd:SignedOutCallbackPath` previously did nothing.** The key in `appsettings.json`
  carried a trailing space, so the value never bound and the framework default applied. The
  key is now correct and the configured value takes effect — if you had been compensating
  with a different path in your app registration, re-check that it still matches.
- **There is no sign-out link in the UI.** `Microsoft.Identity.Web.UI` is not referenced, so
  no sign-in/sign-out pages are mapped. Sign-in happens automatically when an unauthenticated
  request arrives; ending a session means clearing the auth cookie or signing out of Entra ID
  elsewhere. `SignedOutCallbackPath` matters only when a sign-out is initiated externally.
- **`Development` turns off the generic error response.** Outside `Development` an unhandled
  failure returns a bare `An unexpected error occurred.`; in `Development` the framework's
  developer exception page is in play instead. The committed `appsettings.Development.json`
  (a specific tenant, client ID and `localhost:7152` redirect addresses) is excluded from
  the published output, so it is not inside the container — but do not set
  `ASPNETCORE_ENVIRONMENT=Development` in a deployment regardless.
- **`AllowedHosts` ships as `*`.** Any `Host` header is accepted. Pin it to your real
  hostname.
- **Monitored endpoints must speak the Health Checks UI JSON format.** A health endpoint
  that returns plain text or a custom JSON shape will show as unhealthy or fail to parse.
- **The dashboard polls; it does not push.** Status is at most `EvaluationTimeInSeconds`
  old, and an outage shorter than one polling interval can be missed entirely.

## 🔒 Security notes

- Sign-in against Microsoft Entra ID is **mandatory and the only route in.** The default
  authorization policy requires an authenticated user, and the dashboard endpoints are
  mapped with `RequireAuthorization()`. There is no anonymous mode, no local account, and no
  API key — anyone who should see the board needs access to the app registration.
- Every response carries browser-protection headers: `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a `Content-Security-Policy`
  that restricts scripts, styles, fonts and connections to the app's own origin and forbids
  framing (`frame-ancestors 'none'`). Kestrel's `Server` response header is suppressed.
  `style-src` carries `'unsafe-inline'` because the HealthChecksUI bundle injects its own
  `<style>` elements at runtime (see the comment above the policy in `Program.cs`). The
  dashboard's vendor logo (a `background-image` pointed at a GitHub-avatars URL, baked into
  the library's own stylesheet) is deliberately left blocked by `img-src 'self'` — no
  third-party host was added to the policy for it.
- Outside `Development`, an unhandled failure returns `500` with the fixed body
  `An unexpected error occurred.` — no stack trace, no exception type. Diagnostics for a
  failure are in the container's logs, not in the HTTP response.
- Treat `ConnectionStrings__DbConn` as a secret: inject it from your platform's secret store
  rather than a compose file or a shell history.

## 🛠️ Building locally

```bash
docker build -f HBD.HealthZ.UI/Dockerfile -t healthz-ui:local .
```

Running from source needs the .NET SDK version pinned in `global.json`:

```bash
dotnet run --project HBD.HealthZ.UI
```

The `https` launch profile serves `https://localhost:7152` with
`ASPNETCORE_ENVIRONMENT=Development`, which loads `appsettings.Development.json` — replace
its `AzureAd` values with your own tenant before signing in.
