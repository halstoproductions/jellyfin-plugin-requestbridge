# Research: Jellyfin Android TV Client

Milestone 1 deliverable. Read-only study. No client code was modified.

**Source studied:** `jellyfin/jellyfin-androidtv` at commit `394f869`, shallow clone in `_external/jellyfin-androidtv`.
**Jellyfin Kotlin SDK version in use:** `org.jellyfin.sdk:jellyfin-core:1.8.12`.

---

## 1. What I learned

The detail screen is not built from a declarative model. It is a Leanback `Fragment` that imperatively constructs button `View` objects and appends them to a row object. There is no ViewModel for it, no state machine, and no capability concept. The client also has no awareness of server plugins whatsoever.

The single most important finding is that this does not block RequestBridge. The SDK's `ApiClient` exposes a generic `request` method for arbitrary paths, and the `Api` marker interface plus `getOrCreateApi` gives a first-class way to add a typed client for a plugin endpoint. So the client can talk to a RequestBridge endpoint without any SDK change and without raw HTTP plumbing.

---

## 2. Current architecture

### Module layout

Gradle multi-module, from `settings.gradle.kts`:

| Module | Role |
|---|---|
| `:app` | The application, including all detail screen code |
| `:design` | Shared design resources |
| `:playback:core`, `:playback:jellyfin`, `:playback:media3:*` | Playback stack |
| `:preference` | Preference storage abstraction |

Detail screen code lives entirely in `:app`, package `org.jellyfin.androidtv.ui.itemdetail`.

### The key classes

| Class | Language | Lines | Role |
|---|---|---|---|
| `ui/itemdetail/FullDetailsFragment.java` | Java | 1241 | The movie, series, season, and episode detail screen. Owns all button construction. |
| `ui/itemdetail/FullDetailsFragmentHelper.kt` | Kotlin | 400 | Kotlin extension functions on the fragment. All server calls live here. |
| `ui/itemdetail/MyDetailsOverviewRow.kt` | Kotlin | 24 | The row model. Holds the item, image, summary, three info slots, and the action list. |
| `ui/presentation/MyDetailsOverviewRowPresenter.kt` | Kotlin | 89 | Leanback presenter that binds the row model into `DetailRowView`. |
| `ui/TextUnderButton.kt` | Kotlin | 61 | The action button widget itself: an icon with a label underneath. |
| `ui/DetailRowView.kt` | Kotlin | | View binding host, exposes `binding.fdButtonRow` as the button container. |

### Flow from API response to rendered button

```
Navigation supplies "ItemId" as a fragment argument
      |
      v
FullDetailsFragment.onCreateView            (line 158)
  inflates FragmentFullDetailsBinding
  creates MyDetailsOverviewRowPresenter
  calls loadItem(mItemId)                   (line 212)
      |
      v
loadItem                                    (line 358)
  delegates to FullDetailsFragmentHelper.getItem
      |
      v
getItem                                     (helper, line 190)
  api.userLibraryApi.getItem(id).content
  on lifecycleScope + Dispatchers.IO
  returns BaseItemDto through a callback
      |
      v
setBaseItem(item)                           (line 487)
  stores mBaseItem, sets background
      |
      v
BuildDorTask (AsyncTask)                    (line 409)
  doInBackground:  builds MyDetailsOverviewRow, image, summary, info items
  onPostExecute:   builds ClassPresenterSelector, sets rows adapter,
                   adds the row, then calls updateInfo(...)
      |
      v
updateInfo(item)                            (line 671)
  if buttonTypeList.contains(item.getType()):
      mDetailsOverviewRow.clearActions()
      addButtons(BUTTON_SIZE)
      |
      v
addButtons(buttonSize)                      (line 766)
  for each supported action:
      TextUnderButton.create(context, icon, size, padding, label, onClick)
      mDetailsOverviewRow.addAction(button)
      |
      v
MyDetailsOverviewRowPresenter.ViewHolder.setItem   (presenter, line 21)
  binding.fdButtonRow.removeAllViews()
  for (button in row.actions) binding.fdButtonRow.addView(button)
```

### How a button is actually declared

Every action is the same five-line shape inside `addButtons`. The favorite button, at line 970:

