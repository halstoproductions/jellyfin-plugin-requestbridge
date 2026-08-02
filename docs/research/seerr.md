# Research: Seerr as a Request Provider

Milestone 3 deliverable. Read-only study. Nothing was created, modified, or requested in the live instance.

**Specification studied:** `seerr-api.yml` from `seerr-team/seerr`, main branch, OpenAPI 3.0.2, 225 KB.
**Live instance:** `ghcr.io/seerr-team/seerr:latest`, version **3.4.1**, commit `69f73a6`, reachable at `http://localhost:5055`. Confirmed `initialized: true`, `mediaServerType: 2`.

No credentials were used, requested, or handled. Every live call in this document was unauthenticated, and the API surface was read from the published specification rather than from an authenticated session.

---

## 1. What I learned

Seerr is a conventional REST API over Express, versioned under `/api/v1`, with two authentication methods and a coherent request model. Integrating it behind `IRequestProvider` is straightforward.

The single most important finding is a **state resolution mismatch**. The roadmap's state machine has seven states. Seerr can distinguish six of them at best, and cannot distinguish `SEARCHING`, `DOWNLOADING`, or `IMPORTING` from each other at all. It collapses all three into one value, `PROCESSING`. This is not a limitation to work around; it is a fact that Milestone 12 must be rewritten to respect, because `Claude.md` forbids exposing states that cannot be determined.

---

## 2. Authentication

From the specification's `securitySchemes`:

```yaml
cookieAuth:
  type: apiKey
  name: connect.sid
  in: cookie
apiKey:
  type: apiKey
  in: header
  name: X-Api-Key
```

Two mechanisms:

| Method | Mechanism | Fit for RequestBridge |
|---|---|---|
| **API key** | `X-Api-Key` header, key generated in Seerr settings | **Correct choice.** Stateless, no session lifecycle, no credential storage beyond one secret. |
| Cookie | `connect.sid` from `/auth/local` or `/auth/plex` | Wrong for a server-to-server integration. Requires session management and expiry handling. |

RequestBridge should hold a single administrator-configured API key in plugin configuration and use `X-Api-Key` exclusively.

**A consequence worth stating early.** With a single API key, every request reaches Seerr as the same identity. Per-Jellyfin-user attribution does not happen for free. Seerr's `POST /request` accepts a `userId` field, so attribution is possible in principle, but it requires mapping Jellyfin users to Seerr users, which is a design decision for Milestone 4 and beyond, not something to assume.

### Verified live

Unauthenticated endpoints, confirmed 200:

- `GET /api/v1/status` returns `{"version":"3.4.1","commitTag":"69f73a6...","updateAvailable":false,"commitsBehind":0,"restartRequired":false}`
- `GET /api/v1/settings/public` returns instance configuration including `initialized`, `applicationTitle`, `mediaServerType`, `localLogin`, `mediaServerLogin`

Everything else, confirmed 401. Useful for provider health checks: **`/status` is a valid unauthenticated reachability probe**, which lets RequestBridge distinguish "Seerr is down" from "Seerr rejected our key" without a credential.

---

## 3. The API surface that matters

### Search

`GET /search?query=&page=&language=`

Returns a paged, mixed array of `MovieResult`, `TvResult`, and `PersonResult`. Also available: `/search/keyword`, `/search/company`, and an extensive `/discover/*` family (trending, upcoming, by genre, by network, watchlist).

`PersonResult` in a mixed result array means the provider must filter by type before mapping to a RequestBridge item.

### Requesting

`POST /request`, requires the `REQUEST` permission. Body:

```yaml
mediaType: string   # enum: [movie, tv]
mediaId:   number   # the TMDB id
tvdbId:    number   # optional
seasons:   array of number, or "all"
```

Auto-approved when the calling identity has `ADMIN` or `AUTO_APPROVE`. Otherwise the request lands as pending approval.

`GET /request` lists requests, with filters `all, approved, available, pending, processing, unavailable, failed, deleted, completed`. Note this filter vocabulary is richer than the status enum it filters on, which is a hint that some distinctions are derived rather than stored.

Also: `GET /request/count`, `GET /request/{requestId}`, `POST /request/{requestId}/retry`, `POST /request/{requestId}/{status}`.

### Status

