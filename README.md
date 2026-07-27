# Player Settlements for The Old Realms

Maintained Bannerlord 1.3.15 / The Old Realms 1.16 build of Player Settlement.

## Repository model

The upstream source is pinned to the immutable commit in `UPSTREAM_COMMIT`.
`patches/0001-tor-7.6.11.patch` contains the established ToR and stability changes.
`overlays/` contains source files maintained directly in this repository,
including the 7.6.12 strategic siege integration fix. All three runtime DLLs are
built from source in CI. Generated DLLs, PDBs, diagnostics, and release archives
are intentionally excluded from Git history.

## Build

Requirements: Git, PowerShell 7, and .NET SDK 8.

```powershell
./scripts/build.ps1
```

The command verifies source hashes, checks out the pinned upstream commit,
applies the maintained patch and overlays, builds all projects, validates module
XML, and writes the installable archive and SHA-256 file to `dist/`.

## Release

Push a tag such as `v7.6.12`. The release workflow rebuilds from the pinned
source, publishes the ZIP and checksum, and creates the GitHub release.

## 7.6.12 siege-AI fix

Bannerlord's military AI enumerates a faction's settlements, then uses the
fortification-neighbour cache to calculate siege-front value. Dynamically created
fortifications were registered in clan/faction ownership collections in 7.6.11,
yet remained absent from the precomputed neighbour cache. An empty neighbour list
produces a zero siege score, excluding the settlement before target scoring.

7.6.12 incrementally runs Bannerlord's own neighbour test for each missing custom
fortification after a save finishes loading and immediately after construction.
It adds only the missing native cache edges, performs no campaign tick scan, and
does not alter ownership of conquered or transferred settlements.
