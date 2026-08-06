# RequestBridge HTTP API

Milestone 4 deliverable. The contract between a Jellyfin client and the RequestBridge plugin.

This is a **client-facing** contract. It is language-neutral and must remain readable by someone who has never heard of Seerr. It is deliberately separate from the .NET provider interface in [components.md](components.md), because the two have different consumers and different stability requirements: the provider interface may change whenever providers demand it, while this contract is consumed by shipped client builds that cannot be updated in lockstep.

---

## 1. Conventions

| | |
|---|---|
| Base path | `/RequestBridge` |
| Authentication | Jellyfin's standard scheme. All endpoints require an authenticated user. |
| Authorization | `[Authorize]` only. **Never** `RequiresElevation`, or ordinary users could not use the feature. |
| Serialisation | **PascalCase by default.** camelCase available by requesting `application/json; profile="CamelCase"`. |
| Versioning | No version in the path. The client negotiates through `GET /RequestBridge/Capabilities`. See [capabilities.md](capabilities.md). |

Route naming follows Jellyfin's controller convention, so a `RequestBridgeController` with an explicit `[Route("RequestBridge")]` is served at `/RequestBridge` with no registration code.

Two corrections against earlier drafts of this document, both found by testing the running plugin rather than by reading:

**The controller derives from `ControllerBase`, not `BaseJellyfinApiController`.** `BaseJellyfinApiController` lives in the `Jellyfin.Api` assembly, which is not published to NuGet, so a plugin cannot derive from it. What it supplies must be declared explicitly instead.

**PascalCase is what a client actually receives.** The JSON property names below are written in camelCase for readability, but the wire format is PascalCase unless the caller asks for the camelCase profile. The Jellyfin Kotlin SDK sends `Accept: application/json, application/octet-stream;q=0.9, */*;q=0.8`, which resolves to PascalCase, so the Android client will see `State`, not `state`. Serving both requires declaring all three media types on the controller:

```csharp
[Produces(
    MediaTypeNames.Application.Json,
    JsonDefaults.CamelCaseMediaType,
    JsonDefaults.PascalCaseMediaType)]
```

Declaring only `application/json` silently disables the camelCase profile, which is how this was originally shipped and how it was caught.

---

## 2. Shared models

### ExternalId

```
{
  "source": "Tmdb" | "Tvdb" | "Imdb",
  "value":  "603692"
}
```

### MediaType

```
"Movie" | "Series"
```

### RequestState

```
"Unknown" | "Requestable" | "Requested" | "Processing" | "PartiallyAvailable" | "Available"
```

Defined in [state-machine.md](state-machine.md). Clients treat unrecognised values as `Processing`.

### RequestItem

```
{
  "externalIds":  [ ExternalId ],
  "mediaType":    MediaType,
  "title":        "John Wick: Chapter 4",
  "year":         2023,
  "overview":     "...",              // nullable
  "imageUrl":     "https://...",      // nullable, absolute
  "state":        RequestState,
  "jellyfinItemId": "a1b2..."         // nullable, present only if in the library
}
```

`externalIds` is the identity. `jellyfinItemId` is a convenience and is absent for anything not in the library, which is the common case for a requestable item.

### ProblemResponse

Errors use a single shape:

```
{
  "code":    "ProviderUnreachable",
  "message": "The request provider could not be reached."
}
```

`code` is a stable machine-readable enum. `message` is human-readable, may change, and **must never contain provider-specific wording**, because that would leak the provider through the abstraction.

| `code` | HTTP | Meaning |
|---|---|---|
| `ProviderNotConfigured` | 503 | No provider is configured on this server |
| `ProviderUnreachable` | 502 | The provider did not answer |
| `ProviderRejected` | 502 | The provider answered with a failure |
| `NotSupported` | 400 | The operation is not advertised in capabilities |
| `InvalidRequest` | 400 | Malformed or missing parameters |
| `ItemNotFound` | 404 | No such item at the provider |
| `RateLimited` | 429 | The provider signalled throttling |

Note the distinction between a 404 from `ItemNotFound` and a 404 from the plugin being absent. See section 7.

---

## 3. GET /RequestBridge/Capabilities

