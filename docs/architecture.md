# RequestBridge Architecture

Milestone 4 deliverable. This document is the architectural authority. Where it and the research documents disagree, this document wins, and the discrepancy is a bug to fix.

Companion documents: [state-machine.md](state-machine.md), [api.md](api.md), [components.md](components.md), [capabilities.md](capabilities.md).

---

## 1. Purpose

RequestBridge lets a Jellyfin client offer "request this" for media the server does not have, without the client knowing anything about the system that fulfils the request.

The client learns only that a Request Provider exists and what it can do. It never learns that Seerr is behind it.

---

## 2. Layering

```
Android TV client
      |   HTTP, Jellyfin auth
      v
RequestBridge HTTP API          (Jellyfin.Plugin.RequestBridge, controller)
      |   IRequestProvider
      v
Provider implementation         (FakeRequestProvider, later SeerrRequestProvider)
      |   provider-specific protocol
      v
Seerr
```

Rules, in force for the life of the project:

1. No layer may be skipped.
2. No Seerr type, name, identifier, enum value, or error string may appear above the provider implementation. The abstractions library and the HTTP API must be readable by someone who has never heard of Seerr.
3. The client must never branch on provider identity. It branches on capabilities. See [capabilities.md](capabilities.md).

---

## 3. Target versions

| | Decision |
|---|---|
| Jellyfin server | **10.11.11**, target framework **`net9.0`** |
| Not targeted | master, 12.0.0 on `net10.0`, because such a plugin loads on no server a real user runs |
| First provider | Seerr, `seerr-team/seerr`, verified against 3.4.1 |

The plugin-relevant server interfaces were diffed between the stable tag and master and are identical, with one exception: `PluginManifest.ImageResourceName` exists only on master and must not be used. Evidence in [research/server.md](research/server.md) section 7.

---

## 4. Repository model

The `RequestBridge` folder is the repository root. Its parent folder is a container only and is not version controlled. Nothing above the repository root may be referenced by code or build scripts.

| Track | Location | Upstream target |
|---|---|---|
| Provider specification | this repo, `src/RequestBridge.Abstractions` | ships inside the plugin initially |
| Jellyfin server plugin | this repo, `src/Jellyfin.Plugin.RequestBridge` | Jellyfin plugin catalog |
| Android TV client changes | separate fork of `jellyfin-androidtv`, checked out beside this repo | upstream PR to jellyfin-androidtv |

This repository never contains Android client source. The Android TV client is a Kotlin and Gradle application with its own upstream project, and a Jellyfin server plugin cannot inject UI into it, so client work is a genuinely separate contribution. Milestones 8, 9, and 10 are executed in the sibling fork and produce their own commits and their own pull request. The same applies to the Android UI tests in Milestone 14.

External repositories cloned for research live in a sibling `_external/` directory outside this repository. They are read-only reference material.

---

## 5. Decisions taken at Milestone 4

Each of these was a genuine branch. Each is now closed. Reopening one is a deliberate act, not a drift.

### 5.1 Requestable items surface through search

**Decision: search-driven.**

A requestable item is by definition absent from the Jellyfin library. It therefore has no `BaseItemDto`, no `ProviderIds`, and no detail screen. `FullDetailsFragment.loadItem` navigates back when the item lookup returns null, so there is no screen to attach a button to.

Seerr's search returns TMDB results for exactly these items, so the provider supplies the discovery surface Jellyfin lacks. Requestable items surface in search results, which is where a user already goes when the library does not have something.

Consequences:

- The RequestBridge API must return provider-sourced items that have no Jellyfin identity at all. This constrains the item model in section 6.
- Milestone 9 becomes "render a request affordance on a provider-sourced search result", not "add a button to the detail screen".
- The detail screen remains untouched for now. An item already in the library is `AVAILABLE`, and `AVAILABLE` needs no affordance.

Rejected: opening the detail screen on a provider-supplied stub. Richer, but it breaks the screen's core assumption that the item exists in the library, and it makes the upstream ask far larger for no gain at this stage.

### 5.2 The state machine collapses to what is knowable

**Decision: five states plus one partial state. `SEARCHING`, `DOWNLOADING`, and `IMPORTING` are removed from the specification.**

Seerr collapses all three into a single `PROCESSING` value, and exposes no download detail anywhere in its API. `Claude.md` is explicit that only determinable states may be exposed and that there are to be no fake progress indicators. Specifying three states no provider can populate would violate that rule and would leave three states no client could ever be tested against.

Full rationale, mapping, and transitions in [state-machine.md](state-machine.md).

