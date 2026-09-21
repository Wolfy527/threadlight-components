# ThreadLight Components architecture

ThreadLight Components contains the lightweight customer-facing contract used by
distributed prefabs. It does not contain creator tooling and does not depend on
ThreadLight Authoring or either Builder.

## Runtime and Editor responsibilities

- `Runtime` contains the customer prefab identity contract and compatibility
  types required by already distributed prefabs.
- The small Editor support assembly handles recognized legacy/fallback folder
  migration only.
- Customer support components remove their own editor-only state during play
  mode and upload. Creator inspectors, previews, setup windows, exporters, and
  active Live Mirroring behavior live in ThreadLight Authoring.

## Compatibility rules

- Keep serialized field names stable. Use `FormerlySerializedAs` when a rename
  is unavoidable.
- Keep component script `.meta` GUIDs stable.
- Keep managed-reference type names stable or provide an explicit migration.
- Preserve data created by a newer package version without rewriting it.

The fallback installer verifies ThreadLight UI `>=1.0.1 <2.0.0` and VRChat Avatars
`>=3.7.0 <4.0.0` before exposing fallback assemblies. Stable GUID and ownership
markers let it recover after its generated folder is moved, while registered VPM
packages remain authoritative. Missing, corrupt, conflicting, or incompatible
payloads retain rollback behavior and show one actionable editor message per
session.
An existing fallback whose compatibility GUID identity cannot be proven is left
unchanged, regardless of its reported version. Automatic replacement is limited
to recognized, GUID-compatible older fallbacks.

The customer Live Mirroring inspector presents the ordered pair decisions already
used by runtime evaluation. Same-object, nested, persistent-asset, cross-scene,
duplicate-target, and cycle cases have distinct decisions and guidance while
unsafe pairs remain paused. Damaged negative saved versions direct the user to an
unaffected copy; data from a newer version directs them to update Components.

Customer cleanup preflights the complete scene or upload root before removing any
authoring state. It refuses prefab assets, the cleanup root itself, holders outside
that root, missing scripts, and holders containing unrelated runtime components.
Dedicated holders still preserve and reparent creator children before removal.

## Prefab ID

`PrefabId` is the explicit handoff between a distributed customer prefab and
creator-side tools. Export translates the ThreadLight Authoring snapshot into this
customer contract. Resume translates it back without making either package a
compile-time dependency of the other. Its schema version describes the snapshot
container, while the stored Builder and module versions describe the data inside
it. A fingerprint detects partial or external changes before restoration begins.
