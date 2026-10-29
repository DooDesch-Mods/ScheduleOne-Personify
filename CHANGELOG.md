# Changelog

All notable changes to Personify are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [2.0.1] - 2026-10-29

### Fixed

- New NPCs appear in the world. Before, only copies of older NPCs did.

## [2.0.0] - 2026-10-29

### Added

- New editor: four areas (Look, Role, Routine, Relations), a live 3D preview, undo and redo, and edit
  several NPCs at once.
- Test puts you in a save of your choice, right next to your NPC.
- Phone app: set an NPC's start point and route steps where you stand, and see a look on the live NPC.
- NPCs can be civilians, customers, dealers or suppliers. Customers unlock through the contacts you pick.
- Installed packs: switch them on or off, or remove them, from the editor's start page.

### Changed

- Your work lives in projects: saved as you go, with a history to step back to. Import turns a pack into a project.
- Publish builds a Thunderstore-ready zip under `UserData/Personify/Exports`.
- Needs Sideload 1.36.0 and Personnel 2.3.0. Update both along with the mod.

### Removed

- Custom PNG layers from `UserData/Personify/Import`. Tattoos come from the game or from Inkorporated packs, made with Inkubator.

## [1.5.0] - 2026-10-28

### Added

- Customers can start locked: set Unlock to "With a sample" and pick their contacts. Once you know a contact,
  a free sample opens them, like vanilla customers.
- The contact picker lists every NPC in town by area. Load a save once after updating so the list fills.

## [1.4.0] - 2026-10-15

### Added

- New World tab: click the town map to set where an NPC starts the day and where it goes.
- Drag pins to move stops, undo mistakes, and see the whole day on a 24 h strip.
- Drive stops bring their own car, so exported NPCs drive with only Personnel installed. Pick it under "Car".
- Duplicate copies an NPC with its look and day plan.

### Fixed

- NPCs made with Personify are visible again on game 0.4.7.
- Pickers hide avatar parts the game no longer has.

### Changed

- Needs S1API 3.2.1, up from 3.1.1. Update it along with the mod.

## [1.3.0] - 2026-08-13

### Added

- Advanced now reaches every value the game's own NPCs use: gender as a slider, eyebrow angle from -15 to 15,
  thickness up to 4, resting height from -2 to 2.
- Eyelid colour can be set by hand: turn off "Eyelids follow skin" in Advanced and each eye gets its own row.
- Layer tints carry an opacity channel, so a face detail can sit at part strength the way vanilla's freckles do.
- Experimental adds unclamped body and face sliders: gender -3 to 4, height up to 4x, and eyebrow rails far
  past anything vanilla uses.

### Fixed

- Eyelids take the skin colour. They stayed on the default tan, so a dark-skinned NPC ended up with pale lids.
- The censor layer takes its tint from the skin colour, and the underwear swaps when you change the gender.
- Face layers land in the slots the game reads them from. A freckle or eye-shadow layer could take the
  facial-hair slot, where the game draws it in the hair colour.
- A new face layer arrives black instead of white, which is how every stock NPC tints mouths, facial hair and
  detail layers.
- Upper and lower eyelid stay apart in Advanced. One slider per eye used to set both to the same value and
  lose what the Character tab had set.

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

## [1.0.1] - 2026-07-10

Not released. The tag points at the same commit as 1.1.1; there is no GitHub release and no Thunderstore
version under this number.

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
