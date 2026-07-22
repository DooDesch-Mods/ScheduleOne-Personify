# Personify - In-Game NPC Editor for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/personify](https://support.doodesch.de/personify).

> **Design custom NPCs live in-game** - body, face, hair, clothing, tattoos - previewed on the menu
> character, and export a ready-to-publish **Personnel** NPC pack. Runs as a **Side Hustle** gamemode,
> right from the main menu.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)

## What it does

- **Live preview** on the menu character - rotate, zoom, compare against the base human.
- **Character mode** mirrors the vanilla character creator; **Advanced mode** opens the full avatar
  surface: stacked layers, custom PNG imports, per-layer visibility and tint.
- **Inkorporated tattoos**: if Inkorporated is installed, its tattoo packs appear in the tattoo picker;
  chosen art is copied into your pack so exports stay self-contained.
- **One-click export** to a complete Personnel pack, wrapped Thunderstore-ready (manifest, README,
  LICENSE). Drop the pack into `UserData/Personnel/Packs/` to test; upload the wrapper to publish.

**Early release:** the main flows are verified in-game, but 1.0.0 has not yet seen extended real-world
testing - it will mature over the coming releases. Reports at
[support.doodesch.de/personify](https://support.doodesch.de/personify) directly shape the next version.

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
