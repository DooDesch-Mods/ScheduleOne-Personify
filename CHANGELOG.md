# Changelog

All notable changes to Personify are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-07-23

### Added

- Inkorporated tattoo packs now show up directly in the Advanced tab's face and body layer pickers,
  grouped by pack - face-placement tattoos in the face picker, everything else in the body picker.
  Picking one copies the PNG into your NPC pack, so exports stay self-contained (same behaviour as the
  Character tab's tattoo button, which previously was the only place they appeared).

### Fixed

- Tattoo packs installed while the game is running are picked up the next time the editor or a layer
  picker opens - the pack list is no longer read only once per session.

## [1.1.2] - 2026-07-11

### Changed

- Hardened the release build to cut down on antivirus false positives. The published DLL no longer
  carries debug symbols or a local build path, and it now ships proper assembly identity (author,
  product, copyright). Packaging only - no gameplay changes.

## [1.1.1] - 2026-07-10

### Fixed

- Released builds now compile the shared UI layer into Personify itself instead of silently borrowing it
  from SideHustle.dll, so a future Side Hustle update can no longer break the editor at launch.
- The mod now reports its real version to MelonLoader (previous releases always said 1.0.0).

## [1.1.0] - 2026-07-08

### Added

- Quick-pick chips for the game's standard clothing colours in the colour picker.

### Changed

- Clothing colour is easier to find: clothing rows now show a labelled "Colour" swatch.

## [1.0.0] - 2026-07-06

Initial release.

### Added

- Side Hustle gamemode: full NPC editor at the main menu with live preview on the menu character
  (rotate, zoom, base-human comparison).
- Character mode mirroring the vanilla character creator: gender, weight, skin, hair, mouth, facial
  hair, facial details, eyes, eyebrows, top/bottom, shoes, headwear, eyewear, tattoos.
- Advanced mode: stacked face/body/accessory layers, custom PNG layer import, per-layer visibility
  and tint, extension blocks for consumer mods.
- Inkorporated integration: installed tattoo packs are offered in the tattoo picker; chosen art is
  copied into the pack so exports stay self-contained.
- One-click export to a complete Personnel pack, wrapped Thunderstore-ready (manifest.json, README,
  LICENSE, optional icon).
- Auto-derived, duplicate-proof NPC ids (`packname_npcname`); duplicate names rejected at export.
- Project management under `UserData/Personify/Projects/` with autosave.
