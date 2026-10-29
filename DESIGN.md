---
name: Personify
description: In-game NPC editor for Schedule I, a member of the Side Hustle hub family, drawn over the live main menu.
colors:
  text: "#F4F4F2"
  text-muted: "rgba(244, 244, 242, .64)"
  text-dim: "rgba(244, 244, 242, .40)"
  backdrop: "#090A0D"
  glass: "#0D0F14"
  step-0: "#101217"
  step-1: "#16191F"
  step-2: "#23262D"
  step-3: "#2E323A"
  step-4: "#3A3F49"
  line: "#2A2E36"
  cash: "#7CCB5B"
  cash-high: "#97DE77"
  on-cash: "#0C1509"
  cash-quiet: "#1D2A17"
  neon: "#E04AE6"
  neon-text: "#F2A6F5"
  neon-quiet: "#3B1C40"
  on-neon: "#16051A"
  amber: "#E8B24E"
  amber-quiet: "#33270F"
  danger: "#F1707A"
  danger-quiet: "#3A1A1E"
typography:
  display:
    fontFamily: "game-ui"
    fontSize: "26px"
    fontWeight: 800
  headline:
    fontFamily: "game-ui"
    fontSize: "22px"
    fontWeight: 800
  title:
    fontFamily: "game-ui"
    fontSize: "15px"
    fontWeight: 700
  body:
    fontFamily: "game-ui"
    fontSize: "14px"
    fontWeight: 400
  label:
    fontFamily: "game-ui"
    fontSize: "13px"
    fontWeight: 700
  caption:
    fontFamily: "game-ui"
    fontSize: "12px"
    fontWeight: 400
rounded:
  xs: "6px"
  sm: "8px"
  md: "10px"
  lg: "12px"
  panel: "14px"
  dialog: "16px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  panel-gap: "16px"
components:
  button-primary:
    backgroundColor: "{colors.cash}"
    textColor: "{colors.on-cash}"
    typography: "{typography.title}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "40px"
  button-primary-big:
    backgroundColor: "{colors.cash}"
    textColor: "{colors.on-cash}"
    rounded: "{rounded.lg}"
    padding: "0 24px"
    height: "52px"
  button-secondary:
    backgroundColor: "{colors.step-2}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "40px"
  button-secondary-hover:
    backgroundColor: "{colors.step-3}"
  button-ghost:
    textColor: "{colors.text-muted}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "40px"
  button-icon:
    backgroundColor: "{colors.step-2}"
    rounded: "{rounded.md}"
    size: "36px"
  input:
    backgroundColor: "{colors.step-2}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "9px"
    padding: "0 12px"
    height: "36px"
  chip:
    backgroundColor: "{colors.step-2}"
    textColor: "{colors.text-muted}"
    rounded: "9px"
    padding: "0 12px"
    height: "32px"
  chip-selected:
    backgroundColor: "{colors.neon-quiet}"
    textColor: "{colors.text}"
  segment-selected:
    backgroundColor: "{colors.neon-quiet}"
    textColor: "{colors.neon-text}"
    rounded: "{rounded.sm}"
    height: "30px"
  badge-in-game:
    backgroundColor: "{colors.cash-quiet}"
    textColor: "{colors.cash}"
    rounded: "{rounded.sm}"
    padding: "0 10px"
    height: "26px"
  badge-attention:
    backgroundColor: "{colors.amber-quiet}"
    textColor: "{colors.amber}"
    rounded: "{rounded.sm}"
    padding: "0 10px"
    height: "26px"
  badge-neutral:
    backgroundColor: "{colors.step-2}"
    textColor: "{colors.text-muted}"
    rounded: "{rounded.sm}"
    padding: "0 10px"
    height: "26px"
  panel:
    backgroundColor: "{colors.glass}"
    rounded: "{rounded.panel}"
  section:
    backgroundColor: "{colors.step-0}"
    rounded: "{rounded.lg}"
  section-open:
    backgroundColor: "{colors.step-1}"
  npc-row-selected:
    backgroundColor: "{colors.neon-quiet}"
    rounded: "{rounded.lg}"
  dialog:
    backgroundColor: "{colors.glass}"
    rounded: "{rounded.dialog}"
    padding: "22px 24px"
    width: "620px"
  face:
    backgroundColor: "{colors.step-3}"
    rounded: "{rounded.md}"
    size: "44px"
