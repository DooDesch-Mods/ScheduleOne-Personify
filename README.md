# Personify - In-Game NPC Editor for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

> Design custom NPCs live in-game - body, face, hair, clothing, tattoos - preview them on the menu
> character, and export a ready-to-publish
> [Personnel](https://github.com/DooDesch-Mods/ScheduleOne-Personnel) NPC pack. Runs as a
> [Side Hustle](https://github.com/DooDesch-Mods/ScheduleOne-SideHustle) gamemode, right from the main menu.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
![SideHustle](https://img.shields.io/badge/Side%20Hustle-required-yellow)

## Documentation

- 📖 **[Wiki](https://github.com/DooDesch-Mods/ScheduleOne-Personify/wiki)** - the full guide: editor
  modes and controls, custom PNG import, exporting and publishing, troubleshooting.
- 🧩 **[Personnel](https://github.com/DooDesch-Mods/ScheduleOne-Personnel)** - the framework the exported
  packs run on.

## Features

- **Live preview.** Every change is applied instantly to the menu character - rotate, zoom, and
  compare against the base human.
- **Character mode** mirrors the vanilla character creator: gender, weight, skin, hair, mouth,
  facial hair, facial details, eyes, eyebrows, top/bottom, shoes, headwear, eyewear, tattoos.
- **Advanced mode** opens the full avatar surface: stacked face/body/accessory layers, custom PNG
  layers imported from disk, per-layer visibility and tint, and extension blocks for consumer mods.
- **Inkorporated integration.** If [Inkorporated](https://github.com/DooDesch-Mods/ScheduleOne-Inkorporated)
  is installed, its tattoo packs are offered directly in the tattoo picker; chosen art is copied into
  your pack, so the export stays self-contained.
- **One-click export.** Writes a complete Personnel pack (manifest + PNGs) wrapped Thunderstore-ready
  (manifest.json, README, LICENSE) - drop the pack into `UserData/Personnel/Packs/` to test, upload
  the wrapper to publish.
- **Duplicate-proof ids.** NPC ids are derived automatically as `packname_npcname`; the editor
  rejects duplicate names at export.

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | IL2CPP (current Steam public build) |
| MelonLoader | `0.7.3+` |
| S1API | [ifBars/S1API_Forked](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) |
| Side Hustle | [DooDesch-SideHustle](https://thunderstore.io/c/schedule-i/p/DooDesch/SideHustle/) - hosts the editor as a gamemode |
| Personnel | optional at edit time - packs are file-coupled; install it (or any consumer) to see your NPCs in the world |
| Inkorporated | optional - unlocks its tattoo packs inside the editor |

## Installation

### Recommended: a Thunderstore mod manager
Install with r2modman / Gale from the Schedule I community; dependencies (MelonLoader, S1API,
Side Hustle) are pulled in automatically.

### Manual
1. Install **MelonLoader 0.7.3** for Schedule I.
2. Install **S1API** and **Side Hustle**.
3. Drop **`Personify.dll`** into your Schedule I `Mods/` folder.

## Usage

1. Main menu -> **Side Hustle** -> **Personify**.
2. Create a project (this becomes your pack name) and add NPCs with **+ Add NPC**.
3. Design in **Character** mode; switch to **Advanced** for layers, custom PNGs and extensions.
4. **Export** writes the pack to `UserData/Personify/Exports/<name>/` and opens the folder.
5. Copy the inner `Personnel/Packs/<name>` folder into `UserData/Personnel/Packs/` to test locally,
   or upload the whole export to Thunderstore (add a 256x256 `icon.png`).

Custom PNG layers: drop PNGs into `UserData/Personify/Import/`, then use "+ Add custom layer" in
Advanced mode. A layer PNG is a full UV-space texture (like Inkorporated tattoos) - opaque pixels
land where the UV region of that body part sits.

## Configuration

No settings. Projects are stored under `UserData/Personify/Projects/`, exports under
`UserData/Personify/Exports/`.

## Credits

- **DooDesch** - mod author.
- **[ifBars/S1API](https://github.com/ifBars/S1API)** - the modding API this is built on.

## License

Provided as-is under the [MIT License](LICENSE.md).
