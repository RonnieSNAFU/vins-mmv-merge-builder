# Maintenance guide

This guide is for contributors working on the merge tool. For player instructions see `dist-src/builder/README.txt`.

## Dev-mode layout

When `<exe>\data\mod-manifest.json` is absent, `NRMerge.exe` runs in dev mode and uses the repository checkout as its
workspace. See the README's "Paths and environment variables" table for overrides.

| What | Where |
|---|---|
| Source mods (read-only inputs) | `<NightreignRoot>\ELDEN VINS NIGHTREIGN\mod`, `<NightreignRoot>\More Map Variations 2.1.8-hotfix3 & Weapons Mod\mod` |
| Vanilla extracts | `<repo>\vanilla\` (Nightreign), `<repo>\vanilla_er\` (Elden Ring), created by `NRMerge.exe extract-vanilla` |
| Work / output | `<repo>\work\`, `<repo>\out\` (`out\mod` = merged mod before delivery, `out\journal` = decision journals) |
| Delivered mod | `<NightreignRoot>\Elden Vins with more map variations\` |
| Smithbox metadata, Andre dictionaries | the `src/Smithbox` submodule |
| Merge rules | `merge\ai\*.lua.rules.json`; HKS rules live in `src\NRMerge\HksResolve.cs` |

None of these folders except `merge/` are committed (see `.gitignore`).

## Full pipeline (dev mode)

Run these from the build output folder:

```
for st in assemble regmerge mmvrewrite contentmerge emevdmerge msbmerge enemymerge playerscripts playermerge profile verify; do
  ./NRMerge.exe $st || { echo "failed at $st"; break; }; done
```

`verify` must print `VERIFY OK` and `introduced 0`, and it writes `out\verify.ok`. After that, `./NRMerge.exe deliver`
moves the result into the delivery folder. It refuses unless verify passed after the last change and the output folder
is empty.

Partial reruns: a change at or before `regmerge` requires rerunning every later stage. `playermerge` is idempotent (it
works from a regulation snapshot).

## Handling bug reports

Testers attach `VinsMMV-crash-report-<date>.zip`, made by `Collect Crash Report.bat`, which redacts user names and
Steam IDs.

- **Check versions first** (`system-info.txt`). The game must be 1.03.5 (exe 1.3.3.0), the mod version comes from
  `VERSION.txt`, and ME3 must be 0.10 or newer.
- **Crash on load / title screen**: look for ME3 log errors and missing files, then rerun `verify`.
- **Crash in a specific fight or map**: find the map or character ID in `MERGE_REPORT.md` and
  `merge-journal\{maps,events,enemies,ai}.tsv`, then find the ruling that produced it (`src/NRMerge/Resources/rulings.txt`).
- **Weapon/moveset problems**: check `player.tsv`, `binders-player.tsv` and the BehaviorParam_PC rows.
  `NRMerge.exe playercheck` reports clip-to-animation gaps that the merge introduced.
- **Balance**: check `ev-rules.tsv` (EV scaling applied to MMV content) and `regulation.tsv` (two-sided field decisions).

Every fix gets a failing xUnit test first (`tests\NRMerge.Tests`).

Analysis commands (`NRMerge.exe`): `msbshow`, `behprobe`, `behtest`, `animusage`, `clipparents`, `taejudges`, `judgeusers`,
`taefind`, `regdump`, `refscan`, `hkxtypes`, `hkxcompare`, `flverinfo`, `bnddump`.
Reference material: <https://www.soulsmodding.com/doku.php?id=ern-refmat:main>

## Cutting a builder release

1. Bump `<Version>` in `src\NRMerge\NRMerge.csproj`. If you added rulings, update `src\NRMerge\Resources\rulings.txt`
   (`export-rulings <ledger>` copies the `Ruling:` lines of a ledger file into it). The rulings are embedded into
   MERGE_REPORT.md.
2. Commit. `build-release.ps1` refuses uncommitted changes under src/tests/patches/merge/data/dist-src, because the
   release's `source\` folder comes from HEAD.
3. Run `powershell -ExecutionPolicy Bypass -File dist-src\builder\build-release.ps1`. The script:
   - checks the submodules (Smithbox @ cbd477a8, DSLuaDecompiler @ c27340ab) and that both patches are applied;
   - runs the tests and publishes;
   - stages the release and runs `NRMerge.exe selftest` with no .NET on PATH;
   - zips the result and runs the audit.
4. The audit can also run standalone: `dist-src\builder\audit.ps1 -Zip <zip>`, with `-NightreignRoot` and
   `-EldenRingGame` or the `NRMERGE_*` variables. It fails on:
   - game/mod file types;
   - backslash entry names;
   - a missing corpus folder;
   - any entry whose SHA-256 equals a file of either mod, the installed merged mod, the game folders, the vanilla
     extracts or the pinned downloads.
5. End-to-end check (about 20 GB of disk and 45 minutes):
   1. Unzip the release into a clean folder.
   2. Run `NRMerge.exe build --non-interactive --out <full> --cache-dir <cache>`.
   3. Run `NRMerge.exe compare-build <full>\mod <reference>\mod`. It must report 0 differences.
   4. Repeat with `--no-eldenring`.

**Licenses:** the binary links SoulsFormats (GPLv3), so the builder is GPLv3 and ships its source. When you add or update
a NuGet package, add its license to `dist-src\builder\licenses\` and a row to `THIRD-PARTY-NOTICES.txt`.

## When a mod, the game or a pinned file changes

- **EV or MMV update**:
  1. Put the new download in place, rebuild in dev mode and fix the merge.
  2. Regenerate the input manifest with `NRMerge.exe make-mod-manifest <evFolder> <mmvFolder>`, then check it with
     `NRMerge.exe check-mods`.

  The builder refuses old mod versions, so release a new builder version along with the manifest.
- **AI hand resolutions** (`merge\ai\<script>.lua.rules.json`): each rule names the side to start from for a
  conflicting function, that side's SHA-256 and a few line edits. If a mod changes such a function, the build stops with
  "hand resolution for <key> no longer applies". To fix it:
  1. Resolve the function by hand into `<key>.lua` files in a folder.
  2. Run `NRMerge.exe airules-make <script> <thatFolder>`.
  3. Commit the new rules.
- **HKS rules** (`src\NRMerge\HksResolve.cs`): the c0000.hks three-way merge must yield exactly 14 conflict hunks. Any
  other count stops the build with "update the HKS rules".
- **Game patch**:
  1. Update `GameCheck.cs`.
  2. Re-extract with `NRMerge.exe extract-vanilla`.
  3. Rebuild and re-verify.
  4. Run `NRMerge.exe make-vanilla-manifest`.
- **Pinned downloads** (`data\fetch.json`): change the commit in the URL and the SHA-256 together.
  `NRMerge.exe fetch <emptyDir>` proves both.
- **Elden Ring optional**: after any merge change, re-measure the no-ER variant (`--no-eldenring` build plus
  `compare-build`) and update the numbers in `dist-src\builder\README.txt`.

## Design decisions (binding for the merge output)

- EV's enemy scaling is applied in full to MMV's enemies.
- EV's save file `NR0000.CL_SAVE` is reused (no ME3 savefile key).
- Online play uses EV's custom server settings, with shard `VINSMMV`.
- EV's `nighter.dll` and `NightreignFPSFOV.dll` and MMV's `custom_drop_fxrs.dll` are kept, with one redirector (MMV's
  build).
- MMV weapons that use standard movesets get EV's movesets.

Every individual judgement call is recorded as a `Ruling:` line in `src/NRMerge/Resources/rulings.txt`.
