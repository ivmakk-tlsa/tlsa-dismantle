# Changelog

All notable changes to this mod are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this mod uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-09-05

### Added

- The inventory Dispose action now returns salvage instead of destroying the item. The menu label stays "Dispose".
- Consumables that leave an item on use (canned food, water) return that item on dispose, one per unit.
- Bandages return a rag; a battery returns electronics.
- Craftable throwables (molotov, can bomb, beeper bomb, box mine) return their surviving parts.
- Craftable weapons, attachments and crafting parts return one of their recipe inputs, chosen at random.
- Ranged weapons return scrap or firearm parts; metal melee weapons return scrap or melee parts. Wooden or primitive melee returns nothing.
- Each returned item raises the game's own "added to inventory" popup.
- Per-rule on/off toggles and configurable weapon and material chances, all in the config file.
