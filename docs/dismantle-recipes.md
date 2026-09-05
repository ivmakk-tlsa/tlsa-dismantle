# Dismantle recipes

What the `Dismantle` mod gives you when you dispose an item. The mod turns the inventory "Dispose" action into a salvage: instead of destroying the item, it returns parts. The UI label stays "Dispose".

Salvage is decided by a set of rules in priority order. The first rule that matches an item wins, so an item never triggers two rules. Every rule has an on/off toggle in the config, and the weapon and material chances are configurable (see [Config](#config)). Item names below are the in-game display names; the id in code font is the internal asset id.

## Rules, in priority order

1. **Consumable byproduct** - a consumable that leaves an item on use returns that same item, one per unit disposed. Deterministic. Read from the item's own game data, so it covers every container-leaving consumable automatically.
2. **Bandages** - returns 1 Rag, always. This overrides the craftable rule below (Bandages craft from a rag, but a dispose returns the whole rag, not a chance).
3. **Battery** - returns 1 Electronics at 50%.
4. **Craftable throwable** - a molotov or improvised bomb returns only its surviving parts, each at its own chance. Consumed or altered inputs (a soaked rag wick, a punctured can) are lost. See [Throwables](#throwables-rule-4). Non-craftable throwables (grenades, stun grenades, bricks) return nothing.
5. **Craftable weapon, attachment, or crafting part** - returns exactly one of the item's crafting inputs, chosen at random. Never both inputs, never nothing. Applies only to the `rangedWeapon`, `meleeWeapon`, `attachment`, and `misc` categories. Craftable medical (Bandages aside) and ammo return nothing.
6. **Non-craftable ranged weapon** - returns one material by a weighted pick: Scrap 90%, Firearm Parts 10%. The weights sum to 1, so a ranged dispose always pays out (guns are rarer).
7. **Non-craftable metal melee weapon** - returns at most one material by a weighted pick: Scrap 50%, Melee Parts 5%, nothing 45%.
8. **Everything else** - wooden or primitive melee, and any item no rule matches, returns nothing (disposes as before).

The weapon and material rules are chance-based. Weapons do not stack, so each weapon dispose is a single roll. For a stackable output disposed as a stack of N, the amount is `floor(N * chance)` plus one extra with the leftover fractional probability, so the average over the stack matches the chance.

## Consumable byproducts (rule 1, deterministic)

Disposing one of these returns the listed item, the same one the game gives when you use it. The count is per unit: disposing 3 canned beans returns 3 cans.

| Disposed item | Salvage | Source item id | Salvage item id |
|---|---|---|---|
| Canned Beans | Can x1 | `Food-CannedBeans` | `Can` |
| Canned Fish | Can x1 | `Food-CannedFish` | `Can` |
| Canned Fruit | Can x1 | `Food-CannedFruit` | `Can` |
| Canned Meat | Can x1 | `Food-CannedMeat` | `Can` |
| Dog Food | Can x1 | `Food-DogFood` | `Can` |
| Rusty Canned Food | Can x1 | `Food-RustyCannedFood` | `Can` |
| Clean Water | Plastic Bottle x1 | `Food-CleanWater` | `PlasticBottle` |
| Contaminated Water | Plastic Bottle x1 | `Food-DirtyWater` | `PlasticBottle` |

## Fixed item recipes (rules 2 and 3)

| Disposed item | Salvage | Chance | Item id | Salvage item id |
|---|---|---|---|---|
| Bandages | Rag x1 | 100% | `Bandages` | `Rag` |
| Battery | Electronics x1 | 50% | `Battery` | `Electronics` |

## Throwables (rule 4)

A craftable throwable returns only the parts that survive taking it apart. A consumed or altered input is lost: a molotov's rag is a soaked wick, and a bomb's can is punctured and fused. So the salvage is not the full recipe. Each surviving part is an independent roll, so a bomb can return both parts, one, or nothing.

| Disposed item | Recipe | Salvage | Chance each | Config key | Lost |
|---|---|---|---|---|---|
| Molotov Cocktail | Rag + Alcohol | Alcohol x1 | 50% | `MolotovChance` | Rag (soaked wick) |
| Can Bomb | Can + Explosives | Explosives x1 | 50% | `CanBombChance` | Can (punctured) |
| Beeper Bomb | Can Bomb + Electronics | Explosives x1, Electronics x1 | 33% | `BeeperBombChance` | the can |
| Box Mine | Can Bomb + Scrap | Explosives x1, Scrap x1 | 33% | `BoxMineChance` | the can |

Beeper Bomb and Box Mine are built on a Can Bomb, so their salvage flattens that charge back to Explosives. The two outputs each roll on their own, so the pair is not "at most one". Keep Explosives chances low: it is the scarcest crafting ingredient, and three throwables can return it.

Non-craftable throwables (Grenade, Stun Grenade, Brick, Bottle, Torch, Chem-Light) return nothing.

## Craftable items (rule 5)

A craftable weapon, attachment, or crafting part returns exactly one of its crafting inputs, chosen at random (each input equally likely). The mod reads the recipes live from the game, so the set follows game updates with no per-item list here. The inputs are the same ones the game's own crafting menu shows for that item.

Covered categories: `rangedWeapon`, `meleeWeapon`, `attachment`, `misc`. The `misc` case is the crafting-part upgrades: Firearm Parts returns one of Scrap or Gear, Melee Parts returns one of Scrap or Tape.

Not covered (return nothing): craftable medical (except Bandages, rule 2) and ammo. Throwables have their own rule (rule 4).

Examples:

| Disposed item | Crafting inputs | Salvage | Item id |
|---|---|---|---|
| Improvised Shotgun | Can + Firearm Parts | one of Can, Firearm Parts | `ImprovisedShotgun` |
| Pipe Pistol | Pipe + Scrap | one of Pipe, Scrap | `PipePistol` |
| Spiked Bat | Bat + Melee Parts | one of Bat, Melee Parts | `MeleeBatSpiked` |
| Firearm Parts | Scrap + Gear | one of Scrap, Gear | `FirearmUpgrade` |
| Melee Parts | Scrap + Tape | one of Scrap, Tape | `MeleeUpgrade` |

## Non-craftable ranged weapons (rule 6)

Any ranged weapon that has no crafting recipe (the real guns) returns exactly one material, by a single weighted pick. The weights sum to 1, so a ranged dispose never comes back empty. Guns are rarer, so a dispose always pays out.

| Salvage | Chance | Salvage item id |
|---|---|---|
| Scrap x1 | 90% | `Scrap` |
| Firearm Parts x1 | 10% | `FirearmUpgrade` |

Ranged is decided by the item's category, not its id, so damaged variants are included.

Gear is off by default (`RangedGearChance` = 0), so the pool is Scrap or Firearm Parts. To add Gear back, raise `RangedGearChance` and lower `RangedScrapChance` by the same amount, so the weights still sum to 1.

## Non-craftable melee weapons (rule 7 and 8)

A non-craftable metal melee weapon returns at most one material, by a single weighted pick.

| Salvage | Chance | Salvage item id |
|---|---|---|
| Scrap x1 | 50% | `Scrap` |
| Melee Parts x1 | 5% | `MeleeUpgrade` |
| nothing | 45% | - |

Tape is off by default (`MeleeTapeChance` = 0), so the pool is Scrap, Melee Parts, or nothing. Raise `MeleeTapeChance` in the config to add Tape back to the pick.

Wooden or primitive melee weapons have no real metal and return nothing. This set is a fixed id list in the mod:

| Weapon | Item id |
|---|---|
| Bat | `MeleeBatWood` |
| Board | `MeleeBoard` |
| Unarmed | `MeleeUnarmed` |

## Config

The config file is `<GameDir>\BepInEx\config\com.ivmakk.tlsa.dismantle.cfg`. Edit it and restart the game to tune.

`[Rules]` - one toggle per rule: `EnableConsumableByproducts`, `EnableCraftable`, `EnableRangedWeapons`, `EnableMeleeWeapons`, `EnableBattery`, `EnableThrowables`. All default true.

`[Chances]` - the weights and chances (0 to 1):

| Key | Default |
|---|---|
| `RangedScrapChance` | 0.9 |
| `RangedGearChance` | 0 |
| `RangedFirearmPartsChance` | 0.1 |
| `MeleeScrapChance` | 0.5 |
| `MeleeTapeChance` | 0 |
| `MeleePartsChance` | 0.05 |
| `BatteryElectronicsChance` | 0.5 |
| `MolotovChance` | 0.5 |
| `CanBombChance` | 0.5 |
| `BeeperBombChance` | 0.33 |
| `BoxMineChance` | 0.33 |

For a ranged or melee weapon, the three material chances are the weights of one shared pick, so their sum is the chance of any salvage and the leftover is the chance of nothing. They do not stack.

`[General]` - `Verbose` logs each dispose (the rule hit and the salvage given). Keep it off in normal play.

## How this was gathered

The item names here are the in-game display names; the id in code font is the internal asset id. The mod reads all of this live from the game at runtime: consumable byproducts from each `ConsumableItemModel` (a `CreateItemAction` with an `ItemDescriptor`), and craftable inputs from `CraftingRecipeModel.AllRecipes`. So the tables follow game updates, and a craftable item's salvage always matches the inputs the game's own crafting menu shows.
