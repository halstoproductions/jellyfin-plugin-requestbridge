# RequestBridge State Machine

Milestone 4 deliverable. Defines the only states RequestBridge exposes, what makes each determinable, and how a provider maps onto them.

---

## 1. The rule this document obeys

From `Claude.md`:

> No fake progress bars.
> Only expose states that can actually be determined.

Every state below exists because a provider can prove it from an observable condition. A state that cannot be proven is not in this specification.

---

## 2. States

| State | Meaning | Determined by |
|---|---|---|
| `Unknown` | Nothing can be said about this item | No usable external id, provider unreachable, provider error, or provider reports no information |
| `Requestable` | The provider knows this item and it can be requested | Provider knows the item and reports no active request |
| `Requested` | A request exists and has not started being fulfilled | Provider reports a request that is pending approval, or approved but not yet in progress |
| `Processing` | Fulfilment is under way | Provider reports work in progress, without committing to which stage |
| `PartiallyAvailable` | Some of the item exists, more is coming | Provider reports partial availability. Applies to series. |
| `Available` | The item is in the Jellyfin library | Jellyfin has it, or the provider reports full availability |

Six states. No others are valid.

---

## 3. Transitions

```
                 +---------------------------------------+
                 |                                       |
                 v                                       |
  Unknown --> Requestable --> Requested --> Processing ---+--> Available
                 ^                              |              ^
                 |                              v              |
                 |                    PartiallyAvailable ------+
                 |                              |
                 +------------------------------+
```

Legal transitions:

| From | To | Cause |
|---|---|---|
| `Unknown` | any | Information became available |
| any | `Unknown` | Provider became unreachable or lost the item |
| `Requestable` | `Requested` | A request was accepted |
| `Requested` | `Processing` | Fulfilment began |
| `Requested` | `Requestable` | Request declined or deleted |
| `Processing` | `PartiallyAvailable` | Part of a series arrived |
| `Processing` | `Available` | Fulfilment completed |
| `PartiallyAvailable` | `Available` | The remainder arrived |
| `PartiallyAvailable` | `Processing` | More work began |
| `Available` | `Requestable` | The item was removed |

**No state is terminal.** `Available` can regress if media is deleted. Clients must not cache a state as final.

---

## 4. What was removed, and why

The original roadmap machine was:

```
UNKNOWN -> AVAILABLE -> REQUESTABLE -> REQUESTED -> SEARCHING -> DOWNLOADING -> IMPORTING -> AVAILABLE
```

`SEARCHING`, `DOWNLOADING`, and `IMPORTING` are **removed**.

Evidence, from [research/seerr.md](research/seerr.md):

- Seerr's `MediaInfo.status` has exactly six values: `1 UNKNOWN`, `2 PENDING`, `3 PROCESSING`, `4 PARTIALLY_AVAILABLE`, `5 AVAILABLE`, `6 DELETED`.
- Searching, downloading, and importing are all `3 PROCESSING`. Seerr does not distinguish them.
- There is no `downloadStatus` field anywhere in the 225 KB OpenAPI specification. The string "download" does not appear in it at all.

Keeping the three states would mean specifying states no provider can populate, no client can render truthfully, and no test can exercise. That is precisely the fake-progress failure mode the project set out to avoid.

`PartiallyAvailable` was **added**, because Seerr reports it, it is common for series, and it is genuinely useful to a viewer.

### If a future provider can do better

A provider that truly distinguishes the finer stages may extend this model. The extension is non-breaking because a client already asks which states a provider can emit, via `supportedStates` in [capabilities.md](capabilities.md). A client that does not understand a new state treats it as `Processing`.

The rule for adding a state is the same rule as above: demonstrate that a provider can determine it. Not that a provider might.

---

## 5. Provider mapping: Seerr

Requires both of Seerr's status concepts. `MediaInfo.status` describes the media; `MediaRequest.status` describes the approval of a request. Neither alone is sufficient.

| Seerr condition | RequestBridge state |
|---|---|
| Item exists in the Jellyfin library | `Available` |
| No usable TMDB id, or Seerr unreachable, or Seerr error | `Unknown` |
| `MediaInfo.status = 1 UNKNOWN` | `Unknown` |
| Media known, no request present | `Requestable` |
| `MediaInfo.status = 6 DELETED` | `Requestable` |
| `MediaRequest.status = 1 PENDING APPROVAL` | `Requested` |
| `MediaRequest.status = 3 DECLINED` | `Requestable` |
| `MediaRequest.status = 2 APPROVED` and `MediaInfo.status = 2 PENDING` | `Requested` |
| `MediaInfo.status = 3 PROCESSING` | `Processing` |
| `MediaInfo.status = 4 PARTIALLY_AVAILABLE` | `PartiallyAvailable` |
| `MediaInfo.status = 5 AVAILABLE` | `Available` |

Notes:

- A declined request maps to `Requestable`, not to a failure state. From the viewer's position the item can be asked for again. Surfacing "declined" would leak the provider's approval workflow through an abstraction that deliberately does not model approval.
- `DELETED` maps to `Requestable` for the same reason: the item can be asked for again.
- Jellyfin library presence outranks everything. If the server has the file, the item is `Available` regardless of what the provider thinks.

---

## 6. Provider mapping: FakeRequestProvider

Milestone 7. Deliberately trivial, so that the architecture is proven rather than the provider.

| Situation | State |
|---|---|
| Any item, before a request | `Requestable` |
| Any item, after a request in this process lifetime | `Requested` |

No persistence, no timers, no simulated progression. A fake that pretends to advance through `Processing` would be a fake progress bar wearing a different hat, and would test nothing that matters.

---

## 7. Client rules

1. Treat any unrecognised state as `Processing`. Never as `Available`, and never as an error.
2. Never present a state the provider did not report. There is no interpolation and no optimistic advancement.
3. Never render progress detail. There is no percentage, no ETA, and no stage breakdown, because no provider supplies one.
4. Do not cache a state as final. See section 3.
5. `Unknown` means show nothing. It is not an error to display; it is an absence of information.
