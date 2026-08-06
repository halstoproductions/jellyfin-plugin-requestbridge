# Contributing to RequestBridge

## What this is

A provider-agnostic media request API for Jellyfin. A client learns that a request provider exists and what it can do, never which product is behind it.

If you are about to write code, read [docs/architecture.md](docs/architecture.md) first. It is short, and it explains why several things that look overcomplicated are not.

## Layout

```
RequestBridge/                     this repository
    src/RequestBridge.Abstractions   the provider contract
    src/Jellyfin.Plugin.RequestBridge the Jellyfin plugin
    tests/                            test projects
    docs/                             design and research
```

Client changes live in a separate fork of `jellyfin-androidtv` and are contributed there. This repository never contains Android client source.

## Building

```
dotnet build -c Release
dotnet test -c Release
```

Requires the .NET SDK. The projects target `net9.0` to match the Jellyfin 10.11 line.

Deploy to a local server with `scripts/deploy-local.ps1`, then restart Jellyfin. The server discovers plugins only at startup, so a running server will not notice a new folder.

## The rules that matter

These are not style preferences. Breaking one of them breaks the point of the project.

### 1. No product name above the provider layer

`RequestBridge.Abstractions` and the HTTP API must be readable by someone who has never heard of any particular request manager. No product-specific status value, concept, identifier, or error string may escape a provider implementation.

If you catch yourself adding a branch for one product outside its own provider class, the abstraction is missing something. Add the missing capability instead.

### 2. Clients branch on capabilities, never on identity

`providerDisplayName` is for display in administrator interfaces. Any conditional whose outcome depends on its value is a defect, and reviewers should treat it as one.

If behaviour must vary between providers, that difference needs a capability flag, and the flag needs a defined behaviour when false.

### 3. Only expose states that can be determined

Six states exist because a provider can prove each one from something observable. Adding a seventh requires demonstrating that a provider can determine it, not that one might.

No progress percentages, no estimates, no interpolation between states.

### 4. Absence is normal

A server without this plugin, a plugin without a provider, and a provider that is unreachable are all ordinary conditions. Each shows the user nothing and logs nothing alarming. None of them is an error dialog.

### 5. The abstraction has to earn its keep

Replacing one provider with another should be a change to a single dependency registration. If it ever requires a second edit elsewhere, that is a bug in the abstraction, and the fix is to restore it rather than work around it.

## Adding a provider

1. Implement `IRequestProvider` in its own class. That class is the only place your product may be named.
2. Advertise only what you can do. Listing a state you cannot emit, or a capability you ignore, is a lie no client can detect.
3. Translate every failure into `ProviderException` with a `ProviderErrorCode`. Do not let transport exceptions escape.
4. Log what actually went wrong. The exception carries a neutral message on purpose, so the log is the only place an administrator can find the real cause.
5. Register it in `PluginServiceRegistrator`.

Provider configuration arrives through `IPluginConfigurationSource`. Do not read `Plugin.Instance` directly: it is null during service registration and throughout every test, which makes anything reading it impossible to exercise.

## Tests

Run `dotnet test` before opening a pull request. New behaviour needs tests, and so does any bug you fix.

Two things worth knowing about the existing suite:

**Tests are checked for teeth.** A suite that passes on the first run may be asserting nothing. When adding tests, break the code deliberately and confirm the right test fails.

**Provider tests assert on outgoing requests.** The one bug that reached a live server was a serialised null in a request body. No amount of response mocking would have caught it. `SeerrTestHarness` records requests for this reason.

## Commit messages

Prefix with one of: `Research:`, `Feature:`, `Refactor:`, `Fix:`, `Docs:`, `Chore:`.

Explain why, not what. The diff already says what.

## Pull requests

See [docs/pull-request-checklist.md](docs/pull-request-checklist.md).

There are two upstream targets with different reviewers and different standards: the plugin catalog for the server side, and `jellyfin-androidtv` for the client. A change spanning both is two pull requests, and the client one should assume its reviewers have never read this repository.
