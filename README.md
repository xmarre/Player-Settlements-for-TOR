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

CI produces reviewable build artifacts. Tag-driven GitHub release publishing is
implemented, yet remains gated until the repository records written modification
and redistribution permission. `RELEASE_PERMISSION.md` must contain
`Status: approved`, and the repository Actions variable `RELEASE_APPROVED` must
be set to `true`. A tag such as `v7.6.12` then rebuilds from the pinned source,
publishes the ZIP and checksum, and creates the GitHub release.

## 7.6.12 siege-AI fix

Bannerlord's military AI enumerates a faction's settlements, then uses the
fortification-neighbour cache to calculate siege-front value. Dynamically created
fortifications were registered in clan/faction ownership collections in 7.6.11,
yet remained absent from, or incompletely represented in, the precomputed
neighbour cache. Missing native neighbour edges can produce a zero or incomplete
siege-front score and exclude the settlement before normal target scoring.

7.6.12 incrementally runs Bannerlord's own navigation-specific candidate filter,
neighbour test, and symmetric edge-registration method for each custom
fortification after a save finishes loading and immediately after construction.
It reconciles every missing native cache edge, performs no campaign tick scan,
and does not alter ownership of conquered or transferred settlements.