Two separate status concepts, which must not be conflated.

**`MediaInfo.status`**, the availability of the media itself:

| Value | Name |
|---|---|
| 1 | `UNKNOWN` |
| 2 | `PENDING` |
| 3 | `PROCESSING` |
| 4 | `PARTIALLY_AVAILABLE` |
| 5 | `AVAILABLE` |
| 6 | `DELETED` |

**`MediaRequest.status`**, the approval state of a request:

| Value | Name |
|---|---|
| 1 | `PENDING APPROVAL` |
| 2 | `APPROVED` |
| 3 | `DECLINED` |

A complete picture requires both. A request can be `APPROVED` while its media is still `PROCESSING`, and media can be `AVAILABLE` with no request at all.

### Media identifiers

`MediaInfo` carries `id` (Seerr's internal id), `tmdbId`, and `tvdbId` (nullable). `POST /request` takes the **TMDB id** as `mediaId`.

**TMDB is the lingua franca.** This matters because Jellyfin stores external ids in `BaseItemDto.ProviderIds`, keyed by provider name, and TMDB is among them for most movie and series libraries. So id reconciliation is possible, with caveats:

- Items lacking a TMDB id in Jellyfin cannot be matched. The provider must handle this as a genuine `UNKNOWN`, not an error.
- TV uses `tvdbId` as an optional supplement on request creation, so series may need both.
- The direction that matters most for the roadmap is the harder one. Milestones 8 to 10 need to go from something the user is looking at to a Seerr request, but a not-yet-requested item does not exist in Jellyfin at all, so there is no `BaseItemDto` and no `ProviderIds` to read. See section 6.

### Error handling

Errors are JSON with a consistent shape. Verified live:

```json
{"message":"cookie 'connect.sid' required",
 "errors":[{"path":"/api/v1/request","message":"cookie 'connect.sid' required"}]}
```

Standard HTTP semantics: 401 unauthenticated, 403 for permission failures, 404 for missing resources. The provider should map these to RequestBridge's error model rather than surfacing Seerr wording, since the error model is above the provider boundary.

### Rate limiting

**The specification declares none.** No `429` response is documented anywhere in the 225 KB spec, and no rate limit headers were observed. Seerr does proxy TMDB, which has its own limits, so upstream throttling may surface indirectly as slow or failing calls rather than as a documented 429.

Practical conclusion: do not build rate limit handling on speculation, but do not poll aggressively either. Treat 429 as possible-but-undocumented and back off if it appears.

---

## 4. Push versus polling

Seerr has a **webhook notification agent**, `GET`/`POST /settings/notifications/webhook`, with `WebhookSettings`:

```yaml
enabled: boolean
types: number            # bitmask of notification types
options:
  webhookUrl: string
  authHeader: string
  jsonPayload: string    # user-defined template
  supportVariables: boolean
```

This is a significant finding for Milestone 12. RequestBridge does **not** have to poll Seerr. The plugin can expose a webhook receiver endpoint, and an administrator points Seerr's webhook agent at it with a shared `authHeader` secret.

Trade-offs, stated honestly:

- **For push:** near-real-time updates, no polling load, `authHeader` gives a simple shared-secret check.
- **Against push:** requires the Jellyfin server to be reachable from Seerr, requires administrator configuration in two places, and the payload is a user-editable template, so RequestBridge cannot assume its shape unless it also writes the template.

A defensible design is push as an optimization over a polling baseline, so the feature degrades rather than breaks when the webhook is not configured. That decision belongs to Milestone 4.

---

## 5. State mapping: the central finding

Roadmap state machine versus what Seerr can actually report:

| RequestBridge state | Determinable from Seerr? | Source |
|---|---|---|
| `UNKNOWN` | Yes | No TMDB id, provider unreachable, or `MediaInfo.status = 1` |
| `AVAILABLE` | Yes | `MediaInfo.status = 5`, or the item already exists in the Jellyfin library |
| `REQUESTABLE` | Yes | Media known to Seerr with no active request |
| `REQUESTED` | Yes | `MediaRequest.status` is 1 or 2 while media is not yet available |
| `SEARCHING` | **No** | collapsed into `MediaInfo.status = 3` |
| `DOWNLOADING` | **No** | collapsed into `MediaInfo.status = 3` |
| `IMPORTING` | **No** | collapsed into `MediaInfo.status = 3` |

There is no `downloadStatus` field anywhere in the specification. I grepped the entire 225 KB document for "download" and found zero occurrences. Seerr's own API does not expose the Sonarr or Radarr download pipeline in any form the spec commits to.

Three states in the roadmap's state machine therefore cannot be distinguished. `Claude.md` is unambiguous about what to do here:

> No fake progress bars.
> Only expose states that can actually be determined.

So the honest state machine, for a Seerr provider, is:

```
UNKNOWN -> REQUESTABLE -> REQUESTED -> PROCESSING -> AVAILABLE
```

Seerr additionally reports two states the roadmap does not model: `PARTIALLY_AVAILABLE` (4), which is meaningful and common for TV series, and `DELETED` (6).

**Recommendation for Milestone 4:** collapse `SEARCHING`, `DOWNLOADING`, and `IMPORTING` into a single `PROCESSING` state in the RequestBridge specification, and add `PARTIALLY_AVAILABLE`. Keep the finer states out of the specification entirely rather than defining states no provider can populate. A future provider that genuinely can distinguish them can extend the model then, with evidence.

This is a specification change, so it belongs to Milestones 4 and 5, and it changes Milestone 12's "Supported States" list. It should not be actioned from this document.

---

## 6. Can RequestBridge integrate here?

Yes. Seerr is a good fit for `IRequestProvider`, and nothing in its API resists the abstraction.

The provider needs: a base URL, an API key, search by title, lookup by TMDB id, create request, and read status. All exist. All are ordinary REST.

**The unresolved problem is not in Seerr, it is the one already raised in `androidtv.md` section 5.** A requestable item is not in the Jellyfin library, so it has no `BaseItemDto`, no `ProviderIds`, and no detail screen. Seerr's search returns TMDB results for exactly such items, which means Seerr can supply the discovery surface the Jellyfin client lacks. That strengthens the "search-driven" resolution option from Milestone 1, but it also means RequestBridge's API must be able to return provider-sourced items that have no Jellyfin identity at all.

That is a real constraint on the Milestone 5 specification: `RequestItem` cannot be defined in terms of a Jellyfin item id. It has to carry external ids, primarily TMDB, and treat the Jellyfin item as optional.

---

## 7. Limitations

1. Three roadmap states are not determinable. Section 5.
2. No rate limiting documented, so behaviour under load is unknown rather than known-safe.
3. A single API key means one identity. Per-user attribution requires a Jellyfin-to-Seerr user mapping that does not exist yet.
4. The webhook payload is a user-editable template, so its shape cannot be assumed.
5. Search returns `PersonResult` mixed with media results, requiring type filtering.
6. `tvdbId` is nullable and separate from `tmdbId`, so TV identity handling is messier than movies.
7. The specification is the main-branch document, while the running instance is 3.4.1. They are consistent on everything checked here, but the spec is not version-pinned to the container.

---

## 8. Open questions for later milestones

1. Push or poll, or push-over-poll? Section 4. Milestone 4.
2. How are Jellyfin users mapped to Seerr users, if at all? Affects whether requests are attributable. Milestone 4.
3. Does `RequestItem` carry TMDB ids as primary identity, with the Jellyfin item optional? Section 6. Milestone 5.
4. Do `PARTIALLY_AVAILABLE` and `DELETED` enter the RequestBridge state model? Milestone 4.
5. Which Seerr permissions must the API key's identity hold, and should RequestBridge verify them at configuration time?

---

## 9. Verification notes

Every endpoint, schema, field, and enum value in this document was read from `seerr-api.yml` at `seerr-team/seerr` main, or observed directly against the running container. Nothing was recalled.

Live calls made, all unauthenticated and all read-only:

| Call | Result |
|---|---|
| `GET /api/v1/status` | 200, version 3.4.1 |
| `GET /api/v1/settings/public` | 200 |
| `GET /api/v1/request` | 401, error shape captured |
| `GET /api/v1/search?query=dune` | 401 |
| `GET /api-docs`, `/seerr-api.yml`, `/api/docs` | redirect to `/login`, so the spec was taken from the repository instead |

No request was created. No setting was changed. No credential was used.
