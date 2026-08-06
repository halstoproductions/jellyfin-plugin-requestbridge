# Pull request checklist

Two upstream targets, two sets of reviewers, two standards. Work through the section that applies.

---

## Both targets

- [ ] Builds clean with zero warnings
- [ ] Tests pass, and new behaviour has tests
- [ ] New tests were checked for teeth: break the code deliberately, confirm the right test fails
- [ ] No product name outside a provider implementation
- [ ] No conditional branching on `providerDisplayName`
- [ ] No state added without evidence a provider can determine it
- [ ] No progress percentages, estimates, or interpolated states
- [ ] Absence still behaves as absence: no plugin, no provider, and unreachable provider each show the user nothing
- [ ] Commit messages explain why
- [ ] No credential, key, or host name committed

---

## Server plugin, to the plugin catalog

### Correctness

- [ ] Endpoints use `[Authorize]` with no policy. Requiring elevation makes the feature unusable by the users it exists for.
- [ ] Every `ProviderErrorCode` maps to a deliberate HTTP status
- [ ] Provider wording never appears in a response body
- [ ] Failures are logged with enough detail for an administrator to diagnose them, since the response deliberately says little
- [ ] `targetAbi` in `meta.json` is the oldest minor version whose API is actually used, not the newest patch available

### Compatibility

- [ ] Compiled against the floor declared in `targetAbi`, so the compiler enforces the compatibility claim
- [ ] Additive wire changes did not bump `apiVersion`
- [ ] Breaking wire changes did bump it, and the client rules in `docs/capabilities.md` still hold
- [ ] Serialisation still produces the casing clients actually receive. This is not the casing in most documentation.

### Behaviour under failure

- [ ] Provider unreachable returns a neutral error, and the capabilities endpoint still answers
- [ ] Malformed configuration reports a configuration problem rather than throwing
- [ ] A duplicate request is accepted rather than erroring

### Ready to install

- [ ] Version bumped in both `meta.json` and the project file
- [ ] Verified on a real server: plugin loads, endpoints route, configuration page renders
- [ ] Verified against a real provider, not only a fake

---

## Android TV client, to `jellyfin-androidtv`

Assume the reviewers have never read this repository. The change has to justify itself on its own.

### Scope

- [ ] The diff is as small as the feature allows
- [ ] Nothing outside the feature was reformatted, renamed, or "tidied"
- [ ] Changes to shared components are justified in the pull request body, because reviewers will ask
- [ ] New code follows the surrounding conventions, including Kotlin where the neighbours are Kotlin

### Behaviour on a server without the plugin

This is the first thing a reviewer will check, and rightly so.

- [ ] A 404 during discovery is recorded and nothing is shown
- [ ] Nothing is logged at warning level or above for an absent plugin
- [ ] Discovery never blocks navigation or startup
- [ ] Screens are visually identical to the unmodified client
- [ ] Verified by actually removing the plugin, not by reasoning about it

### Correctness

- [ ] Discovery runs once per server connection, not per item
- [ ] Polling only covers items being fulfilled, and stops when there are none
- [ ] Poll interval is no faster than fifteen seconds
- [ ] An unrecognised state is treated as work in progress, never as available and never as an error
- [ ] A failed action leaves state untouched rather than assuming success
- [ ] The user is told when an action fails

### Interface

- [ ] Focus and scroll position survive a state change
- [ ] Every string is in `strings.xml`
- [ ] Cards behave like their neighbours on a remote control

---

## Before pressing submit

- [ ] The pull request body explains the problem before the solution
- [ ] Weak points are named by the author rather than left for a reviewer to find
- [ ] Anything unverified is described as unverified
- [ ] Screenshots or logs are attached for anything visual

That third point matters more than it looks. A reviewer who finds an unmentioned gap starts doubting everything else in the diff.
