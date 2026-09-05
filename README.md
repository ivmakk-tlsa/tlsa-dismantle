# Dismantle

A mod for [*The Last Stand: Aftermath*](https://www.nexusmods.com/thelaststandaftermath) that turns the inventory **Dispose** action into a salvage. Instead of destroying an item, Dispose now returns parts. The menu label stays "Dispose"; only the outcome changes.

Dispose a can of food and keep the can. Dispose a spare gun and get scrap or firearm parts. Dispose a crafted item and get one of the parts you built it from. Nothing is forced on you: an item you have no use for is still one click to break down, and the parts go straight to your inventory with the game's own "added" popup.

## What you get

Salvage is decided by rules in priority order (the first match wins):

- **Consumable byproduct** - a consumable that leaves an item on use (a can, a bottle) returns that same item, one per unit disposed.
- **Bandages** return a rag; a **Battery** returns electronics.
- **Craftable throwables** (molotov, can bomb, beeper bomb, box mine) return their surviving parts. The soaked rag wick and punctured cans are lost.
- **Craftable weapons, attachments and crafting parts** return one of their recipe inputs, chosen at random.
- **Ranged weapons** return scrap or firearm parts; **metal melee weapons** return scrap or melee parts. Wooden or primitive melee, and anything with no rule, dispose as before.

Weapon and material salvage is chance-based. Every rule has an on/off toggle, and every chance is a slider in the config. The full salvage table, every chance, and every config key are in [docs/dismantle-recipes.md](docs/dismantle-recipes.md).

## Config

The config file is `BepInEx\config\com.ivmakk.tlsa.dismantle.cfg`, written on first run. Edit it and restart the game to tune.

- `[Rules]` - one toggle per rule (`EnableConsumableByproducts`, `EnableCraftable`, `EnableRangedWeapons`, `EnableMeleeWeapons`, `EnableBattery`, `EnableThrowables`), all default on.
- `[Chances]` - the weapon and material chances (0 to 1), rendered as sliders by ConfigurationManager.
- `[General]` - `Verbose` logs each dispose and its salvage. Off by default; keep it off in normal play.

See [docs/dismantle-recipes.md](docs/dismantle-recipes.md) for what each key does and its default.

## Install

1. Install [BepInEx 6 (IL2CPP)](https://www.nexusmods.com/thelaststandaftermath/mods/1) for The Last Stand: Aftermath. Start the game once so BepInEx finishes setup, then quit.
2. Extract this mod's zip into the game folder (the folder with the game .exe). The DLL lands in `BepInEx\plugins`. Full path examples:
   - Steam: `C:\Program Files (x86)\Steam\steamapps\common\The Last Stand Aftermath\BepInEx\plugins\Dismantle.dll`
   - Epic: `C:\Program Files\Epic Games\The Last Stand Aftermath\BepInEx\plugins\Dismantle.dll`
3. Start the game. Dispose an item from the inventory and the salvage appears.

Not working? Open `BepInEx\LogOutput.log` and look for the `Dismantle loaded` line.

## Uninstall

Delete `Dismantle.dll` from the `BepInEx\plugins` folder.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against the game's IL2CPP interop assemblies, so a working game install with BepInEx 6 set up is required. Those assemblies are game-derived and are not part of this repo.

```
dotnet build src/Dismantle.csproj -c Release
```

`Directory.Build.props` sets `GameDir` to the default Steam install path. If the game lives elsewhere, override it without editing the file: set a `GameDir` environment variable, or pass `-p:GameDir=...` on the build. The output DLL is at `src\bin\Release\Dismantle.dll`.

The salvage decision logic (`src/SalvageRules.cs`) is game-free and unit-tested under `tests/`:

```
dotnet test tests/Dismantle.Tests/Dismantle.Tests.csproj
```

## Package

Add `-p:Package=true` to a Release build to also produce the ready-to-install zip at `dist\Dismantle-<version>.zip`, laid out as `BepInEx\plugins\Dismantle.dll` so a user extracts it at the game root. A plain build skips this step.

```
dotnet build src/Dismantle.csproj -c Release -p:Package=true
```

## License

Licensed under the GNU General Public License v3.0. Copyright (C) 2026 ivmakk. See [LICENSE](LICENSE).

You may reuse and modify this mod, but you must keep it open under the same license and give credit. Do not reupload it without credit.
