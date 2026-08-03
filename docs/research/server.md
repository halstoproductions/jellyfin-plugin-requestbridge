# Research: Jellyfin Server Plugin System

Milestone 2 deliverable. Read-only study. No server code was modified.

**Source studied:** `jellyfin/jellyfin` at commit `26261db` (master), shallow clone in `_external/jellyfin`.

---

## 1. What I learned

A Jellyfin plugin is a .NET assembly dropped into the plugins folder with a `meta.json` manifest. The server discovers it, adds it to the DI container, and registers its assembly as an ASP.NET Core **application part**, which means any `ControllerBase` inside the plugin becomes a real API endpoint with no registration step. This is a genuinely good fit for RequestBridge.

The most consequential finding is not about how to build the plugin. It is that **the client cannot discover plugins**. `PluginsController` is gated behind `Policies.RequiresElevation`, so a normal Jellyfin user, which is what the Android TV client authenticates as, receives 403 when enumerating plugins. Capability discovery in Milestone 8 therefore cannot work by listing plugins. It must work by calling RequestBridge's own endpoint and treating a non-200 as absence.

The second consequential finding is a version fork in the road, covered in section 7.

---

## 2. Current architecture

### Plugin contract

Located in `MediaBrowser.Common/Plugins/`:

| Type | Role |
|---|---|
| `IPlugin` | Base contract: `Name`, `Description`, `Id`, `Version`, `AssemblyFilePath`, `DataFolderPath`, `GetPluginInfo()`, `OnUninstalling()` |
| `BasePlugin` | Abstract base implementing the plumbing |
| `BasePlugin<TConfigurationType>` | The one plugins actually derive from. Adds typed configuration. |
| `IHasPluginConfiguration` | Configuration contract, implemented by `BasePlugin<T>` |
| `IPluginManager` | Server-side lifecycle: `CreatePlugins()`, `LoadAssemblies()`, `RegisterServices()`, `EnablePlugin()`, `DisablePlugin()`, `RemovePlugin()` |
| `PluginManifest` | The `meta.json` shape |
| `LocalPlugin` | A discovered plugin on disk, with its manifest and a supported/unsupported flag |

`BasePlugin<T>` handles configuration entirely. `Configuration` lazy-loads from XML at `ApplicationPaths.PluginConfigurationsPath`, `SaveConfiguration()` serializes it back, and `UpdateConfiguration()` raises the `ConfigurationChanged` event. The plugin's `Id` comes from the assembly's `[Guid]` attribute (`BasePluginOfT.cs`, lines 59 to 66), so the plugin identity is declared once in `AssemblyInfo` rather than in code.

### Two additional interfaces that matter

```csharp
// MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs
public interface IPluginServiceRegistrator
{
    void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost);
}
```

Requires a parameterless constructor. This is where a plugin registers its own services into the server's DI container. **This is the dependency inversion seam RequestBridge needs**: the provider implementation gets registered here behind its interface.

```csharp
// MediaBrowser.Model/Plugins/IHasWebPages.cs
public interface IHasWebPages
{
    IEnumerable<PluginPageInfo> GetPages();
}
```

Implemented on the plugin class to expose embedded HTML configuration pages, served by `DashboardController`.

### Startup lifecycle and ordering

Verified in `Emby.Server.Implementations/ApplicationHost.cs`:

```
1. ApplicationHost.RegisterServices(serviceCollection)          (line 492)
      _pluginManager.RegisterServices(serviceCollection)
      -> PluginManager instantiates each IPluginServiceRegistrator
         and calls RegisterServices(serviceCollection, _appHost)   (PluginManager.cs:226)

2. GetComposablePartAssemblies()                                 (line 883)
      foreach (var p in _pluginManager.LoadAssemblies())          (PluginManager.cs:106)
      -> plugin assemblies enter the composable parts list

3. ApiServiceCollectionExtensions                                (line 158-161)
      .ConfigureApplicationPartManager(a => a.ApplicationParts.Clear())
      .AddApplicationPart(typeof(StartupController).Assembly)
      foreach (Assembly pluginAssembly in pluginAssemblies)
          mvcBuilder.AddApplicationPart(pluginAssembly);
      .AddControllersAsServices()

4. ApplicationHost                                               (line 728)
      _pluginManager.CreatePlugins()                              (PluginManager.cs:196)
      -> plugin instances constructed
```

