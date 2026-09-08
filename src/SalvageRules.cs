using System;
using System.Collections.Generic;

namespace Dismantle;

// The salvage decision, with no game types. Everything here works on plain ids, strings and
// numbers, so it runs in a unit test with a scripted RNG. The game adapter (Salvage in Plugin.cs)
// reads ItemModel / recipe data into a SalvageInput, calls Collect, then resolves the output ids
// back to game items and gives them. Keep this file free of BepInEx and Il2Cpp references; the test
// project links it directly.

// One salvage output: an item id, how many per disposed unit, and the chance each is returned.
// A deterministic output (a consumable byproduct, a picked recipe input) uses chance 1.
public struct SalvageOutput
{
    public string Id;
    public int PerUnit;
    public double Chance;
    public SalvageOutput(string id, int perUnit, double chance) { Id = id; PerUnit = perUnit; Chance = chance; }
}

// The disposed item as plain data. Byproducts and RecipeInputs are null or empty when the item has
// none. Amount is the per-unit count for a byproduct and the recipe quantity for an input.
public readonly struct SalvageInput
{
    public readonly string Id;
    public readonly string Category;
    public readonly IReadOnlyList<(string Id, int Amount)> Byproducts;
    public readonly IReadOnlyList<(string Id, int Amount)> RecipeInputs;
    // True when RecipeInputs come from a repair recipe (its inputs include the item's own damaged
    // version). Such an item skips the craftable pick and falls through to the category rule, so a
    // salvage never returns a broken weapon.
    public readonly bool RecipeIsRepair;

    public SalvageInput(
        string id,
        string category,
        IReadOnlyList<(string Id, int Amount)> byproducts,
        IReadOnlyList<(string Id, int Amount)> recipeInputs,
        bool recipeIsRepair = false)
    {
        Id = id;
        Category = category;
        Byproducts = byproducts;
        RecipeInputs = recipeInputs;
        RecipeIsRepair = recipeIsRepair;
    }
}

// The salvage chances and rule toggles, copied from the BepInEx config each time.
public struct SalvageConfig
{
    public bool EnableConsumableByproducts;
    public bool EnableCraftable;
    public bool EnableRangedWeapons;
    public bool EnableMeleeWeapons;
    public bool EnableBattery;
    public bool EnableThrowables;
    public bool EnableAttachments;

    public float RangedScrapChance;
    public float RangedGearChance;
    public float RangedFirearmPartsChance;
    public float MeleeScrapChance;
    public float MeleeTapeChance;
    public float MeleePartsChance;
    public float BatteryElectronicsChance;
    public float MolotovChance;
    public float CanBombChance;
    public float BeeperBombChance;
    public float BoxMineChance;
    public float AttachmentScrapChance;
}

// A random source the caller supplies, so a test can script the rolls. The game uses System.Random;
// a test uses a fake that returns a fixed sequence.
public interface IRng
{
    double NextDouble();
    int Next(int maxExclusive);
}

public sealed class SystemRng : IRng
{
    private readonly Random _r;
    public SystemRng() { _r = new Random(); }
    public SystemRng(int seed) { _r = new Random(seed); }
    public double NextDouble() => _r.NextDouble();
    public int Next(int maxExclusive) => _r.Next(maxExclusive);
}

public static class SalvageRules
{
    // Melee weapons with no real metal to recover. These return nothing. Everything else in the
    // meleeWeapon category (that is not craftable) is treated as metal.
    private static readonly HashSet<string> WoodenMelee = new HashSet<string>
    {
        "MeleeBatWood", "MeleeBoard", "MeleeUnarmed",
    };

    // Craftable categories that return one of their recipe inputs. Craftable medical (bandages
    // aside, handled by the explicit rule), throwables and ammo are not here, so they return
    // nothing. Misc covers the crafting-part upgrades (firearm/melee parts).
    private static readonly HashSet<string> CraftableCategories = new HashSet<string>
    {
        "rangedWeapon", "meleeWeapon", "attachment", "misc",
    };

