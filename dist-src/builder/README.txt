VinsMMV Merge Builder
=====================

Builds "Elden Vins with more map variations" on your own PC: one Mod Engine 3
profile for ELDEN RING NIGHTREIGN that combines
  - Elden Vins Nightreign (EV), Nexus mod 287
  - More Map Variations 2.1.8-hotfix3 & Weapons (MMV), Nexus mod 578

This download contains no files from either mod and no game files. The builder
reads the two mods you downloaded yourself and your own game install, and
writes the merged mod next to them. The result is the same, byte for byte, as
the merge we tested.


What you need
-------------
- Windows 10 or 11 (64-bit). Nothing else to install: no .NET, Python or git.
- ELDEN RING NIGHTREIGN, game version 1.03.5, installed through Steam.
- Elden Vins Nightreign, from its Nexus page (mod 287).
- More Map Variations 2.1.8-hotfix3 & Weapons, from its Nexus page (mod 578).
- About 12 GB of free disk space on the drive where the merged mod goes, and
  20 to 30 minutes.
- An internet connection during the build (three small files are downloaded
  from GitHub, see "Downloads during the build").
- Optional: ELDEN RING 1.16.1 installed through Steam. See "Without Elden
  Ring" below.
- Mod Engine 3 (me3) to play. If you do not have it, the builder offers to
  install it for you.


Steps
-----
1. Download Elden Vins Nightreign and More Map Variations 2.1.8-hotfix3 &
   Weapons from their Nexus pages. You can leave them as downloaded archives
   (.zip, .7z or .rar) in your Downloads folder, or extract them anywhere,
   for example into the Nightreign game folder.
2. Download this builder and extract it to any folder.
3. Double-click "Build Merged Mod.bat" (or NRMerge.exe).
   The builder finds Steam, Nightreign, Elden Ring and both mod downloads by
   itself. It looks in the Nightreign install folder, your Downloads, Desktop
   and Documents folders and every Steam library. If it cannot decide, a
   window opens and asks you to pick the folder or archive.
   Both mods are checked against the exact versions this merge was made for.
   If a mod has been updated since, the builder stops and tells you which one.
4. If Mod Engine 3 is not installed, the builder asks whether to download and
   run the official me3 installer (from github.com/garyttierney/me3). Answer
   y to install it, or n to install it yourself later.
5. Wait for the build to finish (about 20 minutes). It checks its own result
   ("VERIFY OK") before writing anything to the output folder.
6. At the end the builder shows and opens the output folder, by default
     <Nightreign install>\Elden Vins with more map variations
   and asks whether to launch the game with the merged profile now.
   Later, launch "Elden Vins with more map variations.me3" from that folder
   with Mod Engine 3 (double-click it, or use me3 launch).

Back up your save first: copy %APPDATA%\Nightreign\<SteamID>\ somewhere safe.
The builder also copies the EV save (NR0000.CL_SAVE) into a
backup-before-merge folder next to it during the build. The output folder has
its own README.txt about online play, saves and the included DLL mods.


Without Elden Ring
------------------
Elden Vins ports content from ELDEN RING (enemies, animations, behaviours,
regulation rows). With Elden Ring 1.16.1 installed, the merge uses those
Elden Ring originals as the common base and the result is identical to the
tested merge.

Without Elden Ring (or with another Elden Ring version) the builder still
produces the same game data. The merge first falls back where it needed an
Elden Ring original, then replays the tested merge's decisions for those
files from data\noer-patches. The patches copy from your own Elden Vins, More
Map Variations and Nightreign files, so they contain almost no data of their
own. Every replayed file is checked against the tested merge by SHA-256.
The only remaining difference is compression: Elden Ring's Oodle library is
not available, so some files are compressed with Nightreign's. The game reads
the same data, and the co-op code (below) is the same.


Playing co-op
-------------
Everyone in a co-op group must run the same game data. If one player's data
differs (another builder version, a hand-edited file), bosses and damage
desync: health bars that never go down, attacks that hit far harder than
they should. Each build prints a co-op code at the end and writes it into
VERSION.txt, for example
  Co-op code: 3F2A-9C1B
Compare codes before you start. To check an existing merged mod folder, run
  NRMerge.exe coop-code "<merged mod folder>"
Builds made with and without Elden Ring by the same builder version have the
same code.


Downloads during the build
--------------------------
These files are not ours to redistribute, so the builder downloads them from
GitHub at fixed commits and checks each one against a SHA-256 hash:
  - Nightreign 1.03.4 regulation.bin (Smithbox repository): the base Elden
    Vins was made from.
  - c0000.hks (El-Fonz0/EldenRingNightreignHKS): base of the player script.
  - nr-common.emedf.json (DarkScript3): event script definitions.
They are kept in %LOCALAPPDATA%\VinsMMV-Merge-Builder\cache, so a second
build needs no internet. Offline PC: copy that cache folder from another PC,
or put the files in a folder and pass --cache-dir "<folder>". The error
message names the exact URLs.


Options
-------
Normally you need none. Open a command prompt in this folder and run
  NRMerge.exe build [options]
or pass the options to "Build Merged Mod.bat".
  --ev <folder|archive>     Elden Vins download (folder with the .me3, its
                            mod folder, or the .zip/.7z/.rar)
  --mmv <folder|archive>    More Map Variations download
  --nightreign <GameDir>    Nightreign "Game" folder
  --eldenring <GameDir>     Elden Ring "Game" folder
  --no-eldenring            build without Elden Ring even if it is installed
  --out <dir>               output folder (must be new or empty)
  --cache-dir <dir>         folder for the downloaded files (offline use)
  --force                   build even if a mod or game version does not
                            match (the result may be broken)
  --keep-work               keep the work folder <out>\_build
  --non-interactive         no questions, no windows, no installer, no launch
NRMerge.exe selftest checks that the builder runs on this PC without touching
any game or mod file.


Troubleshooting
---------------
- "expected version": one of the mods (or the game) is not the version this
  merge was made for. Download the version named in the message.
- "swapped": the two mod folders were given the wrong way round; the builder
  fixes this itself and tells you.
- "output folder is not empty": choose a new folder with --out, or delete the
  previous merged build first.
- Antivirus: NRMerge.exe is a self-extracting .NET program. On first start it
  unpacks two helper DLLs (lua502.dll, libzstd.dll) to %TEMP%\.net\NRMerge.


Licence and credits
-------------------
The builder is free software under the GNU General Public License v3
(LICENSE.txt). Its full source code is in the source\ folder; SOURCES.txt
names the upstream commits. licenses\THIRD-PARTY-NOTICES.txt lists every
component and its licence.
  - Elden Vins Nightreign: its authors (Nexus mod 287).
  - More Map Variations 2.1.8-hotfix3 & Weapons: its authors (Nexus mod 578).
  - Smithbox, SoulsFormats, HKLib: vawser, Katalash, Meowmaritus,
    The12thAvenger and contributors.
  - DSLuaDecompiler: katalash. Lua: Tecgraf, PUC-Rio.
  - c0000.hks base: El-Fonz0. EMEDF: DarkScript3 (AinTunez).
  - Mod Engine 3: garyttierney and contributors.
ELDEN RING NIGHTREIGN and ELDEN RING are trademarks of FromSoftware / Bandai
Namco. This builder is not affiliated with them.