Two things follow from this order. Services are registered **before** plugin instances are constructed, so a service registrator must not depend on the plugin instance existing. And controllers are registered with `AddControllersAsServices`, so plugin controllers get full constructor injection from the same container.

### Routing

`BaseJellyfinApiController`:

```csharp
[ApiController]
[Route("[controller]")]
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class BaseJellyfinApiController : ControllerBase
```

A plugin controller deriving from `ControllerBase` with `[Route("RequestBridge")]` is served at `/RequestBridge`. Deriving from `BaseJellyfinApiController` is not required, but it supplies the conventional route token, the content negotiation, and the `Ok<T>` helpers. There is no route registration step and no manifest entry for endpoints. Adding a controller class is sufficient.

`ApplicationParts.Clear()` at line 135 is worth knowing: the server deliberately clears auto-discovered parts and adds only the core API assembly plus explicitly-passed plugin assemblies. Controller discovery is therefore whitelist-based, not ambient.

### Authentication and authorization

Custom scheme, `AuthenticationSchemes.CustomAuthentication`, backed by `CustomAuthenticationHandler`. Applied with standard ASP.NET Core attributes:

- `[Authorize]` alone, as on `UserLibraryController`, means any authenticated user.
- `[Authorize(Policy = Policies.X)]` for the named policies in `MediaBrowser.Common/Api/Policies.cs`.

The full policy set: `FirstTimeSetupOrElevated`, `RequiresElevation`, `LocalAccessOnly`, `IgnoreParentalControl`, `Download`, `FirstTimeSetupOrDefault`, `LocalAccessOrRequiresElevation`, `AnonymousLanAccessPolicy`, `FirstTimeSetupOrIgnoreParentalControl`, `SyncPlayHasAccess`, `SyncPlayCreateGroup`, `SyncPlayJoinGroup`, `SyncPlayIsInGroup`, `CollectionManagement`, `LiveTvAccess`, `LiveTvManagement`, `SubtitleManagement`, `LyricManagement`.

There is no request-management policy. RequestBridge endpoints intended for ordinary users should use plain `[Authorize]`; administrative endpoints should use `Policies.RequiresElevation`.

### The manifest

`PluginManifest` fields on the target version 10.11.11: `Category`, `Changelog`, `Description`, `Id`, `Name`, `Overview`, `Owner`, `TargetAbi`, `Timestamp`, `Version`, `Status`, `AutoUpdate`, `ImagePath`, `Assemblies`.

Master adds one further field, `ImageResourceName`, for bundled plugins that ship their image as an embedded resource. It does not exist on 10.11.11 and must not be used.

`TargetAbi` is the minimum server version. Enforcement, `PluginManager.cs` line 693:

```csharp
if (!Version.TryParse(manifest.TargetAbi, out var targetAbi)) targetAbi = _minimumVersion;
...
return new LocalPlugin(dir, _appVersion >= targetAbi, manifest);
```

An unparseable or absent `TargetAbi` falls back to the minimum version rather than failing. A plugin whose `TargetAbi` exceeds the running server is marked unsupported (`PluginStatus.NotSupported`) rather than crashing the server.

---

## 3. Limitations