```java
favButton = TextUnderButton.create(requireContext(), R.drawable.ic_heart, buttonSize, 2,
        getString(R.string.lbl_favorite), new View.OnClickListener() { ... });
mDetailsOverviewRow.addAction(favButton);
```

`TextUnderButton.create` is a static factory taking context, a drawable resource, max height, padding, a label, and a click listener. Buttons are plain `FrameLayout` subclasses, focusable, with `descendantFocusability = FOCUS_BLOCK_DESCENDANTS` for the TV remote.

### Which item types get buttons

`buttonTypes`, line 335. A static array converted to `buttonTypeList`:

`EPISODE, MOVIE, SERIES, SEASON, FOLDER, VIDEO, RECORDING, PROGRAM, TRAILER, MUSIC_ARTIST, PERSON, MUSIC_VIDEO`

`updateInfo` only builds buttons when the item type is in this list. Movie and series, the two types RequestBridge cares about most, are both present.

### Button overflow

`showMoreButtonIfNeeded`, line 1123, enforces a soft cap of five visible actions. A hardcoded priority list (queue, trailer, shuffle, favorite, go-to-series) is reversed so the least important collapse first into a "more" popup menu. The comment at line 1127 notes the order must match `res/menu/menu_details_more.xml`, which is a manual synchronization requirement between Java and XML.

### Dependency injection

Koin, with modules in `di/`: `AndroidModule`, `AppModule`, `AuthModule`, `PlaybackModule`, `PreferenceModule`, `UtilsModule`. The fragment resolves dependencies through `inject(...)` into `Lazy` fields, including `ApiClient`, `NavigationRepository`, `UserPreferences`, and `DataRefreshService`.

### The SDK client contract

Verified directly from `jellyfin-sdk-kotlin`, `jellyfin-api/.../api/client/ApiClient.kt`:

```kotlin
public abstract suspend fun request(
    method: HttpMethod = HttpMethod.GET,
    pathTemplate: String,
    pathParameters: Map<String, Any?> = emptyMap(),
    queryParameters: Map<String, Any?> = emptyMap(),
    requestBody: Any? = null,
): RawResponse
```

Plus `getOrCreateApi<T : Api>`, where `Api` is a bare marker interface (`public interface Api`). `accessToken` is documented as "Appended to all requests if set", so authentication is handled by the client for any path, including plugin paths. `RawResponse` carries `body`, `status`, and `headers`, with `createContent<T>()` and `createResponse<T>()` for deserialization.

---

## 3. Limitations

1. **No ViewModel for the detail screen.** Of eleven ViewModels in the app, none belongs to `itemdetail`. State lives in mutable fragment fields (`mBaseItem`, `mDetailsOverviewRow`, and eight button fields). There is nowhere natural to hang asynchronous request state.
2. **Buttons are views, not data.** `MyDetailsOverviewRow.actions` is a `MutableList<TextUnderButton>`, a list of Android views. There is no action model, no id, no ordering key, no enabled or state concept. A button's state is expressed by calling `setActivated` or `setVisibility` on the view itself.
3. **`addButtons` is a 300-line imperative method** with the full action set inlined, using deprecated `AsyncTask` upstream of it. Any new button means editing this method, which is exactly the coupling that makes an upstream PR harder to argue for.
4. **Refresh is coarse.** `updateInfo` clears every action and rebuilds all of them. There is no way to update one button in place, other than mutating the retained field directly (as `setRecTimer` does at line 393).
5. **The five-button cap is hardcoded**, with a hand-maintained priority list that must stay in sync with an XML menu.
6. **Zero plugin awareness.** A search across `app/src/main/java` for plugin API usage returns only ACRA telemetry and the internal playback plugin system. The client never calls the server's plugin endpoints and has no notion of optional server capabilities.
7. **Java, not Kotlin.** The main file is still Java while the surrounding helpers have been migrated to Kotlin. New Java in this file would run against the migration direction.

---

## 4. Opportunities

