# Quiver Library for Playnite

A [Playnite](https://playnite.link) library extension that imports the apps from
[Quiver Launcher](https://github.com/tgeorgiadis/quiver-launcher) (recompilations and PC ports)
into your Playnite library, as a library named **Recomps** with **Quiver Launcher** as the games' source.

Quiver stays in charge of everything that touches the files: Playnite only shows the
install status and starts the games.

## Features

- **Import**: only the apps installed in Quiver are added to Playnite (same install rules
  Quiver itself uses) — install new games from Quiver Launcher, then update the game library.
  A game uninstalled later stays in Playnite as not installed, keeping its play time and
  stats, and can be reinstalled from Playnite. The status is refreshed every time Playnite
  updates its game library.
- **Install / Uninstall**: Playnite's buttons run Quiver in the background
  (`--download` / `--uninstall`). Quiver does the work; Playnite only updates the status.
- **Play**: starts the program that runs the game directly, so play time is tracked by
  Playnite. Many ports also ship a launcher or settings tool, a second renderer or an
  editor: those become the game's other actions (game menu), for example **Setup**.
  See [Executables database](#executables-database).
- **Last played**: Playnite's last played time and Quiver's (`LastPlayed.txt`, used by
  its "Last played" sort) are kept in sync, the most recent one winning: starting a game
  from Playnite updates Quiver, and games started from Quiver update Playnite on the next
  library update. Play time and play count stay Playnite's own (Quiver has none). Can be
  turned off in the settings.
- **Games installed by their own installer**: some ports ship an installer or builder that
  puts the game in a folder of your choice (Tekken 6, Tekken Tag Tournament 2, Burnout
  Revenge, Super Mario Galaxy, ...). If you install the game inside the app's folder (for
  example `<app>\game\`), Play finds it there; the installer stays available as **Setup**.
  Anywhere else, pick the game's program once with **Choose game executable...**.
- **Game menu** (right-click a game → **Recomps (Quiver Launcher)**): **Open saves folder**,
  **Choose game executable...** (any program becomes Play; inside the app's folder it is
  stored relative to it, so moving Quiver's library keeps it), **Use the default game
  executable** and, with the experimental achievements setting on, **Export achievements for
  Playnite Achievements (experimental)...**.
- **Portable installs** (setting, off by default): creates the file that makes each app keep
  its saves and settings in its own folder instead of AppData (usually `portable.txt` next
  to its program). Apps that already have data in their usual folder are left alone, and
  you are told once.
- **Save backups with Ludusavi** (setting, off by default): for the
  [Ludusavi extension for Playnite](https://github.com/mtkennerly/ludusavi-playnite), which
  doesn't know these games. On every library update the extension writes a Ludusavi
  secondary manifest listing each game by its Playnite name with its save folders (portable
  and AppData), and adds it once to Ludusavi's `config.yaml` (keeping `config.yaml.bak`).
  The Ludusavi extension then backs them up when you close each game, to the folder set in
  its settings. Only games whose save folders are known are included.
- **Achievements for Playnite Achievements** (setting, **experimental**, off by default; not
  fully implemented yet): for the [Playnite Achievements](https://github.com/justin-delano/PlayniteAchievements)
  extension, which can't read these games' achievements and has no way for other extensions
  to add them. For the apps whose achievement files are understood (today only Bloodborne's
  trophies), the game menu gets **Export achievements for Playnite Achievements**: it writes
  a CSV with every achievement (name, description, trophy type, hidden, icon) and the
  unlocked ones with their date, in the extension's data folder (`achievements\<game>.csv`),
  and the CSV is rewritten every time the game closes. Import it yourself in Playnite
  Achievements' achievement editor for that game, again after unlocking more (rows are
  matched by ID, nothing is duplicated). See [Achievements](#achievements).
- **Update**: main menu → **Recomps (Quiver Launcher) → Update all Quiver apps** runs `QuiverLauncher --update`.
- Games that Quiver lists more than once (different ports of the same game) get the
  project name added, for example *Mario Kart 64 (SpaghettiKart)*.
- English and Spanish interface.

## Requirements

- Playnite 10 (SDK 6.16 or newer).
- Quiver Launcher for Windows — the portable version, or the regular install.

## Installation

1. Download `QuiverLibrary_<version>.pext` from the releases page.
2. Double-click it (or drag it onto Playnite) and restart Playnite.
3. Open **Add-ons → Extension settings → Libraries → Recomps** and set the
   **Quiver Launcher folder**: the folder where you unzipped the portable Quiver (the one
   holding `QuiverLauncher.exe`, `Update.exe` and `current\`). The settings page shows
   whether Quiver and its library were found. Leave it empty to use Quiver's default
   install location (`%LocalAppData%\QuiverLauncher`).
4. Update the game library (**Main menu → Update Game Library → Recomps**).

## Main menu (Recomps (Quiver Launcher))

| Item | What it does |
|---|---|
| Install from Quiver library... | Searchable list of the apps in Quiver's library that aren't installed; installs the chosen one with `QuiverLauncher --download` and adds it to Playnite. Apps only in Quiver's App Catalog must be added to its library from Quiver first. |
| Update all Quiver apps | Runs `QuiverLauncher --update` and refreshes the install status. |
| Refresh install status | Re-reads Quiver's library without importing new games. |
| Open Quiver Launcher | Opens Quiver. |
| Settings... | Opens the extension settings. |

## Notes

- Games are started without going through Quiver, so they are not updated before
  launching. Use **Update all Quiver apps** from time to time.
- If a game has several executables, no database entry, and none of them can be told
  apart from the others, choose its program with **Choose game executable...** (or open it
  once from Quiver and choose the executable there).
- Avoid installing or uninstalling the same app from Quiver's window and from Playnite at
  the same time.

## Executables database

Quiver keeps a single executable per app. The extension knows more about the apps that
ship several programs, from `src/QuiverLibrary.Core/Executables/executables.json`: which
one starts the game (with its arguments, e.g. `--play` for launchers that can skip their
window) and which ones are offered as other actions. Every entry names its source,
usually the project's README. The executable picked in Quiver doesn't override an entry;
one chosen from the game menu does.

- `file` is relative to the app's folder; its file name and folder names may contain `*`/`?`
  (the highest version wins). With `"searchSubfolders": true` it is a file name looked for in every
  subfolder, the shallowest one winning: for games whose installer lets you choose where
  they go.
- `arguments` may use environment variables (`%LOCALAPPDATA%`), `{AppDir}` (the app's folder)
  and `{ExeDir}` (the program's folder). The play program may also take `environment`, a list of
  `name`/`value` variables expanded the same way, for runtimes that need what their own launcher
  sets (Crash of the Titans' MojoRecomp runtime), and `"hideConsole": true` for console programs
  that open the game window themselves; the extension then starts that process itself.
- The play program may also take `launch`, a program relative to `file`'s folder that is started
  instead (the engine behind a launcher, with the launcher's arguments; `file` still marks where
  the game is, and is started as is while `launch` isn't there), `"borderless": true` to turn the
  game window into a borderless one covering its monitor (when the game's own full screen option
  doesn't work), and `{XboxLanguage}` in arguments and `environment`: Playnite's language as an
  Xbox 360 language number (ReXGlue's `REX_USER_LANGUAGE`).
- `{GamesDir}` is the shared games folder: the "Games folder" setting (General tab) or, when it is empty,
  `games` next to Quiver's `Apps` folder (`<Quiver>\games`): for games whose compiler or installer refuses a folder inside the app's
  own. It may start the play program's `file`, and be used in arguments, `environment`,
  `userData.local`, `achievements.file`, `setup.built` and `setup.defaultInstallDir`.
- `userData` says where the app keeps its saves: `portable` (the marker file's name,
  `builtin` when it always uses its own folder, or `none`), `appData` (environment
  variables allowed), `local` (relative to the app's folder) and `saves` (inside both;
  several folders or files separated by `;`). It feeds **Open saves folder**, portable
  installs and the Ludusavi backups.
- `achievements` says whether the app has achievements: `kind` (`rexglue`, `retroachievements`,
  `ps4trophy`, `custom`, or `none` when checked and there are none), `file` (where it records
  the unlocks, relative to the app's folder or with environment variables; several places
  separated by `;`, the one in use first) and `retroAchievementsId` (the game on
  retroachievements.org). An entry may hold only `userData` or `achievements`: its programs
  are then chosen automatically.

Apps without an entry: the play action is the only candidate left after dropping
setup/uninstall/crash helpers and then launchers (`*launcher*`, `*config*`, ...); the
executable picked in Quiver only settles a remaining tie. Every other candidate becomes
an other action, a single launcher being called **Setup**; more than eight are taken as
bundled build tools and none is offered.

On every library update, installed apps with several programs and no entry are written
to `executables.local.json` in the extension's data folder
(`ExtensionsData\a48d03b5-791b-4b3c-ad89-1c8c88e81dd2\`) with `"reviewed": false`,
and listed in `extensions.log`. Those entries are rewritten automatically and are only a
record. Edit one and set `"reviewed": true` to use it: it is then kept as is and wins
over the shipped database. Reviewed entries are welcome as contributions to
`executables.json`.

Other actions written by the extension are the non-play file actions whose path starts
with `{InstallDir}\`; they are replaced on every update. Any other action you add to
these games is left alone (give your own a full path rather than `{InstallDir}`).

## Achievements

Some recomps have achievements, but each keeps them its own way, and the Playnite
Achievements extension knows none of these games. What the extension does today
(**experimental**):

- **Recorded in a file the extension can read** (Bloodborne: `CUSA00207\trophies.txt`, with
  the full list and icons in `trophy_cache\`): exported as a CSV for Playnite Achievements,
  see the setting above. Sonic Unleashed, Super Mario 64 (Ghostship), F-Zero X (G-Diffuser)
  and Twilight Princess (Dusklight) also record theirs in a file, already listed in
  `executables.json`; their export will follow (some don't store the achievement names).
- **RetroAchievements built in** (Silent Hill; also The Minish Cap, not checked again): the
  unlocks go to your RetroAchievements account. In Playnite Achievements, set the game's
  provider to RetroAchievements and its game ID by hand (Silent Hill **11252**, The Minish
  Cap **559**; also stored as `retroAchievementsId`). The extension adds no platform to
  these games, so Playnite Achievements allows that override. Log in to RetroAchievements
  inside the port so it can unlock them. Other recomps of games that have a
  RetroAchievements set don't report to it, so playing them unlocks nothing there.
- **No local file**: the ReXGlue ports checked so far leave no achievement file, and Dead
  Rising 2 (Case Zero / Case West) sends them to a XenonLive account.

## Changelog

**0.3.15**
- inFamous: Play now finds the game the setup built in the shared games folder (`{GamesDir}\<folder>\inFamous.exe
  --play`), with its save folder, the common display settings (`infamous-recomp\settings.ini`) and the reminder
  on uninstall.
- New entries: Sonic the Hedgehog (2006) (Marathon Recompiled), Fighting Vipers and Fighters Megamix (their
  launchers' `--play`, straight into the game with the disc and options chosen in the launcher).

**0.3.14**
- New "Close the game with Select + Start on the gamepad" setting (General tab, on by default): holding Select
  (Back, View, −) and then pressing Start closes the running recomp, first as with its window's close button and,
  if it is still open 5 seconds later, by ending its programs (the one Playnite started, its children and anything
  running from the game's folders). Xbox-style (XInput) controllers only.

**0.3.13**
- Hyrule Warriors: Definitive Edition: Play is `play\hwder.exe` in the shared games folder (`{GamesDir}`, any
  folder name), where its setup builds the game; the uninstall warning points there too.
- Hyrule Warriors: Age of Calamity: saves are `profile\save`, its backups and the account file (no longer the
  whole profile with its caches).

**0.3.12**
- The Legend of Zelda: Ocarina of Time 3D (TriAevum): Play is `Echoes_of_Time.exe` (the game's new name since
  v0.8.0; `TriAevum.exe` no longer exists); saves folder `data\savedata`.

**0.3.11**
- New "Games folder" setting (General tab): where games built or installed outside their app folder live
  (`{GamesDir}`), for PCs that keep them somewhere other than `<Quiver>\games`. Empty keeps the old folder.

**0.3.10**
- Cadence of Hyrule: Play is `cadence.exe` where its installer put it in the shared games folder (`{GamesDir}`,
  any folder name), no longer a leftover build program inside the app folder; saves next to it.

**0.3.9**
- 12 new entries: Luigi's Mansion 3 (Hotel Luigi, `--play`), The Legend of Zelda: A Link to the Past
  (`zelda3.exe` without its launcher), Killer Instinct Gold, Dead or Alive 3 (doa3-re), Illusion of Gaia, Bloody
  Roar Extreme (European program; the USA and Japan ones as other actions), Blinx: The Time Sweeper, BLiNX 2,
  Alone in the Dark: The New Nightmare, Hyrule Warriors: Age of Calamity (EXCUUUUSE ME PRINCESS), Mario & Luigi:
  Brothership and The Darkness. Play program, saves folder, setup kind and, where the game has one, display
  settings (full screen and internal resolution) for each.

**0.3.8**
- Bayonetta 3 (Witch Time): the saves folder is `build\userdata\save` (where the game really writes them), for the
  saves menu and Ludusavi.

**0.3.7**
- Perfect Dark Zero Reinstated: Play is `PDZReinstatedTU3.exe` straight from where its installer put it in the
  shared games folder (`{GamesDir}`, any folder name); saves folder next to it; full screen and internal
  resolution from the Display tab. The Bonus DVD action is gone (it is outside the app folder).
- Saves of a game in the shared games folder are looked for next to its play program.

**0.3.6**
- Settings in tabs: General, Saves and achievements, Display, Import.
- Common display settings (Display tab): full screen and internal resolution (the game's own, 1080p,
  1440p or 2160p), applied when a game starts through the new `display` field of `executables.json`
  (command-line options and/or lines of the game's settings file). For now NeverDead, Tekken 6 and
  Bayonetta 3 (Witch Time). Per-game settings are prepared but not editable yet.
- NeverDead: saves and achievements folders; the resolution now comes from the Display tab.

**0.3.5**
- NeverDead (Neverdeth) plays in Playnite's language (Spanish, French, German or Italian when
  Playnite uses one of them) at twice the internal resolution (1440p).

**0.3.4**
- Notes in `executables.json`: Ocarina ReTuned hangs after Start Game with the game language in
  Spanish (USA ROM), and Astro Bot's US dump 01.007.000 crashes when a game loads (port issue #54;
  tested version 01.019.000).

**0.3.3**
- Bayonetta 3 (Witch Time) plays with `--play`, skipping the launcher's screens (they still open
  when the game isn't built or a newer release is out).

**0.3.2**
- Tekken 6 plays without its launcher: the engine starts directly in a borderless full screen
  window, in Playnite's language (the launcher, still under other actions as "Launcher", asks
  for the resolution every time and loops with another language). `executables.json`: `launch`,
  `borderless` and `{XboxLanguage}` for the play program.

**0.3.1**
- "Setup pending" tag on the games installed in Quiver that still have to be built or installed
  by their launcher or installer (never on apps uninstalled from Quiver), so they can be
  filtered; added and removed on every library update. `executables.json`: `setup.built`, a file
  or folder that only exists once the game is ready.
- Shared games folder `{GamesDir}` (`<Quiver>\games`) for games that can't be built inside their
  app's folder, and `hideConsole` for the play program.
- New or reviewed entries: Star Fox (SpaceAnimalsRecomp, played without its launcher), WipEout
  Omega Collection (`Play WipEout.exe`), Hyrule Warriors: Age of Imprisonment (fullscreen,
  without its launcher; Quiver lists it as Age of Calamity), Burnout 2, Dead or Alive 2
  Ultimate, Kameo, Astro Bot, Cadence of Hyrule, Hyrule Warriors: Definitive Edition, inFamous,
  Silent Hill: Book of Memories, Ratchet & Clank (OpenRAC), Tekken Tag Tournament 2 (Ultimate
  Edition), Wario Land: Shake It! and Bayonetta 3 (B3-Express).

**0.3.0**
- Play programs that need environment variables: the extension starts them itself (Crash of
  the Titans runs its MojoRecomp runtime directly, without the launcher window).
- `executables.json`: `environment` for the play program, `{AppDir}`/`{ExeDir}` and
  environment variables in arguments, wildcards in folder names.
- New entries: Crash of the Titans, Dino Crisis, GoldenEye 007: Reloaded, Gravity Rush 2,
  Gravity Rush Remastered, Knack, Parasite Eve II, Perfect Dark Zero Reinstated, SoulCalibur
  (Voldorium) and Turok: Evolution.
- Apps whose download is an installer (the game is installed or built into another folder):
  when Quiver updates one, a notification says the installed game is still the old one and
  opens its installer. `executables.json`: `setup` (`installer` / `builder`); marked: Tekken 6,
  Tekken Tag Tournament 2, Burnout Revenge, Dino Crisis, Perfect Dark Zero Reinstated, Street
  Fighter Alpha 3, WET, Xenoblade Chronicles X and both Crash of the Titans installers;
  `builder` (launchers that build the game from the disc): Mario Kart: Double Dash!! (Project
  Tandem), Soulcalibur II (RingOut), Ratchet & Clank: A Crack in Time, Resident Evil Outbreak,
  Bayonetta 3, Ocarina of Time 3D (ReTuned), Kirby Air Ride and Mario Kart DS. `setup` also
  records each installer's command line (`install`, `update`) and `notes`.
- Sidebar panel "Recomps: installers" (Desktop mode): every installed app with an installer or a
  launcher that builds the game, whether Quiver's download is newer than the installed game, its
  commands and notes; open the installer or the folder, and mark the setup as done. Apps whose
  update command needs nothing else (WET; Xenoblade Chronicles X once installed) are updated
  from there: after a confirmation the command runs in the background, one at a time, with its
  output in a log, the game shown as installing meanwhile and the progress on the sidebar icon.
  Installers that need the disc again (Tekken Tag Tournament 2, Tekken 6, Burnout Revenge) ask
  for it once and remember it while the file exists ("Forget files" clears it).
- Uninstalling an installer's app: Quiver removes only the app folder, so a notification says
  where its game still is when it was installed elsewhere (a play program chosen outside the app
  folder, or the installer's default folder: Dino Crisis, Perfect Dark Zero Reinstated).
- Experimental achievements export: also the ReXGlue ports' unlocks (`achievements\<title id>.toml`):
  Street Fighter III: 3rd Strike Online Edition (with the icons its ArcadeInfo.xml lists),
  Spider-Man: Edge of Time and GoldenEye 007 (XBLA). Names, descriptions, gamerscore and icons
  come from the game's own data (the XDBF in its `default.xex`, in Playnite's language when the
  game has it) when its program is not LZX-compressed (Spider-Man); otherwise they are exported
  as "Achievement N".
- "Update all Quiver apps": Quiver's `--update` stops after the first app it updates, so the
  extension watches its output, ends each run once that app is updated and starts it again
  until every update is done; the progress window says which app is updating, can be
  cancelled, and the notification lists the apps updated.
- Street Fighter Alpha 3 plays the game installed in `game\` (its installer is the Setup action).
- New entries (play program, saves, portable, achievements): Street Fighter III: 3rd Strike
  Online Edition (ReXGlue achievements file), Jet Set Radio Future, Resident Evil Code:
  Veronica X, Shadow the Hedgehog, Stunt Race FX, Wario World, Haunting Ground, Bomberman 64:
  The Second Attack!, Bloodborne (Lantern Engine), Resident Evil: The Umbrella Chronicles, and
  the saves of Virtua Fighter 2, Virtua Fighter 3 and Luigi's Mansion.

**0.2.0**
- Experimental achievements export for Playnite Achievements (setting off by default):
  Bloodborne's trophies as a CSV to import in that extension, from the game menu and
  rewritten when the game closes.
- `executables.json`: new `achievements` field (kind, unlock file, RetroAchievements game
  ID) for the apps reviewed so far.

**0.1.0** — first release.

## Repository layout

```
QuiverLibrary.sln
src/
  QuiverLibrary.Core/         Quiver logic (paths, library files, install rules, command line, saves, achievements); no Playnite dependency
  QuiverLibrary.Core.Tests/   xUnit tests for Core
  QuiverLibrary.Playnite/     The Playnite GameLibrary extension (extension.yaml, settings, controllers, localization)
```

## Building

Requires the .NET SDK (8 or newer); NuGet restores PlayniteSDK and the .NET Framework
4.6.2 reference assemblies, so Visual Studio isn't needed.

```
dotnet build QuiverLibrary.sln -c Release
dotnet test src/QuiverLibrary.Core.Tests -c Release
```

The extension is the content of `src/QuiverLibrary.Playnite/bin/Release`
(`QuiverLibrary.Playnite.dll`, `QuiverLibrary.Core.dll`, `extension.yaml`, `pluginicon.png`,
`Localization/`). Zip it and rename the `.zip` to `.pext`.

## License

[MIT](LICENSE).
