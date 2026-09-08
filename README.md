# Dismantle

A mod for [*The Last Stand: Aftermath*](https://www.nexusmods.com/thelaststandaftermath) that lets you recover useful parts from unwanted gear through the inventory's **Dispose** action.

Keep the empty cans from unwanted food, turn a spare attachment into Scrap, or take a crafted weapon apart for a recipe ingredient. Use Dispose as usual, including the quantity or confirmation prompt. Any recovered parts go straight into your inventory with the game's usual "added to inventory" popup.

## What you get

- **Canned food and water** return their empty Can or Plastic Bottle, one per item disposed.
- **Bandages** return 1 Rag. **Batteries** have a 50% chance to return 1 Electronics each.
- **Craftable weapons, attachments and crafting parts** return one randomly chosen recipe ingredient. A weapon that can only be repaired (not crafted) returns Scrap or its weapon parts instead.
- **Guns without a crafting recipe** return 1 Scrap (90%) or 1 Firearm Parts (10%). **Metal melee weapons without a recipe** return 1 Scrap (95%) or 1 Melee Parts (5%).
- **Attachments without a crafting recipe** return 1 Scrap.
- **Molotovs and improvised bombs** have a chance to return Alcohol, Explosives or other parts.

Some items still give nothing, including a plain Bat or Board, ammo and non-craftable throwables. Batteries and improvised throwables can also return nothing if the salvage chance fails. Disposing still removes the item.

See the [full dismantling recipes and salvage chances](docs/dismantle-recipes.md) for item-by-item returns and stack examples.

## Install

1. Install [BepInEx 6 (IL2CPP)](https://www.nexusmods.com/thelaststandaftermath/mods/1) for The Last Stand: Aftermath. Start the game once so BepInEx finishes setup, then quit.
2. Extract this mod's zip into the game folder (the folder with the game .exe). The DLL lands in `BepInEx\plugins`. Full path examples:
   - Steam: `C:\Program Files (x86)\Steam\steamapps\common\The Last Stand Aftermath\BepInEx\plugins\Dismantle.dll`
   - Epic: `C:\Program Files\Epic Games\The Last Stand Aftermath\BepInEx\plugins\Dismantle.dll`
3. Start the game and load a save.

**Example:** Dispose of a non-craftable attachment to recover 1 Scrap.

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
