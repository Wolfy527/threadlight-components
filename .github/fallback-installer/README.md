# Fallback Installer

Build:

```powershell
./.github/scripts/build-fallback-installer.ps1
```

The generated installer Unity package is for product authoring. Import it into
a project that has ThreadLight Builder installed, then include this folder in the
product export:

```text
Assets/Threadlight/Components/Installer
```

Do not include either Components package folder in the product export:

```text
Packages/com.wolfyvr.threadlight.components
Packages/com.wolfyvr.threadlight.components.fallback
```

ThreadLight Builder keeps the installer staged. In a customer project, the bootstrap
keeps the VPM package when present. Otherwise, it safely moves recognized legacy
scripts aside and installs or updates the fallback under Supporting Files without
downgrading a newer version. VCC removes that fallback before installing the
managed package. Temporary installer files are removed when Unity closes.
