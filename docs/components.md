# RequestBridge Components

Milestone 4 deliverable. What gets built, where it lives, and what depends on what.

---

## 1. Component diagram

```
+--------------------------------------------------------------+
|  jellyfin-androidtv fork            (separate repository)     |
|                                                              |
|    RequestBridgeApi : Api           discovery + calls        |
|    capability store                 "does a provider exist"  |
|    search result affordance         Milestone 9              |
+----------------------------|---------------------------------+
                             |  HTTP, Jellyfin auth
                             v
+--------------------------------------------------------------+
|  src/Jellyfin.Plugin.RequestBridge          (this repo)       |
|                                                              |
|    Plugin                  BasePlugin<PluginConfiguration>,  |
|                            IHasWebPages                      |
|    PluginServiceRegistrator IPluginServiceRegistrator        |
|    RequestBridgeController BaseJellyfinApiController         |
|    Dto/*                   wire models                       |
|    Mapping                 domain <-> wire                   |
+----------------------------|---------------------------------+
                             |  IRequestProvider
                             v
+--------------------------------------------------------------+
|  src/RequestBridge.Abstractions             (this repo)       |
|                                                              |
|    IRequestProvider        the seam                          |
|    RequestItem, ExternalId, MediaType, RequestState          |
|    RequestResult, ProviderCapabilities                       |
|    ProviderException, ProviderErrorCode                      |
|                                                              |
|    No Jellyfin references. No Seerr references. No HTTP.     |
+----------------------------^---------------------------------+
                             |  implements
        +--------------------+--------------------+
        |                                         |
+-------------------------+          +-------------------------+
| FakeRequestProvider     |          | SeerrRequestProvider    |
| Milestone 7             |          | Milestone 11            |
| in the plugin assembly  |          | knows Seerr, exclusively|
+-------------------------+          +-------------------------+
```

---

## 2. `src/RequestBridge.Abstractions`

The specification project. Milestone 5 builds this and nothing else.

**Hard constraints.** No reference to any Jellyfin assembly. No reference to Seerr. No HTTP types. No ASP.NET types. If this project cannot compile standalone against nothing but the base class library, the abstraction has leaked.

### `IRequestProvider`

```csharp
public interface IRequestProvider
{
    ProviderCapabilities Capabilities { get; }

    Task<IReadOnlyList<RequestItem>> SearchAsync(
        string query, MediaType? mediaType, int limit, CancellationToken ct);

    Task<RequestItem?> GetItemAsync(
        ExternalId id, MediaType mediaType, CancellationToken ct);

    Task<RequestResult> RequestAsync(
        ExternalId id, MediaType mediaType, IReadOnlyList<int>? seasons, CancellationToken ct);
}
```

Design notes:

- `Capabilities` is a property, not a call, so the capabilities endpoint can answer while the provider is unreachable. A provider health signal is data inside `ProviderCapabilities`, not the success of a call.
- Every method takes a `CancellationToken`. A remote request that outlives the screen that asked for it is a bug.
- `GetItemAsync` returns null for "no such item", rather than throwing. Absence is an ordinary outcome, not an exception.
- There is no requesting-user parameter. See architecture decision 5.3, and the debt in architecture section 8.
- No method exposes an approval concept, a progress value, or a provider identifier.

### Models

| Type | Shape | Notes |
|---|---|---|
| `ExternalId` | `record(ExternalIdSource Source, string Value)` | Immutable. Value equality matters for lookups. |
| `ExternalIdSource` | enum `Tmdb, Tvdb, Imdb` | No magic strings anywhere |
| `MediaType` | enum `Movie, Series` | |
| `RequestState` | enum, the six states | See [state-machine.md](state-machine.md) |
| `RequestItem` | record with `ExternalIds`, `MediaType`, `Title`, `Year`, `Overview`, `ImageUrl`, `State`, `JellyfinItemId?` | Immutable |
| `RequestResult` | `record(bool Accepted, RequestItem Item)` | |
| `ProviderCapabilities` | see [capabilities.md](capabilities.md) | Immutable |
| `ProviderException` | carries `ProviderErrorCode` | The only exception type crossing the seam |
| `ProviderErrorCode` | enum matching [api.md](api.md) section 2 | Mapped to HTTP by the controller, never by the provider |

Immutability throughout, per the coding standards. A provider must not be able to hand back an object the controller can mutate.

---

## 3. `src/Jellyfin.Plugin.RequestBridge`

The Jellyfin-facing plugin. Milestone 6 builds the skeleton.

### `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages`