The discovery endpoint. The first call any client makes.

**Request:** no parameters.

**200** returns a capability document, defined in [capabilities.md](capabilities.md).

**Errors:** `ProviderNotConfigured` if the plugin is installed but has no provider.

This endpoint must answer without contacting the provider, so that discovery works even when the provider is down. A provider health signal belongs in the capability document, not in the success or failure of this call.

---

## 4. GET /RequestBridge/Search

Find items, including items the Jellyfin library does not have. This is the discovery surface for requestable media, per architecture decision 5.1.

**Query parameters:**

| Name | Required | Notes |
|---|---|---|
| `query` | yes | Free text |
| `mediaType` | no | `Movie` or `Series`. Omitted means both. |
| `limit` | no | Default 20, maximum 50 |

**200:**

```
{ "items": [ RequestItem ] }
```

Results are provider-sourced. Items already in the Jellyfin library are returned with `state: "Available"` and a populated `jellyfinItemId`, so a client can render one merged list without a second lookup.

Person and non-media results are filtered out by the provider. They never reach the client.

**Errors:** `NotSupported` if `canSearch` is false. `ProviderUnreachable`, `RateLimited`.

---

## 5. GET /RequestBridge/Items/{source}/{value}

Current state of one item. Used to refresh after a request, and to poll.

**Path parameters:** `source` is an `ExternalId.source`; `value` is its value.

**Query parameters:** `mediaType`, required, because the same numeric id can exist for both a movie and a series at some sources.

**200:** a single `RequestItem`.

**Errors:** `ItemNotFound`, `ProviderUnreachable`, `RateLimited`.

Polling guidance: clients should poll only while an item is visible and in a non-terminal state, and no faster than once every 10 seconds. No provider currently documents rate limits, which means limits are unknown rather than absent.

---

## 6. POST /RequestBridge/Requests

Ask for an item.

**Body:**

```
{
  "externalId": ExternalId,
  "mediaType":  MediaType,
  "seasons":    [ 1, 2 ]        // optional, Series only, omit for all
}
```

**200:**

```
{
  "accepted": true,
  "item":     RequestItem
}
```

`item` carries the state after the request, normally `Requested`. Returning the item avoids a mandatory follow-up poll.

**Errors:** `NotSupported` if `canRequest` is false, `InvalidRequest` if `seasons` is supplied for a movie, `ItemNotFound`, `ProviderRejected`, `ProviderUnreachable`, `RateLimited`.

**Idempotency:** requesting an item that is already `Requested`, `Processing`, or `Available` is **not** an error. The endpoint returns `accepted: true` with the current state. Clients cannot reliably prevent double submission on a remote control, and a duplicate request is harmless.

Requests are submitted under the server's single configured provider identity, per architecture decision 5.3. There is no requesting-user field, deliberately.

---

## 7. Discovery semantics

This is how a client decides whether RequestBridge exists. It matters because Jellyfin's own plugin list is admin-only, so a normal user cannot ask which plugins are installed.

| Response to `GET /RequestBridge/Capabilities` | Client conclusion |
|---|---|
| **200** | Provider present. Use the capability document. |
| **404** | Plugin not installed. Show nothing. Do not retry this session. |
| **401** | Not authenticated. Retry after authentication. |
| **403** | Installed but this user is not permitted. **Not the same as absent.** Show nothing, do not treat as an error, and do not retry. |
| **503** | Installed, no provider configured. Show nothing. |
| **5xx, timeout** | Unknown. May retry with backoff. |

Conflating 403 with 404 would hide a real permission problem behind an apparent absence, which is the kind of failure nobody ever debugs successfully.

---

## 8. What is deliberately not in this API

- **No provider name or type is exposed for behavioural use.** A display name exists in capabilities for an administrator-facing UI only. A client that branches on it violates the architecture.
- **No approval workflow.** Declined maps to `Requestable`. See [state-machine.md](state-machine.md) section 5.
- **No progress detail.** No percentage, no ETA, no stage.
- **No requesting user.** See architecture decision 5.3, and the debt recorded in architecture section 8.
- **No push or webhook endpoint.** Polling is the baseline. Push arrives as an advertised capability when Milestone 12 justifies it.
