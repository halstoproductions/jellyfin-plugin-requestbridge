# RequestBridge

A provider-agnostic media request API for Jellyfin.

When a user searches for something the server does not have, RequestBridge lets a client offer to request it, without the client knowing what fulfils the request.

```
Jellyfin client
      |  HTTP, existing Jellyfin authentication
      v
RequestBridge API           /RequestBridge, a plugin endpoint
      |  IRequestProvider
      v
Provider implementation
      |  product-specific protocol
      v
The tool that actually fulfils requests
```

The client asks what the provider **can do**, never what it **is**.

## Status

Working, and exercised end to end against a real server, a real request manager, and a real Android TV client. Not yet released.

- Plugin: capability, search, item status, and request endpoints
- Provider abstraction with two implementations, one of them a fake used to prove the architecture
- Android TV: discovery, requestable items in search, request submission, state refresh
- 105 automated tests

## Requirements

- Jellyfin 10.11 or newer
- A supported request provider, configured on the plugin's settings page

## Installing

Build and copy the output into your server's `plugins` directory, then restart Jellyfin:

```
dotnet build -c Release
./scripts/deploy-local.ps1
```

Configure the provider URL and API key at Dashboard, Plugins, RequestBridge.

Without a configured provider the plugin reports itself as unconfigured and clients show nothing.

## Behaviour when it is not there

A server without this plugin behaves exactly as it did before. Clients treat a 404 during discovery as "no provider", show nothing, and log nothing alarming. This is verified rather than assumed: the plugin is removed, the client is restarted, and the screens are compared.

## Documentation

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Layering, decisions, and why several things are the way they are |
| [docs/api.md](docs/api.md) | The HTTP contract |
| [docs/state-machine.md](docs/state-machine.md) | The six states and what makes each determinable |
| [docs/capabilities.md](docs/capabilities.md) | Discovery, versioning, and degradation |
| [docs/components.md](docs/components.md) | What exists and what depends on what |
| [docs/rfc.md](docs/rfc.md) | The upstream proposal |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Rules that matter, and how to add a provider |

## Design notes worth knowing

**Six states, not more.** An earlier draft had separate searching, downloading, and importing states. They were removed after finding that a real provider collapses all three into one value and exposes no download detail at all. Specifying states nothing can populate produces states no client can render truthfully.

**No progress bars.** Nothing here interpolates, estimates, or invents.

**Absence, forbidden, and unreachable are three different things.** Merging them hides real problems behind apparent absence.

**Identity is external ids, not Jellyfin item ids.** A requestable item is by definition absent from the library, so it has no Jellyfin identity to be keyed on. The server correlates against the library so that anything already held is reported as available rather than offered for request.

## Licence

Intended for contribution to the Jellyfin ecosystem. Licence to be confirmed before release.