1. **Plugin enumeration is admin-only.** `PluginsController` carries a class-level `[Authorize(Policy = Policies.RequiresElevation)]`. A normal user cannot list installed plugins, read plugin configuration, or fetch a plugin manifest.
2. **No capability or feature-advertisement mechanism exists.** The server has no concept of "this installation supports feature X" that a client can query. Nothing to extend; RequestBridge must define its own.
3. **No plugin-to-client push channel is documented at the plugin layer.** The server has a WebSocket, but whether a plugin can publish messages onto it needs separate investigation before Milestone 12 commits to push over polling.
4. **Configuration is XML on disk**, one file per plugin, loaded lazily with a bare `catch` that silently substitutes a default instance on any deserialization failure (`BasePluginOfT.cs`, lines 188 to 197). Corrupt configuration is silently reset, not surfaced.
5. **Service registration happens before plugin construction**, so anything needing the plugin instance during registration has an ordering problem.
6. **`ApplicationParts.Clear()`** means a plugin assembly must be passed through the plugin manager path. A controller in a helper assembly that is not listed in the manifest's `Assemblies` will not be routed.

---

## 4. Opportunities

1. **Controllers are free.** A `RequestBridgeController : BaseJellyfinApiController` gives `/RequestBridge/...` with content negotiation, DI, and the standard auth attributes, with zero registration code. This is the RequestBridge API surface from the roadmap's layering diagram.
2. **`IPluginServiceRegistrator` is the dependency inversion seam.** Register `IRequestProvider` to `FakeRequestProvider` in Milestone 7, then swap the concrete type to the Seerr provider in Milestone 11 by changing one line in one file. The plugin, the controller, and the client all stay untouched. This is exactly the architecture the project requires, and the server hands it to us.
3. **`BasePlugin<TConfigurationType>` plus `IHasWebPages`** covers Milestone 6's configuration page and Milestone 11's provider settings, including persistence, with no custom code.
4. **`ConfigurationChanged` event** gives a clean way to re-resolve or invalidate a provider when an administrator changes the Seerr URL or API key.
5. **`TargetAbi` degrades gracefully.** A version mismatch marks the plugin unsupported rather than breaking the server, which lowers the risk of shipping early.

---

## 5. Can RequestBridge integrate here?

Yes. The server side is the least risky part of this project.

The shape is already determined by the existing contracts:

```
Plugin : BasePlugin<PluginConfiguration>, IHasWebPages      <- identity, config, config page
ServiceRegistrator : IPluginServiceRegistrator              <- binds IRequestProvider to an implementation
RequestBridgeController : BaseJellyfinApiController         <- /RequestBridge/... endpoints
IRequestProvider (in the abstractions project)              <- the seam; no Seerr above this line
```

Nothing here requires a server change, an upstream patch, or a new extension point. The plugin is an ordinary consumer of published interfaces.

### The discovery consequence

Because `PluginsController` requires elevation, Milestone 8 cannot ask "is RequestBridge installed?" through any existing server API. The workable pattern is:

- The client calls `GET /RequestBridge/Capabilities` as a normal authenticated user.
- **200** with a capability document means the provider exists and describes what it supports.
- **404** means the plugin is not installed. The client stays silent, exactly as it does today.
- **403 or 401** means installed but not permitted, which is a distinct state from absent and should not be conflated with it.

This keeps the client's knowledge limited to "a Request Provider exists and advertises these capabilities", which is precisely the abstraction boundary the project requires. It also means the capability endpoint must be reachable by an ordinary user, so plain `[Authorize]`, not `RequiresElevation`.

---

## 6. Candidate extension points, ranked

| Rank | Location | Why |
|---|---|---|
| 1 | `IPluginServiceRegistrator` | The provider swap point. The whole provider abstraction hangs off this. |
| 2 | `BaseJellyfinApiController` subclass in the plugin | The RequestBridge API surface, zero registration cost |
| 3 | `BasePlugin<TConfigurationType>` | Identity, versioning, typed configuration persistence |
| 4 | `IHasWebPages` | Administrator configuration page for Milestones 6 and 11 |
| 5 | `ConfigurationChanged` event | Provider re-initialization on settings change |

---

## 7. Target server version: DECIDED

