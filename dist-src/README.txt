Elden Vins with more map variations
===================================

A merge of two ELDEN RING NIGHTREIGN mods into one Mod Engine 3 profile:
  - Elden Vins Nightreign (EV)
  - More Map Variations 2.1.8-hotfix3 & Weapons Mod (MMV)

EV's combat rework (movesets, enemy scaling, passives, boss swaps, forced Deep of
Night) is kept, and MMV's map variations, extra bosses and new weapons are added.
EV's scaling is also applied to MMV's enemies. MMV weapons that use a standard
moveset get EV's moveset for their weapon type. MMV weapons with unique movesets
keep their own. MERGE_REPORT.md lists every merge decision. merge-journal\ has
the detailed per-file decision tables.


Requirements
------------
- ELDEN RING NIGHTREIGN, game version 1.03.5 (regulation 10350000).
- Mod Engine 3 (me3): https://github.com/garyttierney/me3


Install
-------
1. Extract this folder anywhere. The profile uses only relative paths, so it
   does not have to be inside the game folder.
2. Launch "Elden Vins with more map variations.me3" with Mod Engine 3. You can
   double-click it after me3 is installed, or run:
     me3 launch -p "<this folder>\Elden Vins with more map variations.me3"
3. Do not run EV or MMV at the same time. This profile already contains both.


Online
------
The included server redirector (mod\ServerRedirector) connects the game to the
community server https://nightreign.fs-emu.net instead of the retail servers.
The custom shard is VINSMMV, so you are matched only with other players running
this merge. You can change these settings in
mod\ServerRedirector\cl_server_redirector.ini.

The shard does not check that everyone has the same game data. Compare the
co-op code in VERSION.txt with your partners before a run: if the codes
differ, bosses and damage desync (health bars that never go down, attacks that
hit far harder than they should). Everyone should build with the same builder
version.


Save file
---------
The redirector is set to the alternative save (CL_USE_ALT_SAVE=true). The game
uses NR0000.CL_SAVE, which is the same save file as Elden Vins Nightreign, and
does not touch your retail .sl2 save. Your EV progress carries over and is
shared with EV.
BACK UP YOUR SAVE FIRST: copy %APPDATA%\Nightreign\<SteamID>\ somewhere safe.


Forced Deep of Night (nighter.dll)
----------------------------------
EV's profile loads mod\dll\nighter.dll, which forces Deep of Night (config:
mod\dll\nighter.json). EV's download does not include nighter.dll, so it is not
included here either. Without it, me3 logs a "missing nighter.dll" warning and
the game runs normally without forced Deep of Night. To enable forced Deep of
Night, get nighter.dll and copy it into mod\dll\.


Included DLL mods
-----------------
- mod\ServerRedirector\cl_server_redirector.dll: Nightreign Server Redirector by
  Church Guard (the newer build shipped with MMV)
- mod\dll\custom_drop_fxrs.dll: custom drop effects for MMV's new weapon types
  (shipped with MMV; config custom_drop_fxrs.yaml)
- mod\dll\NightreignFPSFOV.dll: FPS/FOV tweak (shipped with EV; config
  mod\dll\NightreignFPSFOV\config.ini)


Credits
-------
All game content belongs to the original authors. This package only combines
their work:
  - Elden Vins Nightreign: the Elden Vins Nightreign author/team
  - More Map Variations & Weapons Mod: the More Map Variations author/team
  - Nightreign Server Redirector: Church Guard
  - custom_drop_fxrs, NightreignFPSFOV, nighter: their respective authors (as
    distributed with MMV and EV)
  - Mod Engine 3: garyttierney and contributors
  - Tooling used for the merge: SoulsFormats/Smithbox, HKLib, DSLuaDecompiler
    (katalash), DarkScript3, El-Fonz0's Nightreign HKS decompilation.
Please support the original mods on their own pages. Redistributing this merge
may require permission from the original authors.

The full list of merge decisions is in MERGE_REPORT.md.
