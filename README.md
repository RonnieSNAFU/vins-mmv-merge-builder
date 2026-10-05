# VinsMMV Merge Builder

Builds **"Elden Vins with more map variations"**, a merged mod for **ELDEN RING NIGHTREIGN**, on your own PC. It combines:

- **Elden Vins Nightreign** (EV): <https://www.nexusmods.com/eldenringnightreign/mods/287>
- **More Map Variations 2.1.8-hotfix3 & Weapons** (MMV): <https://www.nexusmods.com/eldenringnightreign/mods/578>

The output is one [Mod Engine 3](https://github.com/garyttierney/me3) profile.

> **This repository ships no game files and no mod files.** It holds only the merge tool's source code, tests, build
> scripts, merge rules and manifests (file names, sizes and SHA-256 hashes). The builder reads the two mods **you download
> yourself from their official Nexus pages** plus your own game install, and it writes the merged mod on your PC. Please
> download both mods from the links above and support their authors.

## How it works

`NRMerge.exe build` runs a three-way merge of both mods against the vanilla game files. It extracts the vanilla files
from your Nightreign (and, optionally, Elden Ring) install, using the hash manifest in `data/vanilla-manifest.tsv`. It
covers:

- regulation params, including EV's scaling rules applied to MMV's content and ID remapping for colliding rows;
- binders, TAE animations, Havok behaviors, FLVER parts and FMG texts;
- MSB maps, EMEVD event scripts, enemy AI Lua and the player HKS script.

The builder checks both mod downloads against `data/mod-manifest.json`, so it only runs on the mod versions the merge
was verified for. It verifies its own output before writing anything. Every decision is journaled into
`merge-journal/` and `MERGE_REPORT.md` next to the merged mod.

## For players: using a release

1. Download Elden Vins Nightreign (Nexus mod 287) and More Map Variations 2.1.8-hotfix3 & Weapons (Nexus mod 578) from
   their Nexus pages. You can leave them as archives or extract them.
2. Download `VinsMMV-Merge-Builder-<version>.zip` from the
   [latest release](https://github.com/RonnieSNAFU/vins-mmv-merge-builder/releases/latest), extract it anywhere and double-click **`Build Merged Mod.bat`**.
3. The builder finds Steam, Nightreign, Elden Ring and both mods by itself, or asks you for them. It builds the merged
   mod in about 20 minutes and offers to launch the game through Mod Engine 3.

Requirements: Windows 10/11 x64, ELDEN RING NIGHTREIGN 1.03.5 (Steam), both mods at the versions above, about 12 GB of
free disk space, an internet connection during the first build and Mod Engine 3. Elden Ring 1.16.1 is optional:
without it, the builder replays the verified build's Elden-Ring-based decisions from `data/noer-patches`, so the game
data is the same either way.

The player guide in [`dist-src/builder/README.txt`](dist-src/builder/README.txt) covers options, offline builds and
troubleshooting. **Back up your save first** (`%APPDATA%\Nightreign\<SteamID>\`).

## Building from source

Requirements:

- Windows x64 and the **.NET 10 SDK** (the SDK, not just the runtime). Download the x64 installer from Microsoft's
  official page: <https://dotnet.microsoft.com/download/dotnet/10.0>. You can also install it from a terminal with
  `winget install Microsoft.DotNet.SDK.10`. Check it with `dotnet --version`, which should print 10.0.x. If `dotnet`
  isn't on your PATH, `build-release.ps1` also accepts an SDK in `<repo>\dotnet\`.
- git, for the submodules (<https://git-scm.com/download/win>).

Players using a release don't need any of this: the release exe is self-contained.

```powershell
git clone --recurse-submodules https://github.com/RonnieSNAFU/vins-mmv-merge-builder.git
cd vins-mmv-merge-builder
# apply our patches to the pinned upstream checkouts (once, after cloning or updating submodules)
git -C src/Smithbox apply ../../patches/smithbox-hklib-nightreign-cmsg.patch
git -C src/DSLuaDecompiler apply ../../patches/dslua-net10.patch

dotnet build src/NRMerge -c Release
dotnet test tests/NRMerge.Tests
```

### Dependencies

| Dependency | How it is obtained |
|---|---|
| [Smithbox](https://github.com/vawser/Smithbox) @ `cbd477a8` (SoulsFormats, Andre, HKLib) | git submodule `src/Smithbox`, plus `patches/smithbox-hklib-nightreign-cmsg.patch` (adds a Nightreign Havok member to HKLib) |
| [DSLuaDecompiler](https://github.com/katalash/DSLuaDecompiler) @ `c27340ab` | git submodule `src/DSLuaDecompiler`, plus `patches/dslua-net10.patch` (retargets the csproj files to .NET 10) |
| DrSwizzler 1.1.1 (a SoulsFormats dependency) | **not in this repository.** `dotnet restore` downloads it from nuget.org because Smithbox's SoulsFormats references it. The package publishes no license, see `dist-src/builder/licenses/DrSwizzler-NOTICE.txt`. |
| SharpCompress and the other NuGet packages | restored by `dotnet restore` from nuget.org |
| Nightreign 1.03.4 `regulation.bin`, the `c0000.hks` merge base, `nr-common.emedf.json` | downloaded **at build time** from GitHub at pinned commits and checked by SHA-256 (`data/fetch.json`). These files aren't redistributable, so they are never committed. |

### Tests

`dotnet test tests/NRMerge.Tests` runs the unit tests, which use synthetic data. Some tests compare against real mod or
game files, or against the maintainer's work folders (`work/`, `analysis/`, an installed merged mod). Those tests skip
themselves, or pass trivially, when their inputs are absent. The EMEVD merge tests need DarkScript3's
`nr-common.emedf.json`, which is not redistributable. `NRMerge.exe fetch <dir>` downloads it (pinned and hash-checked);
copy it to `tools/darkscript/nr-common.emedf.json` to enable those tests.

### Paths and environment variables

The default ("dev") configuration finds its inputs like this:

| Variable | Meaning | Default |
|---|---|---|
| `NRMERGE_REPO` | repository root (work folders `vanilla/`, `out/`, `work/` live here) | nearest parent of the exe holding `src/NRMerge/NRMerge.csproj` |
| `NRMERGE_NIGHTREIGN_ROOT` | `<SteamLibrary>\steamapps\common\ELDEN RING NIGHTREIGN` (holds `Game` and the mod folders) | found through Steam |
| `NRMERGE_ELDENRING_GAME` | `<SteamLibrary>\steamapps\common\ELDEN RING\Game` | found through Steam |

The `build` command, which is what players run, finds everything itself or takes `--ev`, `--mmv`, `--nightreign`,
`--eldenring` and `--out`.

### Cutting a release

`powershell -ExecutionPolicy Bypass -File dist-src\builder\build-release.ps1` checks the pinned submodules and patches,
runs the tests and publishes a self-contained single-file `NRMerge.exe`. It then stages the release (exe, Smithbox
metadata, manifests, merge rules, licenses, source) and runs `dist-src\builder\audit.ps1`. The audit fails if any file
in the zip matches a game or mod file by hash or by type. See [`docs/MAINTENANCE.md`](docs/MAINTENANCE.md).

## Repository layout

| Path | Contents |
|---|---|
| `src/NRMerge` | the merge tool and builder (C#) |
| `src/LuaNorm` | small Lua normalisation helper |
| `tests/NRMerge.Tests` | xUnit tests |
| `merge/ai` | hand resolutions for conflicting AI functions. For each function they record which side wins, that side's SHA-256 and a few edited lines. |
| `data` | mod/vanilla manifests (names, sizes, hashes only) and the pinned-download catalog |
| `patches` | our patches to the Smithbox and DSLuaDecompiler submodules |
| `dist-src` | release scripts, player README, crash-report collector, license texts, Nexus page text |

## License and credits

The builder is free software under the **GNU General Public License v3** ([LICENSE](LICENSE)), because it links
Smithbox's SoulsFormats (GPLv3). Third-party components and their licenses are listed in
[`dist-src/builder/licenses/THIRD-PARTY-NOTICES.txt`](dist-src/builder/licenses/THIRD-PARTY-NOTICES.txt).

- Elden Vins Nightreign: its authors ([Nexus mod 287](https://www.nexusmods.com/eldenringnightreign/mods/287)).
- More Map Variations 2.1.8-hotfix3 & Weapons: its authors ([Nexus mod 578](https://www.nexusmods.com/eldenringnightreign/mods/578)).
- Smithbox, SoulsFormats, HKLib: vawser, Katalash, Meowmaritus, The12thAvenger and contributors.
- DSLuaDecompiler: katalash. Lua: Tecgraf, PUC-Rio.
- c0000.hks base: El-Fonz0. EMEDF: DarkScript3 (AinTunez).
- Mod Engine 3: garyttierney and contributors.

ELDEN RING NIGHTREIGN and ELDEN RING are trademarks of FromSoftware / Bandai Namco. This project is not affiliated with
them or with the authors of the two mods.
