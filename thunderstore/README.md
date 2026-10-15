# Personify - In-Game NPC Editor for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/personify](https://support.doodesch.de/personify).

> **Design custom NPCs live in-game** - body, face, hair, clothing, tattoos - previewed on the menu
> character, and export a ready-to-publish **Personnel** NPC pack. Runs as a **Side Hustle** gamemode,
> right from the main menu.

![Version](https://img.shields.io/badge/version-1.4.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
[![Polyfill](https://polyfill.doomods.com/badge/personify.svg)](https://polyfill.doomods.com/mod/personify)

[![MLVScan Personify.dll](https://mlvscan.com/attestations/att_kZRd-b_ArzT91bfOzeZlapWI/badge.svg)](https://mlvscan.com/attestations/att_kZRd-b_ArzT91bfOzeZlapWI)

![The World tab day plan next to the NPC driving through town](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/header.jpg)

## What it does

- **Live preview** on the menu character - rotate, zoom, compare against the base human.
- **Character mode** mirrors the vanilla character creator; **Advanced mode** opens the full avatar
  surface: stacked layers, custom PNG imports, per-layer visibility and tint.
- **World tab**: click the town map to set the start point and plan the day. Drive stops bring
  their own car, so exported NPCs drive with Personnel alone.
- **Inkorporated tattoos**: if Inkorporated is installed, its tattoo packs appear in the tattoo picker;
  chosen art is copied into your pack so exports stay self-contained.
- **One-click export** to a complete Personnel pack, wrapped Thunderstore-ready (manifest, README,
  LICENSE). Drop the pack into `UserData/Personnel/Packs/` to test; upload the wrapper to publish.

**Still a young mod:** the main flows are verified in-game, but the breadth of combinations has not yet
seen extended real-world testing - it will mature over the coming releases. Reports at
[support.doodesch.de/personify](https://support.doodesch.de/personify) directly shape the next version.

## Screenshots

![A full day plan on the town map](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/day-plan.jpg)

![An exported NPC driving its own car](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Personify/main/docs/img/on-the-road.jpg)

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API** and **Side Hustle** (pulled in as dependencies).
- Optional: **Personnel** (to see your NPCs in the world) and **Inkorporated** (tattoo packs in the editor).

## Usage

Main menu -> **Side Hustle** -> **Personify**. Create a project, add NPCs, design them, hit **Export**.
The export lands in `UserData/Personify/Exports/<name>/` and the folder opens automatically. Custom
PNG layers go into `UserData/Personify/Import/` first, then "+ Add custom layer" in Advanced mode.

Full guide on [GitHub](https://github.com/DooDesch-Mods/ScheduleOne-Personify).

## License

MIT. See the included LICENSE.md.