Identity, versioning, typed configuration persistence, and the administrator configuration page. `Id` comes from the assembly `[Guid]` attribute, so plugin identity is declared once in assembly metadata rather than in code.

### `PluginConfiguration : BasePluginConfiguration`

Provider selection and provider settings, including the base URL and API key for Milestone 11. Persisted as XML by the base class with no custom code.

The `ConfigurationChanged` event is the hook for re-initialising a provider when an administrator changes settings.

### `PluginServiceRegistrator : IPluginServiceRegistrator`

**The single most important file in the project.**

```csharp
public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
{
    services.AddSingleton<IRequestProvider, FakeRequestProvider>();   // Milestone 7
    // Milestone 11 changes exactly this line to SeerrRequestProvider
}
```

Requires a parameterless constructor. Runs before plugin instances are constructed, so it must not depend on `Plugin.Instance` existing.

This is where Milestone 11's "replace the fake provider" becomes a one-line change with the controller, API, and client untouched. If replacing the provider ever requires touching a second file, the abstraction has failed and the fix is to restore it, not to work around it.

### `RequestBridgeController : BaseJellyfinApiController`

```csharp
[Authorize]
[Route("RequestBridge")]
public class RequestBridgeController : BaseJellyfinApiController
```

Constructor-injects `IRequestProvider` and `ILibraryManager`. No registration code is needed: the server clears application parts and adds each plugin assembly explicitly, then calls `AddControllersAsServices()`, so a controller class in the plugin assembly is routed and gets full DI.

Responsibilities, and only these:

1. Validate input.
2. Call the provider.
3. Check Jellyfin library presence and override state to `Available` where the server has the file.
4. Map domain models to wire DTOs.
5. Map `ProviderErrorCode` to HTTP status and `ProblemResponse`.

The controller contains no provider logic and no Seerr knowledge.

### `Dto/` and mapping

Wire models mirroring [api.md](api.md), kept separate from the abstraction models.

This is a deliberate acceptance of mechanical mapping code, and the reason is worth stating because it looks like duplication. The provider interface serves .NET provider authors and may change when a provider demands it. The HTTP contract serves shipped Android clients that cannot be updated in lockstep with the server. Fusing them would mean a provider-driven refactor silently changing the wire format under deployed clients. The mapping is mechanical, contains no logic, and buys independent evolution of two contracts with different audiences.

---

## 4. Providers

### `FakeRequestProvider`, Milestone 7

Lives in the plugin assembly. Returns `Requestable` before a request and `Requested` afterwards, held in memory for the process lifetime. No persistence, no timers, no simulated progression.

Its purpose is to prove the architecture end to end, so it must be boring. A fake that simulates progress would be testing the fake.

### `SeerrRequestProvider`, Milestone 11

The only component in the entire system permitted to know Seerr exists.

Holds: base URL, API key sent as `X-Api-Key`, an `HttpClient` from `IHttpClientFactory`, the status mapping table from [state-machine.md](state-machine.md) section 5, and translation of Seerr failures into `ProviderException`.

Whether it lives in the plugin assembly or a separate project is a Milestone 11 decision. It does not affect the architecture, because the seam is the interface.

---

## 5. Client components, in the fork

Not in this repository. Listed so the whole picture is visible.

| Component | Milestone | Notes |
|---|---|---|
| `RequestBridgeApi : Api` | 8 | Registered via `ApiClient.getOrCreateApi`, using the SDK's generic `request` method. No SDK change needed. |
| Capability store | 8 | Holds the discovery result for the session. Koin singleton. |
| Search result affordance | 9 | Renders on provider-sourced results per architecture decision 5.1 |
| Request action and refresh | 10 | Calls `POST /RequestBridge/Requests`, then re-reads the item |

The SDK's `ApiClient` already exposes `suspend fun request(method, pathTemplate, pathParameters, queryParameters, requestBody): RawResponse` and appends the access token to all requests, so plugin endpoints are reachable and authenticated with no SDK modification. Verified in [research/androidtv.md](research/androidtv.md).

---

## 6. Dependency rules

1. `RequestBridge.Abstractions` depends on nothing. Ever.
2. `Jellyfin.Plugin.RequestBridge` depends on the abstractions and on Jellyfin. It must not depend on any provider implementation except through DI registration in one file.
3. Providers depend on the abstractions. They must not depend on the plugin, the controller, or the DTOs.
4. The client depends on the HTTP contract in [api.md](api.md). It must not depend on provider identity.

A violation of any of these is an architectural regression, not a style preference.
