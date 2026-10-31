# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Two audiences with equal weight:
- Players who want a few of their own NPCs in their own town (Schedule I, singleplayer or co-op host).
- Pack authors who build NPC packs and publish them on Thunderstore or Nexus.

Both sit at a PC with mouse and keyboard, in the game's main menu, with the game running behind the editor.
UI language is English or German.

## Product Purpose
Personify is an NPC editor for Schedule I that runs inside the game. A player builds an NPC without code: look,
role (civilian, customer, dealer, supplier), daily routine on the town map, and relations to other NPCs. Success:
the NPC stands in the player's town and behaves like a vanilla NPC.

## Positioning
See it, test it, done: the NPC is built live on the game's real character model, one click puts the player next to
it in a save, and one more click takes it into their game. No other Schedule I tool shows the result on the real
model and tests it in the real game.

## Operating Context
- Reached from the Side Hustle hub over the main menu ("Play" on the Personify tile). The game's live menu scene
  stays behind the editor; the 3D preview character is rendered into that scene.
- Work is organised in projects (auto-saved, with history). "In mein Spiel" installs a project as a pack for the
  Personnel mod; "Veröffentlichen" builds a Thunderstore-ready zip.
- "Testen" restarts the game, loads a chosen save and puts the player next to the NPC.
- A phone companion app in the game sets start points and route steps where the player stands.

## Capabilities and Constraints
- Rendered by Sideload: HTML/CSS/JS to Unity uGUI, not a browser. No CSS animation, filter, float, visibility,
  backdrop blur or url() images; transitions on colour, opacity and transform; Latin-1 glyphs only. Images are
  bundle PNGs or runtime PNGs published by the host as `s1://name`.
- Full screen 1920x1080 (and other 16:9/16:10 sizes) over the main menu.
- Tattoos come from the game or from installed Inkorporated packs (made with Inkubator). No raw PNG import.
- Needs Sideload, Personnel, S1API and Side Hustle.

## Brand Commitments
- Personify is part of the Side Hustle family: it uses the hub's visual language (user decision, 2026-10-29).
- Lucide icons, rendered to PNG where SVG cannot be shown.
- German text with proper umlauts; plain "-" instead of dashes; one sentence per state in UI strings.

## Evidence on Hand
- The game's own character model, accessories, tattoos and town map (live in the editor).
- Contact pictures captured from the preview (`portraits/<id>.png` in a project).
- No testimonials, download counts or reviews may be invented.

## Product Principles
1. The NPC is the hero: every screen shows the character or its face, never a bare list.
2. See, test, done: the path from change to "in my game" is short and always visible.
3. Plain game language first; raw numbers only in Expert mode.
4. A step that restarts the game or removes work says its consequence before it happens.
5. One primary action per view.
