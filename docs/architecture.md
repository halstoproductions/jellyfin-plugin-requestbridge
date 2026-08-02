# RequestBridge Architecture

**Status: stub.** Created by Milestone 0 to record the repository model only.

The full architecture is designed in Milestone 4 and must not be written ahead of it. This file exists so that the repository layout decision has a home inside the repository rather than only in `Claude.md`.

---

## Layering

The layering rule is fixed and defined in `Claude.md`. Restated here because every later decision depends on it:

```
Android TV client
      |
      v
Generic Request Provider API
      |
      v
Jellyfin plugin (RequestBridge)
      |
      v
Provider implementation (Seerr first)
```

No layer may be skipped. The Android TV client never learns which provider is behind RequestBridge.

---

## Repository model

The `RequestBridge` folder is the repository root. Its parent folder is a container only and is not version controlled.

Three tracks live in two physical locations.

| Track | Location | Upstream target |
|---|---|---|
| Provider specification | this repo, `src/RequestBridge.Abstractions` | ships inside the plugin initially |
| Jellyfin server plugin | this repo, `src/Jellyfin.Plugin.RequestBridge` | Jellyfin plugin catalog |
| Android TV client changes | separate fork of `jellyfin-androidtv`, checked out beside this repo | upstream PR to jellyfin-androidtv |

### Rules

This repository never contains Android client source. The Android TV client is a Kotlin and Gradle application with its own upstream project. A Jellyfin server plugin cannot inject UI into it, so client work is a genuinely separate contribution.

Milestones 8, 9, and 10 are therefore executed in the sibling fork. They produce their own commits and their own upstream pull request. The same applies to the Android UI tests in Milestone 14.

External repositories cloned for research live in a sibling `_external/` directory outside this repository. They are read-only reference material and are ignored by `.gitignore` as a safety net.

---

## Provider target

The first provider targets Seerr, the merged Overseerr and Jellyseerr codebase at `seerr-team/seerr`. Seerr supports Jellyfin, Plex, and Emby, so a Seerr provider must not assume Jellyfin on the far side.

Legacy Overseerr and Jellyseerr deployments remain in the wild. Supporting them is a possible future provider, never a version branch inside the Seerr provider.

---

## Deferred to Milestone 4

Everything else, specifically:

- Component diagram
- Request state machine, beyond the initial sketch in `Claude.md`
- HTTP API surface
- Interface definitions
- Capability discovery mechanism
- Authentication model

Do not fill these in early.
