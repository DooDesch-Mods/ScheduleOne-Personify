# Changelog

All notable changes to Personify are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

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
