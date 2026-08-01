# Changelog

All notable changes to Personify are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.2.2] - 2026-08-01

### Changed

- Runs on Schedule I 0.4.6f11.
- Needs S1API 3.1.1, up from 3.0.5. Update it along with the mod.

## [1.2.1] - 2026-07-27

### Fixed

- Clothing no longer sticks between characters. Building several NPCs in a row could leave a garment from
  an earlier one on the new character: the game clears only six of its eight avatar layer slots, so an NPC
  with more layers than that strands its clothing where the next NPC inherits it.
- Layers no longer look darker than they should. The same leak could composite one layer several times
  over, which read as a tint that nothing in the editor removed.
- A layer no longer vanishes without explanation. Past eight body layers the game silently drops one;
  the surplus is now dropped deliberately and named in the log.
- Two NPCs that each import a PNG with the same filename no longer share one texture. Custom layer paths
  were derived from the filename alone, so the second NPC rendered the first one's image.

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