**RequestBridge targets the Jellyfin stable 10.11 line, `net9.0`, with `targetAbi` declared as `10.11.0.0`.**

The reference source is tag `v10.11.11`. The development server is **10.11.10**, which is why `targetAbi` must be a minor-version floor rather than a specific patch: the server test is `_appVersion >= targetAbi`, so declaring `10.11.11` would refuse to load on 10.11.10. See `docs/architecture.md` section 3.

| | Target: latest stable | Not used: master |
|---|---|---|
| Version | **10.11.11**, released 2026-06-06 | 12.0.0, unreleased |
| Target framework | **net9.0** | net10.0 |
| Who runs it | essentially every real user | contributors and nightly builds |

Rationale: a plugin built against `net10.0` will not load on any server a real user runs. `TargetAbi` marks it unsupported, which is a clean failure rather than a crash, but it is still a plugin nobody can install. The first proof of architecture, Milestones 6 to 10, is worth far more if it runs on a server that exists. The situation is to be re-verified at Milestone 15, when upstream preparation begins against whatever is current then.

The local toolchain is .NET SDK 10.0.201, which can target `net9.0` given the reference pack.

### Cross-version verification

This document was written from master. Because the target is now stable, the plugin-relevant surface was diffed between `v10.11.11` and master rather than assumed. Result:

| File | Stable vs master |
|---|---|
| `MediaBrowser.Common/Plugins/IPlugin.cs` | identical |
| `MediaBrowser.Common/Plugins/BasePluginOfT.cs` | identical |
| `MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs` | identical |
| `MediaBrowser.Model/Plugins/IHasWebPages.cs` | identical |
| `MediaBrowser.Common/Api/Policies.cs` | identical |
| `Jellyfin.Api/BaseJellyfinApiController.cs` | identical |
| `MediaBrowser.Common/Plugins/PluginManifest.cs` | differs: master adds `ImageResourceName` |

Behaviour checked individually on the stable tag, all unchanged:

- `PluginsController` still carries `[Authorize(Policy = Policies.RequiresElevation)]` at class level, so the admin-only discovery constraint in section 5 holds on the target version.
- `ApplicationParts.Clear()`, `AddApplicationPart(pluginAssembly)` per plugin, and `AddControllersAsServices()` are all present, so controller routing works identically.
- `instance?.RegisterServices(serviceCollection, _appHost)` is present, so the `IPluginServiceRegistrator` seam is available.
- `new LocalPlugin(dir, _appVersion >= targetAbi, manifest)` is present, so `TargetAbi` degradation behaves as described.
- `MediaBrowser.Common.csproj` on the tag confirms `<TargetFramework>net9.0</TargetFramework>`.

Everything in this document therefore applies to the target version, with the single `ImageResourceName` exception noted in section 2. Line numbers cited are from master and may be off by a few lines on the tag; the content at those locations was verified to match.

---

## 8. Open questions for later milestones

1. ~~Which server version to target.~~ **Resolved.** Stable 10.11.11 / `net9.0`. See section 7.
2. ~~Do 10.11 and master differ in any of the interfaces used here?~~ **Resolved.** Diffed; identical apart from `ImageResourceName`. See section 7.
3. Can a plugin publish messages on the server WebSocket, or is client polling the only option? Affects Milestone 12's choice between push and poll.
4. Is there an existing plugin catalog listing requirement (repository JSON, signing) that shapes the build output? Affects Milestone 15.
5. Does the client-facing capability endpoint need to work before a user is fully authenticated? Affects where the Android client triggers discovery.

---

## 9. Verification notes

Every interface, attribute, line number, and policy name in this document was read directly from the cloned source at commit `26261db`. The stable target framework was read from `MediaBrowser.Common.csproj` at tag `v10.11.11`, and the latest release version from the GitHub releases API, rather than recalled.

No build was attempted. Building the server is not required by this milestone, and the plugin project that will actually need to compile does not exist until Milestone 6.
