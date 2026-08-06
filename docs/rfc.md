# RFC: A provider-agnostic media request API for Jellyfin

**Status:** draft, for community discussion
**Targets:** `jellyfin` plugin ecosystem, and `jellyfin-androidtv`

---

## Summary

Jellyfin has no way for a client to offer "ask for this" when a user searches for something the server does not have. Tools that fill this gap exist, but every client integration with them is bespoke and hardcodes one product.

This proposes a small, provider-agnostic contract so that a Jellyfin client can offer a request action without knowing what fulfils it, and a reference implementation of that contract as a plugin.

The Android TV client change is deliberately tiny: it asks the server whether a request provider exists, and if one does, shows requestable items in search results.

## Motivation

Today a user who searches for a film the server lacks sees nothing. The common workaround is a second application with its own URL, its own login, and its own interface, which the user has to know about and reach on another device.

The pieces to fix this already exist. Jellyfin plugins can expose API endpoints, and the Kotlin SDK can call arbitrary paths on the server. What is missing is an agreed shape for the conversation.

Doing this per product does not scale. A client that special-cases one request manager acquires a dependency on that product's API, its status vocabulary, and its release cycle. The client should not know which product is installed, and with a capability-based contract it does not have to.

## Non-goals

- Replacing any existing request manager. This is a bridge, not a competitor.
- Adding request management to the Jellyfin server itself.
- Approval workflows, quotas, or user management. Those belong to whatever fulfils requests.
- Progress reporting. See "States" below.

## Design

Four layers, each unaware of the one below it but one step:

```
Jellyfin client
      |  HTTP, existing Jellyfin authentication
      v
RequestBridge API           (a plugin endpoint, /RequestBridge)
      |  IRequestProvider
      v
Provider implementation
      |  product-specific protocol
      v
The tool that actually fulfils requests
```

### Capability discovery, not product detection

A client asks `GET /RequestBridge/Capabilities` once per server connection and receives a document describing what the provider **can do**, never what it **is**.

```json
{
  "apiVersion": 1,
  "providerDisplayName": "Example",
  "providerHealth": "Healthy",
  "supportedMediaTypes": ["Movie", "Series"],
  "supportedStates": ["Unknown", "Requestable", "Requested", "Processing", "Available"],
  "canSearch": true,
  "canRequest": true,
  "canReportStatus": true,
  "supportsSeasonSelection": true
}
```

`providerDisplayName` exists so an administrator settings screen can name the configured provider. Any client branching on its value has reintroduced exactly the coupling the flags exist to prevent.

Three properties of this design are worth calling out, because each solves a problem that only appears in practice:

**Absence is not an error.** A 404 means the plugin is not installed, and the client shows nothing. A server without RequestBridge behaves exactly as it does today. This was verified by removing the plugin and confirming the client renders identically.

**403 is not 404.** Installed-but-forbidden and not-installed are different conditions with different fixes. Merging them hides a permission problem behind an apparent absence.

**Health is data, not the success of the call.** The capabilities endpoint answers without contacting the provider. If it did contact it, a provider outage would be indistinguishable from the plugin not being installed, and clients would disable a feature that was briefly unavailable.

### States

Six states, and no others:

| State | Meaning |
|---|---|
| `Unknown` | Nothing could be determined |
| `Requestable` | Known, and not currently requested |
| `Requested` | Requested, fulfilment not started |
| `Processing` | Being fulfilled, stage unspecified |
| `PartiallyAvailable` | Some of it exists. Applies to series. |
| `Available` | In the library |

An earlier draft had separate searching, downloading, and importing states. They were removed after studying a real provider's API: it collapses all three into one value and exposes no download detail at all. Specifying states no provider can populate would have produced three states no client could render truthfully and no test could exercise.

The rule that follows: a state may be added only by demonstrating that a provider can determine it, never on the grounds that one might. Capability advertisement makes such an addition non-breaking, because clients already ask which states a provider emits and treat unrecognised ones as `Processing`.

No progress percentages. No estimated times. Nothing interpolated.

### Identity

An item is identified by external catalogue ids, primarily TMDB, and not by a Jellyfin item id.

This falls out of the problem: a requestable item is by definition absent from the library, so it has no Jellyfin identity to be keyed on. The server correlates results against the library and reports anything already held as `Available`, which stops a user being offered a request for media they already own.

### Where the client surfaces it

Search results, not the item detail screen.

The detail screen only exists for items the library has, and `FullDetailsFragment` navigates back when an item lookup returns null. A "request" button there could only ever appear for items that are already available. Search is where a user goes when the library lacks something, which is the same moment they would want to ask for it.

## Implementation status

A working implementation exists and has been exercised end to end against a real server, a real request manager, and a real Android TV client:

- Plugin: capability, search, item status, and request endpoints, all requiring an ordinary authenticated user rather than an administrator.
- Provider abstraction: `IRequestProvider`, with no dependency on Jellyfin or on any product.
- Two providers: a fake used to prove the architecture, and one for a real product.
- Android TV: discovery, requestable items in search results, request submission, and state refresh.
- 105 automated tests. Both suites were checked by deliberately introducing faults and confirming the right tests failed.

Swapping the fake provider for the real one changed one line in one file. Nothing in the API, the client, or the plugin's own code moved.

## Open questions for the community

1. **Attribution.** The reference implementation authenticates to the provider with a single administrator-configured key, so requests are not attributable to individual Jellyfin users. Per-user attribution needs a user mapping and would change `IRequestProvider`. Is attribution required for a first version?

2. **Where the abstraction lives.** It currently ships inside the plugin. If more than one provider is expected, a separate package would let providers be written without depending on the plugin.

3. **Other clients.** The contract is client-agnostic, but only Android TV has been implemented. Web and mobile would need their own work.

4. **Push updates.** Status is polled, no faster than once every fifteen seconds and only for items being fulfilled. Some providers can push via webhooks. Push would be an advertised capability layered over polling so that clients not understanding it keep working.

5. **Scope of the client change.** The Android TV change adds one row to the search screen and about three hundred lines. Is that acceptable as a single contribution, or should discovery land before the UI?

## What reviewers should push back on

Written by the author, because these are the weakest points:

- The client change touches shared card rendering to support artwork hosted outside the server. It is a small branch, but it is in code the feature does not own.
- The provider abstraction has one real implementation plus a fake. An abstraction validated by a single case may be shaped by that case more than its authors realise.
- Instrumentation tests do not exist. Model and logic layers are tested; the UI wiring is not.
- Requests are not attributable to users. See open question 1.