---

# Design System: Personify

## Overview

**Creative North Star: "The NPC Stands on the Stage"**

Personify is drawn by Sideload, an HTML/CSS/JS subset rendered to Unity uGUI over the Schedule I main menu. It is not a browser: every box is a column flexbox, there is no animation, filter, blur or `url()` image, a gradient has at most two stops, text is Latin-1, and transitions run on colour, opacity and transform only. The design works inside those walls rather than imitating a web page.

The editor gives the whole screen to the live 3D preview of the NPC, rendered into the game's menu scene. The tools are opaque near-black glass panels floating over that stage: a slim crew list on the left, the fields panel on the right, and one pipeline bar centred at the bottom. The start page is a crew wall: project cards carrying the NPCs' face portraits, with installed packs in a side panel. Faces appear wherever an NPC is named. It belongs to the Side Hustle hub family: the same slab wordmark, the same dark steps, the same cash green for the action that pays off.

Values are plain language. Sliders name their two ends ("Männlich - Weiblich", "Klein - Groß"), collapsed sections show their current value as a summary, and raw numbers appear only in Expert mode. Money, share percent and clock times stay numeric because they are numbers in the game too.

**Key Characteristics:**
- The 3D preview is the stage; panels float over it with 16px margins.
- Opaque surfaces in dark steps; no translucent surface anywhere.
- Three colours carry meaning: cash green acts, neon is live or selected, amber needs attention.
- Heavy game-ui type (700/800) for names and titles, regular for values and notes.
- Faces (contact pictures, initials as fallback) beside every NPC name.
- Lucide icons as white PNGs, tinted by colour.

## Colors

A near-black world in opaque steps, lit by three signal colours that each mean exactly one thing.

### Primary
- **Cash Green** (`cash`, gradient from `cash-high`): the one action that does something in the current view: "Neues Projekt" on the start page, "In mein Spiel" in the pipeline bar, the confirm button of each dialog ("Neu starten und testen"). Text on it is `on-cash`. Its quiet form (`cash-quiet` ground, `cash` text) marks a finished state: the "In deinem Spiel" badge, a done wizard step, the "ok" toast, the spawn pin on the map.

### Secondary
- **Neon Magenta** (`neon`, `neon-text`, `neon-quiet`): live or selected. Switches that are on, the selected NPC row and role card, the selected save in the Testen dialog, the active tab underline, segmented and chip selections, checked checkboxes, the selected route step and its map pin, the multi-select count. Solid `neon` fills small controls with `on-neon` marks; large selected surfaces take `neon-quiet` with a 2px `neon` edge.

