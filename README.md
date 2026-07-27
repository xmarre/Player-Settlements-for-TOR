# Player Settlements for The Old Realms

Maintained Bannerlord 1.3.15 / The Old Realms 1.16 build of Player Settlement.

## Repository model

The upstream source is pinned to the immutable commit in `UPSTREAM_COMMIT`.
`patches/0001-tor-7.6.11-base.patch` contains the upstream deltas that are not maintained as complete files. `overlays/BannerlordPlayerSettlement/` contains the maintained 7.6.11 source-file baselines plus the 7.6.12 strategic siege-integration source. Patches `0002` through `0009` are applied in filename order after the overlays and record focused, independently reviewable corrections for placement, saving, ownership-state invariants, deep-edit bounds, reflection compatibility, visual finalizers, and removal of obsolete ghost-marker paths.

All three runtime DLLs are built from source in CI. Generated DLLs, PDBs, diagnostics, and release archives are excluded from Git history. `SOURCE_SHA256SUMS.txt` must list every maintained `.cs`, `.csproj`, and `.patch` input; the build fails on an unlisted file, stale entry, or hash mismatch.

## Build

Requirements: Git, PowerShell 7, and .NET SDK 8.

```powershell
./scripts/build.ps1
```

The command verifies source-manifest completeness and SHA-256 values before checkout, resolves the pinned upstream commit, applies the base patch, copies maintained overlays, applies all post-overlay patches in filename order, builds all projects, validates module XML, and writes the deterministic installable archive and checksum to `dist/`.

## Release

CI produces reviewable build artifacts. Tag-driven GitHub release publishing is implemented, yet remains gated until the repository records written modification and redistribution permission. `RELEASE_PERMISSION.md` must contain `Status: approved`, and the repository Actions variable `RELEASE_APPROVED` must be set to `true`.

A tag such as `v7.6.12` then rebuilds from pinned and hashed source, publishes the ZIP and checksum, and creates the GitHub release. The write-capable release action is pinned to an immutable commit.

## 7.6.12 siege-AI fix

Bannerlord's military AI enumerates faction settlements, then uses the fortification-neighbour cache to calculate siege-front value. Dynamically created fortifications were registered in clan and faction ownership collections in 7.6.11, yet remained absent from, or incompletely represented in, the precomputed neighbour cache. Missing native neighbour edges can produce a zero or incomplete siege-front score and exclude the settlement before normal target scoring.

7.6.12 incrementally runs Bannerlord's own navigation-specific candidate filter, neighbour test, and symmetric edge-registration method for each custom fortification after a save finishes loading and immediately after construction. It reconciles every missing native cache edge, performs no campaign-tick scan, and does not alter ownership of conquered or transferred settlements.

## Placement and save hardening

Gate and port placement use the cursor directly and no longer depend on a marker prefab. Placement commits only from the current valid mouse-release frame. Obsolete marker state and retry methods have been removed. The save path retains localised notifications, avoids the unstable in-process reload, clears pending state when the campaign session ends, and unlocks a missing save callback after a five-minute monotonic timeout.

Additional invariant fixes preserve castle collections, settlement overwrite types, deep-edit child bounds, reflection fallbacks, and Harmony finalizer signatures.