1. **`ApiClient.request` is the integration point.** A RequestBridge endpoint can be reached today, with authentication, without touching the SDK. This is the single most important enabler and it is already verified.
2. **`getOrCreateApi` plus the `Api` marker interface** gives a clean, idiomatic place for a typed `RequestBridgeApi`, matching how every generated Jellyfin API is structured. Capability discovery in Milestone 8 should use this shape rather than ad hoc calls.
3. **`FullDetailsFragmentHelper.kt` is the right home for new server calls.** Every existing call lives there as a Kotlin extension function on the fragment, with a consistent `lifecycleScope` plus `Dispatchers.IO` plus callback shape and `ApiClientException` handling. A `getRequestState` extension would be indistinguishable in style from `getItem`.
4. **One insertion point for the button.** A request button needs exactly one `TextUnderButton.create` plus one `addAction` inside `addButtons`, gated on state. That is a genuinely small upstream diff.
5. **`updateInfo` already re-runs `addButtons`** on refresh, so a state change can be reflected by re-invoking the existing refresh path rather than inventing one. Useful for Milestone 10.
6. **`buttonTypeList` already includes `MOVIE` and `SERIES`.** No change needed there.

---

## 5. Can RequestBridge integrate here?

Yes, and cleanly, with one significant caveat.

**What works.** The client can call a plugin endpoint through the injected `ApiClient` with no SDK modification. New server calls have an established idiom in the helper file. Adding one button to `addButtons` is a small diff of the same shape as the existing twelve.

**The caveat.** The natural place for a "Request" button is the detail screen for an item, and the detail screen only exists for items the server already has. An item that is not in the library has no `BaseItemDto` and therefore no detail screen to attach a button to. `FullDetailsFragment.loadItem` navigates back if `getItem` returns null (line 369 and line 383).

This means the state machine splits across two very different UI surfaces:

- `AVAILABLE` is the only state reachable from the existing detail screen, because the item is in the library by definition.
- `REQUESTABLE`, `REQUESTED`, `SEARCHING`, `DOWNLOADING`, `IMPORTING` describe items that are **not** in the library, and therefore have no detail screen today.

The roadmap's Milestone 9 says to show the button when state is `REQUESTABLE`, which is precisely the state that cannot occur on the screen the button is being added to.

This is an architectural question, not a coding problem, and it must be answered before Milestone 8 rather than discovered during Milestone 9. It does not change the plugin or provider design, only where the client surfaces the result. I am flagging it rather than resolving it, because resolving it is Milestone 4's job.

Three candidate resolutions, listed without recommendation:

1. **Search-driven.** Surface requestable items in search results, where the user is already looking for something the library lacks. Needs a new detail-like screen or an extension of search result handling.
2. **Partial detail screen.** Let the detail screen open on a provider-supplied item stub rather than a library `BaseItemDto`. Larger client change, larger upstream ask.
3. **Reduce Milestone 9's scope honestly.** Prove capability discovery and button rendering on an item that *is* in the library, using a deliberately fake state, and defer the not-in-library surface to a later milestone. Smallest proof, matches the roadmap's "fake provider" spirit, but the button appears where a real user would never need it.

---

## 6. Candidate extension points, ranked

| Rank | Location | Why |
|---|---|---|
| 1 | `ApiClient.request` plus a `RequestBridgeApi : Api` registered through `getOrCreateApi` | Verified to exist, no SDK change, authentication handled |
| 2 | `FullDetailsFragmentHelper.kt`, new extension function | Matches the established idiom for every server call on this screen |
| 3 | `FullDetailsFragment.addButtons`, line 766 | The one place a button must be added; smallest possible diff |
| 4 | `FullDetailsFragment.updateInfo`, line 671 | Existing refresh path, reusable for state changes |
| 5 | Koin module in `di/` | Where a discovery or state repository would be registered as a singleton |

---

## 7. Open questions for later milestones

1. Where does a requestable item live in the UI, given it has no detail screen? See section 5. Belongs to Milestone 4.
2. Does capability discovery run once per server connection, or per item? Milestone 8.
3. Does the server plugin endpoint need to be reachable before authentication completes? Affects where discovery is triggered.
4. Would upstream accept new Java in `FullDetailsFragment`, or require the addition in Kotlin? Affects Milestone 15.
5. Is polling acceptable for status updates, or is a WebSocket message required? `ApiClient` exposes `webSocket: SocketApi`, so both are technically available. Milestone 12.

---

## 8. Verification notes

Every line number and signature in this document was read directly from the cloned source at commit `394f869`, not recalled. The SDK `request` signature was read from `jellyfin-sdk-kotlin` sources rather than inferred from usage.
