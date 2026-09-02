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

## Prefab ID

`PrefabId` is the explicit handoff between a distributed customer prefab and
creator-side tools. Export translates the ThreadLight Authoring snapshot into this
customer contract. Resume translates it back without making either package a
compile-time dependency of the other. Its schema version describes the snapshot
container, while the stored Builder and module versions describe the data inside
it. A fingerprint detects partial or external changes before restoration begins.
