# Personify - In-Game NPC Editor for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/personify](https://support.doodesch.de/personify).

> Build your own NPCs inside the game - look, role, daily routine and contacts - with a live 3D
> preview, test them next to you in your save, and publish a ready-to-upload
> [Personnel](https://github.com/DooDesch-Mods/ScheduleOne-Personnel) NPC pack. Opens from the
> [Side Hustle](https://github.com/DooDesch-Mods/ScheduleOne-SideHustle) hub on the main menu.

![Version](https://img.shields.io/badge/version-2.1.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
![SideHustle](https://img.shields.io/badge/Side%20Hustle-required-yellow)
![Sideload](https://img.shields.io/badge/Sideload-1.36.0+-yellow)
![Personnel](https://img.shields.io/badge/Personnel-2.4.2+-yellow)
[![Polyfill](https://polyfill.doomods.com/badge/personify.svg)](https://polyfill.doomods.com/mod/personify)

[![MLVScan Personify.dll](https://mlvscan.com/attestations/att_glTynOODReF7VEqZWqgEcHVb/badge.svg)](https://mlvscan.com/attestations/att_glTynOODReF7VEqZWqgEcHVb)

![The new editor next to an NPC from it in town](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/header.jpg)

## Documentation

- 📖 **[Personnel wiki](https://docs.doodesch.de/mods/personnel/)** - the framework your packs run on:
  pack format and id rules, the consumer API, multiplayer, troubleshooting.
- This README is the guide to the editor itself.

## Features

- **Look.** Body, hair, face, eyes, clothing, accessories and tattoos, applied live to a 3D preview
  you can rotate and zoom. Random look, and an Expert view with the full face, body and accessory
  layer stacks.
- **Role.** Civilian, customer, dealer or supplier. Customers get their profile, order days, taste,
  samples and addiction; dealers and suppliers get their own panels.
- **Routine.** Click the town map to set where the NPC starts the day and where it goes: go inside,
  sit, hang out, or drive to a car park in its own car. The whole day shows on a 24 h strip.
- **Relations.** Pick the NPCs that know yours and how the player unlocks them.
- **Test.** Pick a save; the game restarts into it and puts you right next to your NPC.
- **Phone app.** In a loaded save, jump to an NPC, set its start point or a route stop where you
  stand, and see a look change on the live NPC.
- **Projects.** Saved as you go, with undo and redo, a history to step back to, and edits on several
  NPCs at once. Import any installed Personnel pack as a project.
- **Installed packs.** Switch a pack on or off, or remove it, from the start page.
- **Publish.** Builds a Thunderstore-ready zip (manifest, README, LICENSE) under
  `UserData/Personify/Exports/`.
- **Inkorporated tattoos.** If [Inkorporated](https://github.com/DooDesch-Mods/ScheduleOne-Inkorporated)
  is installed, its tattoo packs appear in the tattoo picker; the art is copied into your pack.

## Screenshots

| | |
|---|---|
| ![The Look area with the live 3D preview](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/editor-look.jpg) | ![The Role area of a customer](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/editor-role.jpg) |
| ![The Routine area with the day on the town map](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/editor-routine.jpg) | ![The phone app in a loaded save](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/phone-app.jpg) |

![An NPC from the editor in town](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/in-town.jpg)

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | IL2CPP (current Steam public build) |
| MelonLoader | `0.7.3+` |
| S1API | [ifBars/S1API_Forked](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) `3.2.1+` |
| Side Hustle | [DooDesch-SideHustle](https://thunderstore.io/c/schedule-i/p/DooDesch/SideHustle/) `3.2.1+` - the hub the editor opens from |
| Sideload | [DooDesch-Sideload](https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/) `1.36.0+` - draws the editor and the phone app |
| Personnel | [DooDesch-Personnel](https://thunderstore.io/c/schedule-i/p/DooDesch/Personnel/) `2.4.2+` - runs your NPCs in the game |
| Inkorporated | optional - its tattoo packs show up in the tattoo picker |

## Installation

### Recommended: a Thunderstore mod manager
Install with r2modman / Gale from the Schedule I community; the dependencies are pulled in
automatically.

### Manual
1. Install **MelonLoader 0.7.3** for Schedule I.
2. Install **S1API**, **Side Hustle**, **Sideload** and **Personnel**.
3. Drop **`Personify.dll`** into your Schedule I `Mods/` folder.

## Usage

1. Main menu -> **Side Hustle** -> **Personify**.
2. Create a project (it becomes your pack) and add NPCs with **+ New NPC**.
3. Work through **Look**, **Role**, **Routine** and **Relations**. The problems list names what is
   still missing.
4. **Add to my game** installs the pack. **Test** starts a save of your choice next to the NPC.
5. **Publish** writes the zip to `UserData/Personify/Exports/`. Add a 256x256 `icon.png` before you
   upload it to Thunderstore.

Tips:

- Escape or right-click steps back one level. Ctrl+Z and Ctrl+Y undo and redo.
- Select several NPCs in the list to change them together.
- **History** in the project menu steps a project back to an earlier state.
- Own tattoos come from [Inkubator](https://github.com/DooDesch-Mods/ScheduleOne-Inkubator): install its export as
  an Inkorporated pack and the tattoos show up in the picker.
- The NPC id (`packname_npcname`) is what other mods reference; renaming an NPC changes it.

## Configuration

No settings. Projects are stored under `UserData/Personify/Projects/`, exports under
`UserData/Personify/Exports/`. Projects from 1.x open as they are.

## Credits

- **DooDesch** - mod author.
- **[ifBars/S1API](https://github.com/ifBars/S1API)** - the modding API this is built on.

## License

Provided as-is under the [MIT License](LICENSE.md).