    // Rules in priority order; the first that matches wins. A consumable gives its byproduct; a
    // specific item override wins next; a craftable gives one of its recipe inputs; otherwise the
    // item's category decides.
    public static List<SalvageOutput> Collect(in SalvageInput input, in SalvageConfig cfg, IRng rng)
    {
        var outputs = new List<SalvageOutput>();

        CollectConsumable(input, cfg, outputs);
        if (outputs.Count > 0)
        {
            return outputs;
        }
        if (CollectExplicit(input, cfg, outputs))
        {
            return outputs;
        }
        if (CollectCraftable(input, cfg, rng, outputs))
        {
            return outputs;
        }
        CollectRanged(input, cfg, rng, outputs);
        CollectMelee(input, cfg, rng, outputs);
        CollectAttachment(input, cfg, outputs);
        return outputs;
    }

    // A consumable's use-time byproducts (a can, a bottle). Deterministic (chance 1): the byproduct
    // is always returned, one item per unit disposed.
    private static void CollectConsumable(in SalvageInput input, in SalvageConfig cfg, List<SalvageOutput> outputs)
    {
        if (!cfg.EnableConsumableByproducts || input.Byproducts == null)
        {
            return;
        }
        for (int i = 0; i < input.Byproducts.Count; i++)
        {
            var (id, amount) = input.Byproducts[i];
            if (id != null && amount > 0)
            {
                outputs.Add(new SalvageOutput(id, amount, 1.0));
            }
        }
    }

    // Specific per-item salvage. Runs before the craftable and category rules so it overrides them:
    // Bandages craft from a rag, but a dispose returns the full rag (100%), not the craftable pick.
    // Craftable throwables also live here, not in the craftable pick, because only their durable
    // parts survive: a molotov's alcohol (the rag wick is a soaked loss), a bomb's explosives and
    // metal/electronics (the punctured can is a loss). Each surviving part is an independent roll,
    // so a bomb can give both parts, one, or nothing. Returns true when the item matched, so the
    // later rules do not also run.
    private static bool CollectExplicit(in SalvageInput input, in SalvageConfig cfg, List<SalvageOutput> outputs)
    {
        switch (input.Id)
        {
            case "Bandages":
                Add(outputs, "Rag", 1.0f);
                return true;
            case "Battery":
                if (cfg.EnableBattery)
                {
                    Add(outputs, "Electronics", cfg.BatteryElectronicsChance);
                }
                return true;
            case "MolotovCocktail":
                if (cfg.EnableThrowables)
                {
                    Add(outputs, "Alcohol", cfg.MolotovChance);
                }
                return true;
            case "CanBomb":
                if (cfg.EnableThrowables)
                {
                    Add(outputs, "Explosive", cfg.CanBombChance);
                }
                return true;
            case "BeeperBomb":
                if (cfg.EnableThrowables)
                {
                    Add(outputs, "Explosive", cfg.BeeperBombChance);
                    Add(outputs, "Electronics", cfg.BeeperBombChance);
                }
                return true;
            case "BoxMine":
                if (cfg.EnableThrowables)
                {
                    Add(outputs, "Explosive", cfg.BoxMineChance);
                    Add(outputs, "Scrap", cfg.BoxMineChance);
                }
                return true;
            default:
                return false;
        }
    }

    // A craftable weapon, attachment or crafting part returns exactly one of its recipe inputs,
    // chosen at random (never both, never nothing). Returns true when the item is a covered
    // craftable (so category rules do not also run); returns false when it has no recipe or its
    // category is not covered, so the caller falls through.
    private static bool CollectCraftable(in SalvageInput input, in SalvageConfig cfg, IRng rng, List<SalvageOutput> outputs)
    {
        if (!cfg.EnableCraftable)
        {
            return false;
        }
        if (input.Category == null || !CraftableCategories.Contains(input.Category))
        {
            return false;
        }
        if (input.RecipeInputs == null || input.RecipeInputs.Count == 0)
        {
            return false;
        }
        // A repair recipe's inputs include the item's own damaged version. Never salvage that: fall
        // through so a weapon uses the category rule (scrap or parts) instead of returning a broken one.
        if (input.RecipeIsRepair)
        {
            return false;
        }
        var pick = input.RecipeInputs[rng.Next(input.RecipeInputs.Count)];
        if (pick.Id != null && pick.Amount > 0)
        {
            outputs.Add(new SalvageOutput(pick.Id, pick.Amount, 1.0));
        }
        return true;
    }

