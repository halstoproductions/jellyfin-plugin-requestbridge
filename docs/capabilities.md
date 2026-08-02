# RequestBridge Capability Discovery

Milestone 4 deliverable. How a client learns that a Request Provider exists and what it can do, without learning what it is.

---

## 1. Why capabilities exist

The client must be able to render the right thing against providers with different abilities, without ever branching on provider identity.

The rule, in one line:

> A client branches on what a provider **can do**, never on what a provider **is**.

`if (provider == "seerr")` is an architectural violation. `if (capabilities.canRequest)` is the intended shape. This is what keeps Seerr out of the client, and it is the reason this document exists as a first-class part of the design rather than as a field on some other response.

---

## 2. The document

`GET /RequestBridge/Capabilities`, 200:

```json
{
  "apiVersion": 1,
  "providerDisplayName": "Seerr",
  "providerHealth": "Healthy",
  "supportedMediaTypes": ["Movie", "Series"],
  "supportedStates": [
    "Unknown", "Requestable", "Requested",
    "Processing", "PartiallyAvailable", "Available"
  ],
  "canSearch": true,
  "canRequest": true,
  "canReportStatus": true,
  "supportsSeasonSelection": true
}
```

| Field | Type | Purpose |
|---|---|---|
| `apiVersion` | int | Contract version. See section 4. |
| `providerDisplayName` | string | **Administrator-facing display only.** See section 3. |
| `providerHealth` | enum | `Healthy`, `Degraded`, `Unreachable`, `NotConfigured` |
| `supportedMediaTypes` | array | Which media types the provider handles at all |
| `supportedStates` | array | Which states this provider can actually emit |
| `canSearch` | bool | Whether `GET /Search` is usable |
| `canRequest` | bool | Whether `POST /Requests` is usable |
| `canReportStatus` | bool | Whether item state is meaningful, or always `Unknown` |
| `supportsSeasonSelection` | bool | Whether `seasons` on a request is honoured |

Absent fields are treated as false or empty. A client must not fail on unknown fields, because a newer server will send them.

---

## 3. `providerDisplayName` and its one legitimate use

It exists so an administrator settings screen can say which provider is configured. That is the whole purpose.

**Permitted:** rendering it as text in an administrator-facing UI.

**Forbidden:** any conditional whose outcome depends on its value. Feature detection, workarounds, string comparison, or logging that drives behaviour. If a client needs to know whether a provider can do something, that thing needs a capability flag, and adding one is the correct fix.

This field is the most likely place for the architecture to rot, because the shortcut is always available and always tempting. Treat any code that reads it inside a conditional as a defect.

---

## 4. Versioning

`apiVersion` is an integer, incremented only on a **breaking** change to [api.md](api.md).

Additive changes do not increment it: new capability fields, new optional response fields, new states.

Client rules:

1. If `apiVersion` is higher than the client understands, continue using the fields it recognises. Do not refuse to work.
2. If `apiVersion` is lower than the client's minimum, disable the feature silently.
3. Never assume a field exists because a previous server sent it.

There is no version in the URL path, deliberately. Path versioning would force a client to know which versions exist before it can ask anything. Asking the capabilities endpoint answers that question in one call, which is the same call the client already has to make for discovery.

---

## 5. Health, and why it is data

`providerHealth` reports whether the provider is reachable. It is a **field**, not the success or failure of the capabilities call.

The capabilities endpoint must answer without contacting the provider. If it contacted the provider, then a provider outage would look identical to the plugin not being installed, and the client would silently disable a feature that is merely temporarily unavailable.

| Value | Meaning | Suggested client behaviour |
|---|---|---|
| `Healthy` | Provider answered normally | Full functionality |
| `Degraded` | Reachable, some operations failing | Full functionality, tolerate errors |
| `Unreachable` | Configured but not answering | Show nothing, retry later |
| `NotConfigured` | Plugin installed, no provider set up | Show nothing, do not retry |

For Seerr, health can be established cheaply: `GET /api/v1/status` requires no authentication, so an unreachable instance is distinguishable from a rejected API key without using a credential.

---

## 6. Discovery flow

```
Client authenticates to Jellyfin
      |
      v
GET /RequestBridge/Capabilities
      |
      +-- 200 ------> store the document for the session
      |                     |
      |                     +-- providerHealth == Healthy or Degraded
      |                     |         -> feature enabled
      |                     +-- otherwise
      |                               -> feature disabled, silently
      |
      +-- 404 ------> plugin not installed. Disable. Do not retry this session.
      +-- 403 ------> installed, user not permitted. Disable. Do not retry.
                      NOT the same as 404.
      +-- 401 ------> retry after authentication
      +-- 503 ------> no provider configured. Disable.
      +-- 5xx ------> unknown. Retry with backoff.
```

Discovery runs **once per server connection**, not per item. The result is held for the session. An item-level call must never be the thing that discovers whether the feature exists.

`403` must never be folded into `404`. Absent and forbidden are different conditions with different fixes, and merging them produces a bug that is essentially undebuggable from the client side.

---

## 7. Degradation

Every capability flag has a defined off behaviour, so a partial provider is usable rather than broken.

| Flag false | Client behaviour |
|---|---|
| `canSearch` | No request discovery surface. The feature is effectively invisible. |
| `canRequest` | Show state where known, offer no request action |
| `canReportStatus` | Offer requests, show no state |
| `supportsSeasonSelection` | Request whole series only, do not offer season pickers |
| media type absent | That type behaves as if RequestBridge is not installed |

The failure mode of every unknown is **show nothing**. Never an error dialog, and never a guess. A user who has no request provider should not be able to tell that this feature exists at all.

---

## 8. Adding a capability later

1. Add the field. Do not increment `apiVersion`.
2. Define the false behaviour in section 7 before implementing the true behaviour.
3. Older clients ignore it and keep working.

If a change cannot be expressed this way, it is breaking, and it increments `apiVersion`.
