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

- `file` is relative to the app's folder; its file name may contain `*`/`?` (the highest
  version wins). With `"searchSubfolders": true` it is a file name looked for in every
  subfolder, the shallowest one winning: for games whose installer lets you choose where
  they go.
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