    // A non-craftable ranged weapon returns at most one material - scrap, gear or firearm parts -
    // chosen by weight. Returning two (gear plus firearm parts) would unbalance: gear is rare and
    // crafts into parts.
    private static void CollectRanged(in SalvageInput input, in SalvageConfig cfg, IRng rng, List<SalvageOutput> outputs)
    {
        if (!cfg.EnableRangedWeapons || input.Category != "rangedWeapon")
        {
            return;
        }
        AddWeightedOne(outputs, rng,
            ("Scrap", cfg.RangedScrapChance),
            ("Gear", cfg.RangedGearChance),
            ("FirearmUpgrade", cfg.RangedFirearmPartsChance));
    }

    // A non-craftable metal melee weapon returns at most one material - scrap, tape or melee parts -
    // chosen by weight. Wooden/primitive melee (no real metal) returns nothing.
    private static void CollectMelee(in SalvageInput input, in SalvageConfig cfg, IRng rng, List<SalvageOutput> outputs)
    {
        if (!cfg.EnableMeleeWeapons || input.Category != "meleeWeapon" || WoodenMelee.Contains(input.Id))
        {
            return;
        }
        AddWeightedOne(outputs, rng,
            ("Scrap", cfg.MeleeScrapChance),
            ("Tape", cfg.MeleeTapeChance),
            ("MeleeUpgrade", cfg.MeleePartsChance));
    }

    // A non-craftable attachment (looted suppressors, scopes, sights, magazines, ammo mods) returns
    // scrap. Craftable attachments never reach here: the craftable rule matches them first and
    // returns, so this rule only sees the looted ones.
    private static void CollectAttachment(in SalvageInput input, in SalvageConfig cfg, List<SalvageOutput> outputs)
    {
        if (!cfg.EnableAttachments || input.Category != "attachment")
        {
            return;
        }
        Add(outputs, "Scrap", cfg.AttachmentScrapChance);
    }

    // The give-time stack math, pure. For each output the amount is the expected value
    // PerUnit * quantity * Chance, given as its floor plus one extra with the fractional
    // probability. A deterministic output (chance 1) gives exactly PerUnit * quantity and consumes
    // no roll. Outputs that roll to 0 are dropped.
    public static List<(string Id, int Count)> RollQuantities(List<SalvageOutput> outputs, int quantity, IRng rng)
    {
        var result = new List<(string Id, int Count)>();
        if (outputs == null || outputs.Count == 0 || quantity <= 0)
        {
            return result;
        }
        foreach (var output in outputs)
        {
            double expected = (double)output.PerUnit * quantity * output.Chance;
            int total = (int)Math.Floor(expected);
            double frac = expected - total;
            if (frac > 0.0 && rng.NextDouble() < frac)
            {
                total++;
            }
            if (total > 0)
            {
                result.Add((output.Id, total));
            }
        }
        return result;
    }

    // Add one material output by id, if the chance is positive. The chance is carried to the give
    // step and applied there over the stack.
    private static void Add(List<SalvageOutput> outputs, string id, float chance)
    {
        if (chance > 0f)
        {
            outputs.Add(new SalvageOutput(id, 1, chance));
        }
    }

    // Add at most one output, chosen by weight. Each weight is an absolute probability; the leftover
    // (1 - sum of weights) is the chance of nothing. One roll decides, so two items are never
    // returned together. Weights are expected to sum to at most 1; if they exceed 1, the later
    // choices are squeezed by the cumulative walk.
    private static void AddWeightedOne(List<SalvageOutput> outputs, IRng rng, params (string id, float weight)[] choices)
    {
        double roll = rng.NextDouble();
        double cumulative = 0.0;
        foreach (var (id, weight) in choices)
        {
            if (weight <= 0f)
            {
                continue;
            }
            cumulative += weight;
            if (roll < cumulative)
            {
                outputs.Add(new SalvageOutput(id, 1, 1.0));
                return;
            }
        }
    }
}
