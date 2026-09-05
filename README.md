# ThreadLight Components

Lightweight customer support components for ThreadLight prefabs.

## Install

<a href="https://wolfy527.github.io/threadlight-components/?install=1">
  <img src=".github/assets/add-to-vcc-button.svg" alt="Add ThreadLight Components to VCC" width="132">
</a>

VCC repository: `https://wolfy527.github.io/threadlight-components/index.json`

Install `ThreadLight Components` through VCC.

A standalone Unity package is also available on the
[Releases](https://github.com/Wolfy527/threadlight-components/releases) page. It
keeps an installed VCC package, updates an older fallback, and does not
downgrade a newer fallback.

If an older Glizzy import has already left compiler errors inside
`Assets/Wolfy_527/~ Supporting Files/Prefab Components Fallback`, updating alone
cannot run Unity's cleanup code. Preserve that folder and its `.meta` file in
a backup outside `Assets` before reopening Unity. Do this only for the obsolete
Glizzy fallback; prefabs that still reference its scripts need migration first.

## Includes

- The lightweight customer prefab identity contract
- Real-time target mirroring, shared scaling, and Scene-view ghost previews for customer prefab setup
- A Live Mirroring inspector compatible with already distributed prefabs
- Runtime and upload cleanup for customer-side support state
- A guarded fallback installer for customers who do not use VCC

Creator-side generation windows and exporters are supplied by
ThreadLight Authoring through ThreadLight Builder or ThreadLight Mirroring. They
are not part of this customer package, and ThreadLight Components does not depend
on ThreadLight Authoring.

Customer and creator tools use the same ThreadLight UI package. VCC installs
this dependency automatically; the generated fallback installer includes it too.

## Requirements

- Unity 2022.3
- VRChat SDK - Avatars 3.7 or newer within 3.x

## License

See [LICENSE.md](LICENSE.md).

Created by WolfyVR.