A future provider that genuinely distinguishes the finer states may extend the model then, with evidence. Capability advertisement makes that extension non-breaking, because a client already asks which states a provider can emit.

### 5.3 Requests use a single shared identity

**Decision: one administrator-configured API key. Every request reaches the provider as that identity.**

Simplest workable model. No user mapping, no extra configuration, no fallback behaviour to define for Jellyfin users with no provider counterpart.

Accepted costs, stated plainly:

- The provider cannot show which Jellyfin user asked for something.
- Provider-side per-user quotas and approval rules do not apply per person. They apply to the shared identity.
- Adding attribution later is a **breaking change** to `IRequestProvider`, because the request operation would gain a requesting-user concept it does not have today. This is a known, accepted debt, recorded in section 8 rather than hidden.

### 5.4 Status is polled, with push left open

**Decision: polling is the baseline. No push in the specification for now.**

Seerr has a webhook notification agent with a configurable URL and auth header, so push is possible and is the better end state. It is not specified now because:

- Nothing before Milestone 12 needs it.
- The webhook payload is a user-editable template, so its shape cannot be assumed unless RequestBridge also writes that template.
- Push requires the Jellyfin server to be reachable from the provider and requires configuration in two places.

The design must not preclude it. Push, when it arrives, is an optimisation over the polling baseline, advertised as a capability, so a client that does not understand it keeps working. Deciding it now would be speculative.

---

## 6. Identity model

This is the most consequential structural decision, and it follows from 5.1.

**A RequestBridge item is identified by external ids, not by a Jellyfin item id.** A Jellyfin item id is optional and is present only when the item happens to exist in the library.

```
ExternalId  = { Source, Value }        Source in { Tmdb, Tvdb, Imdb }
RequestItem = { ExternalIds[], MediaType, Title, Year, Overview, ImageUrl, State, ... }
```

TMDB is the primary currency, because it is what the provider requests against and what Jellyfin most commonly stores in `ProviderIds`.

Reconciliation caveats, all real:

- A Jellyfin item with no TMDB id cannot be matched. That is a genuine `UNKNOWN`, not an error.
- Series may need both TMDB and TVDB ids.
- A requestable item has no Jellyfin identity at all, which is precisely why the model cannot be keyed on one.

---

## 7. Why this fits Jellyfin

Nothing here requires a server change, an upstream patch, or a new extension point. The plugin is an ordinary consumer of published interfaces:

| Need | Existing Jellyfin mechanism |
|---|---|
| Provider swap without touching callers | `IPluginServiceRegistrator` registers `IRequestProvider` into the server container |
| HTTP endpoints | A `ControllerBase` in the plugin assembly, registered automatically as an ASP.NET Core application part |
| Authentication | `[Authorize]` with the server's existing `CustomAuthentication` scheme |
| Administrator configuration | `BasePlugin<TConfigurationType>` plus `IHasWebPages` |
| Reacting to settings changes | The `ConfigurationChanged` event |

The provider swap at Milestone 11 is a one-line change in one file. The plugin, the controller, the API, and the client are untouched.

---

## 8. Known debts and risks

1. **Attribution is a future breaking change.** See 5.3.
2. **Discovery cannot use the plugin list.** `PluginsController` is admin-only, so a normal user gets 403. Discovery works by calling the capabilities endpoint and treating 404 as absent. 401 and 403 mean present-but-forbidden, which is a distinct state and must not be folded into absence.
3. **No rate limiting is documented by Seerr.** Behaviour under load is unknown rather than known-safe. Do not build for 429 speculatively, but do not poll aggressively either.
4. **The client work is a separate upstream contribution** with its own review, on a codebase whose detail screen is still Java while everything around it has moved to Kotlin.
5. **The OpenAPI specification studied is main-branch** while the verified instance is 3.4.1. Consistent on everything checked, but not version-pinned.

---

## 9. Milestone map

| Milestone | Produces | Depends on |
|---|---|---|
| 5 | `RequestBridge.Abstractions`: interfaces and models, no Jellyfin, no Seerr | this document |
| 6 | Plugin skeleton: loads, configuration page, logging, health endpoint | 5 |
| 7 | `FakeRequestProvider` registered through `IPluginServiceRegistrator` | 6 |
| 8 | Client capability discovery, no UI | 7 |
| 9 | Request affordance on provider-sourced search results | 8 |
| 10 | End-to-end fake request workflow | 9 |
| 11 | `SeerrRequestProvider` replaces the fake, one line changes | 10 |
| 12 | Real status synchronisation, polling baseline | 11 |