### Tertiary
- **Amber** (`amber`, `amber-quiet`): needs attention. Problem counts, the "changed" badge, the Testen dialog's consequence line ("Das Spiel startet neu ..."), Mixed values in multi-select, and the highlight ring on a field a problem points to.
- **Soft Red** (`danger`, `danger-quiet`): errors and removal. Error problems, form errors, the delete item in a menu. The destructive dialog button uses a deep red fill (#5A2227) with pale pink text (#FFD9DC).

### Neutral
- **Light Text** (`text`): names, titles, values.
- **Muted Text** (`text-muted`): labels, secondary lines, summaries, slider end names.
- **Dim Text** (`text-dim`): disabled items and empty-state hints.
- **Backdrop** (`backdrop`): the start page ground, the only full-screen surface.
- **Glass** (`glass`): panels, dialogs, the pipeline bar, the map frame.
- **Steps 0-4** (`step-0` to `step-4`): nested surfaces, darkest to lightest. Collapsed sections sit on `step-0`, open sections and list rows on `step-1`, buttons and inputs on `step-2`, hovers on `step-3`, scroll thumbs and route blocks on `step-4`.
- **Line** (`line`): the 1px dividers under the tabs, above the Expert footer and inside menus.

### Route Colours
Route steps on the day timeline carry a fixed data palette by step kind, muted so the neon selection edge stays loudest: walk #3A4A6A, stay in building #4A3A5E, sit #2E5450, location action or dialogue #5A4A2A, vending/ATM/slot machine #5A2E3A, drive to car park #2E4A5A, deal #2E5A3A. Map places use #8A8F9E (generic), #B48CE6 (building), #6FA8E6 (car park), #5FD09A (seat).

### Named Rules
**The Three Signals Rule.** Cash green, neon and amber each carry one meaning and are never used for decoration. If a colour is not acting, live or warning, the element is light text on a dark step.

**The One Cash Button Rule.** A view has at most one solid cash button. A second positive action is secondary (`step-2`) or quiet cash.

**The Opaque Glass Rule.** Surfaces are opaque hex. White-alpha surfaces blend to a flat mid grey in the renderer; alpha is used for text tints only, and black alpha only for the dialog scrim (rgba(0, 0, 0, .66)) and shadows.

## Typography

**Display Font:** game-ui (the game's own UI face, supplied by Sideload)
**Body Font:** game-ui

**Character:** One family, carried by weight. Names and titles sit at 800, buttons and labels at 700, values and notes at 400; no uppercase, no letter-spacing, no italics.

### Hierarchy
- **Display** (800, 26px): the "Projekte" wall title on the start page.
- **Headline** (800, 22px): dialog titles, the NPC name in the fields panel, the empty-state message. The editable project name runs at 24px/800.
- **Card Title** (800, 19px for project cards; 18px for the packs panel heading; 16px for the project menu and tabs at 700).
- **Title** (700-800, 15px): NPC names in the list, section headers, role card names.
- **Body** (400, 14px): field labels, values, menu items, dialog text. Values that are set read 700; defaults read 400 muted.
- **Label** (700, 13px): sub-headings inside sections, dialog field labels, wizard steps, small buttons.
- **Caption** (400, 12px): role under a name, slider end names, badge text (700), list sub-lines. Timeline ticks at 11px.

### Named Rules
**The Plain Words Rule.** A value is shown as words a player uses. Slider ends are named, section headers summarise their value, and numbers appear only in Expert mode, except money, share percent and clock times.

## Layout

The editor is a 1920x1080 (and other 16:9/16:10) full-screen layer. Panels are absolutely placed with 16px margins: the side list is 320px wide, the fields panel 460px, both full height. The stage is the space between them (left 352px, right 492px); dragging there turns the NPC, the wheel zooms. On the Alltag tab the stage becomes a glass-framed town map that ends 112px above the bottom so the pipeline bar never covers it. The pipeline bar floats 32px above the bottom edge, centred over the stage.

The start page is a padded column (40px 48px 32px) on the backdrop: wordmark and tagline, then a two-column body with 28px between the crew wall (448px project cards in a wrapping grid with 16px gaps) and the 420px installed-packs panel.

Rhythm is 8px-based with a few 2px adjustments: 8px between rows and buttons, 12px inside list rows, 14-16px panel padding, 20px panel header padding. Every list row and button has a fixed height (28/34/40/52px buttons, 44-48px row headers, 60px pack and project rows) so lists stay even.

## Elevation & Depth

Depth is tonal first: the five surface steps nest darker-to-lighter inward. One soft shadow lifts floating layers off the 3D stage: panels, dialogs, menus and toasts carry `0 18px 40px rgba(0, 0, 0, .55)`. Nothing else casts a shadow.

### Shadow Vocabulary
- **Float** (`box-shadow: 0 18px 40px rgba(0, 0, 0, .55)`): glass panels, dialogs, overflow menus, toasts.

### Named Rules
**The Tonal Nesting Rule.** Each nested surface is one step lighter than its parent; hover moves one step further. A row on `step-1` hovers to `step-2`, a button on `step-2` hovers to `step-3`.

## Shapes

Soft rectangles throughout. Radius grows with the size of the surface: 6px on route blocks, 8px on small buttons, badges and timeline tracks, 9-10px on inputs, buttons and icon buttons, 12px on rows, cards, sections and menus, 14px on panels, 16px on dialogs. Faces are rounded squares (10px at 44px, 12px at 56-60px), never circles. Switches, scroll thumbs, step numbers and map pins are the only fully rounded shapes.

Borders are reserved for selection and focus of attention: a 2px edge on selectable cards and rows (`neon` when selected), a 3px `neon` underline on the active tab, a 1px `amber` ring on a highlighted field, a 2px light ring on map pins. A box has one border colour.

## Components

### Buttons
Solid, heavy-labelled, fixed height.
- **Shape:** gently rounded (10px; 8px at the 28px size, 12px at the 52px size).
- **Primary (cash):** two-stop vertical gradient from `cash-high` to `cash`, `on-cash` text and icon, 700 weight. Hover lifts the gradient one tint (#B0EA94 to `cash-high`). Sizes 28/34/40/52px.
- **Secondary:** `step-2` fill, light text, hovers to `step-3`. "Testen", "Pack importieren", "Abbrechen", "Neuer NPC".
- **Ghost:** no fill, muted text; hover fills `step-2` and lightens the text.
- **Quiet cash:** `cash-quiet` fill with `cash` text for a positive secondary action.
- **Destructive:** deep red fill (#5A2227) with pale pink text, only as a dialog confirm for removal.
- **Disabled:** `step-1` fill, dim text and icon; clicks are ignored.
- **Icon button:** 36px square, 10px radius, `step-2` or ghost; carries a hover hint. Undo/redo, duplicate, delete, overflow.
- Transitions: background and text colour over 120ms.

### Chips and Segmented Controls
- **Chips:** 32px, 9px radius, `step-2` with muted text and a 2px edge in the same colour; selected chips take `neon-quiet` with a `neon` edge and light text.
- **Segmented:** a `step-2` track with 3px inset; options are 30px, 8px radius, muted 600 text; the selected option takes `neon-quiet` with `neon-text`.

### Switch and Checkbox
- **Switch:** 42x24 track, knob 18px. Off: `step-3` track, muted knob. On: `neon` track, `on-neon` knob at the right. Mixed (multi-select): `step-4` track, knob centred. Pack rows pair it with an "An"/"Aus" word.
- **Checkbox:** 18px rounded square, 2px `step-4` edge; checked fills `neon` with an `on-neon` mark.

### Badges
Status chips of 26px, 8px radius, 12px/700 text with an optional 14px icon: "In deinem Spiel" in quiet cash, "changed" in quiet amber, "Nicht in deinem Spiel" in `step-2` with muted text.

### Cards / Containers
- **Panel:** `glass`, 14px radius, float shadow.
- **Project card:** 448px wide, 18px padding, up to five 60px faces then a "+N" tile, name at 19px/800, NPC count, status badge and overflow button on the bottom row. A 2px edge appears on hover (`step-4`).
- **Section (accordion):** `step-0` collapsed, `step-1` open, 12px radius. The 48px header shows the section name (15px/700), a right-aligned muted summary of its value, and a chevron (right when closed, down when open).
- **Role card:** 204px, `step-1`, 2px edge; selected takes `neon-quiet` with a `neon` edge.

### Inputs / Fields
- **Text input:** 36px, 9px radius, `step-2` fill, light text, placeholder at 58% white. Number inputs are 76px wide, right-aligned. The NPC name field reads as a heading (24px/800) on `glass` and only fills `step-1` on hover.
- **Field row:** a 128px muted label, the control, and a right-aligned value (700 when set, 400 muted at default).
- **Named-end slider:** label above, then the left end name (64px, muted 12px), the rail, and the right end name. Expert mode adds the number. A multi-selection with different values shows an amber "Mixed" tag.
- **Problem highlight:** the field a problem points to gets `amber-quiet` fill, a 1px `amber` ring and 6px padding.

### Navigation
- **Tabs:** the fields panel's areas (Aussehen, Rolle, Alltag, Beziehungen) as 16px/700 text, muted at rest, light on hover, light with a 3px `neon` underline when active; 22px gaps above a 1px `line` divider.
- **Crew list:** NPC rows with a 44px face, name (15px/700) and role (12px muted), a checkbox for multi-select. Hover fills `step-1`; the open NPC gets `neon-quiet` with a 2px `neon` edge; checked rows get `neon-quiet` only.
- **Project menu:** a 44px `step-1` bar with the project name (16px/800) and a chevron, opening the overflow menu.

### Pipeline Bar (signature)
One floating glass bar over the stage: problem status ("Bereit" or the amber/red count), the in-game badge, a 1px divider, "Testen" as a big secondary button, and "In mein Spiel" as the big cash button. Clicking the status opens the problem list above it.

### Dialogs, Menus, Toasts
- **Dialog:** `glass`, 16px radius, 22px 24px padding, 14px gap, float shadow, over a black 66% scrim. Widths 480/600/620/640px. Header: 22px/800 title plus a close icon button. A step that restarts the game or removes work states the consequence in an amber line before the buttons. Actions sit at the bottom right: secondary "Abbrechen", then the cash or destructive confirm.
- **Save list (Testen):** 12px-radius rows on `step-1` with a 34px slot-number tile; the selected save takes `neon-quiet`, a `neon` edge and a `neon` number tile.
- **Overflow menu:** 260px, `step-1`, 12px radius, float shadow; 40px items with a 16px muted icon, hover `step-3`; delete in `danger`; `line` separators.
- **Toast:** 520px, centred 28px from the top, `step-1`, 15px/600 text tinted by tone (cash, danger, amber, light), with a check or alert icon; disappears after 4.2s.
- **Wizard progress:** equal-width 34px steps on `step-1`; current takes `neon-quiet`, done ones read in `cash`.

### Face
The NPC's contact picture over its initials on a `step-3` rounded square: 44px in lists, 56px in the fields header, 60px on project cards.

## Do's and Don'ts

### Do:
- **Do** give the 3D preview the space between the panels and keep every tool panel opaque `glass`.
- **Do** use exactly one solid cash button per view, for the action that changes something.
- **Do** mark live or selected state with neon: `neon-quiet` fill plus a 2px `neon` edge on large items, solid `neon` on small controls.
- **Do** show a face next to every NPC name.
- **Do** name slider ends in plain words and summarise a collapsed section's value in its header; keep numbers for Expert mode, money, share percent and clock times.
- **Do** state the consequence in an amber line before a step that restarts the game or removes work.
- **Do** use Lucide icons as white PNGs tinted by `color`, 16-20px.
- **Do** write German with real umlauts and ß, a plain "-" where a dash is needed, and one sentence per state.

### Don't:
- **Don't** use white-alpha or other translucent surfaces; the renderer blends them to grey.
- **Don't** use cash green, neon or amber for decoration, headings or icons without meaning.
- **Don't** frame the character in a middle column between bordered side columns.
- **Don't** show engine values ("0.21", "deg") as the default reading of a field.
- **Don't** put a destructive action at equal weight beside the action a row exists for; it lives in the overflow menu.
- **Don't** use more than two gradient stops, CSS animation, filter, blur, `url()` images or non-Latin-1 glyphs.
- **Don't** use uppercase or letter-spaced text labels; the slab wordmark PNG is the identity asset, not a type style.
