using System;
using System.Collections.Generic;
using Dismantle;
using Xunit;

namespace Dismantle.Tests;

// A scripted RNG: returns the doubles and ints it was given, in order. Throws if the code asks for
// more than were scripted, which keeps a test honest about how many rolls a path consumes.
internal sealed class FakeRng : IRng
{
    private readonly Queue<double> _doubles;
    private readonly Queue<int> _ints;

    public FakeRng(double[] doubles = null, int[] ints = null)
    {
        _doubles = new Queue<double>(doubles ?? Array.Empty<double>());
        _ints = new Queue<int>(ints ?? Array.Empty<int>());
    }

    public double NextDouble()
        => _doubles.Count > 0 ? _doubles.Dequeue() : throw new InvalidOperationException("FakeRng: no more doubles scripted");

    public int Next(int maxExclusive)
        => _ints.Count > 0 ? _ints.Dequeue() : throw new InvalidOperationException("FakeRng: no more ints scripted");
}

public class SalvageRulesTests
{
    private static SalvageConfig DefaultConfig() => new SalvageConfig
    {
        EnableConsumableByproducts = true,
        EnableCraftable = true,
        EnableRangedWeapons = true,
        EnableMeleeWeapons = true,
        EnableBattery = true,
        EnableThrowables = true,
        EnableAttachments = true,
        RangedScrapChance = 0.5f,
        RangedGearChance = 0.25f,
        RangedFirearmPartsChance = 0.1f,
        MeleeScrapChance = 0.5f,
        MeleeTapeChance = 0.125f,
        MeleePartsChance = 0.05f,
        BatteryElectronicsChance = 0.5f,
        MolotovChance = 0.5f,
        CanBombChance = 0.5f,
        BeeperBombChance = 0.33f,
        BoxMineChance = 0.33f,
        AttachmentScrapChance = 1f,
    };

    private static SalvageInput Input(
        string id,
        string category,
        (string, int)[] byproducts = null,
        (string, int)[] recipe = null)
        => new SalvageInput(id, category, byproducts, recipe);

    // A rng that must not be touched. Used to assert a path consumes no rolls.
    private static FakeRng NoRolls() => new FakeRng();

    // ---- Priority order (first matching rule wins) ----

    [Fact]
    public void Consumable_byproduct_wins_over_category()
    {
        // A ranged category is set, but a byproduct is present: the consumable rule fires first and
        // the category rule never rolls (so an empty rng is safe).
        var input = Input("Food-CannedBeans", "rangedWeapon", byproducts: new[] { ("Can", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Can", o.Id);
        Assert.Equal(1, o.PerUnit);
        Assert.Equal(1.0, o.Chance);
    }

    [Fact]
    public void Explicit_bandages_wins_over_craftable()
    {
        // Bandages have a recipe (a rag), but the explicit rule returns the whole rag at 100% and
        // the craftable rule never runs.
        var input = Input("Bandages", "medical", recipe: new[] { ("Rag", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Rag", o.Id);
        Assert.Equal(1.0, o.Chance);
    }

    [Fact]
    public void Craftable_wins_over_category()
    {
        // A craftable ranged weapon returns one recipe input, not a scrap/gear category pick.
        var input = Input("ImprovisedShotgun", "rangedWeapon", recipe: new[] { ("Can", 1), ("FirearmUpgrade", 1) });

        // rng.Next(2) -> index 1 picks the second input; no NextDouble consumed.
        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(ints: new[] { 1 }));

        var o = Assert.Single(outputs);
        Assert.Equal("FirearmUpgrade", o.Id);
        Assert.Equal(1, o.PerUnit);
        Assert.Equal(1.0, o.Chance);
    }

    [Fact]
    public void Craftable_pick_index_zero_selects_first_input()
    {
        var input = Input("ImprovisedShotgun", "rangedWeapon", recipe: new[] { ("Can", 1), ("FirearmUpgrade", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(ints: new[] { 0 }));

        Assert.Equal("Can", Assert.Single(outputs).Id);
    }

    // ---- Craftable category scope ----

    [Fact]
    public void Craftable_medical_returns_nothing()
    {
        // A craftable medical item (not Bandages) is outside the craftable whitelist and matches no
        // category rule, so it returns nothing.
        var input = Input("SomeMedkit", "medical", recipe: new[] { ("Rag", 2) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        Assert.Empty(outputs);
    }

    [Fact]
    public void Craftable_misc_returns_one_input()
    {
        // Misc covers the crafting-part upgrades. Firearm Parts craft from scrap + gear.
        var input = Input("FirearmUpgrade", "misc", recipe: new[] { ("Scrap", 1), ("Gear", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(ints: new[] { 0 }));

        Assert.Equal("Scrap", Assert.Single(outputs).Id);
    }

    // ---- Weighted single pick (ranged) ----

    [Theory]
    [InlineData(0.4, "Scrap")]         // < 0.5
    [InlineData(0.6, "Gear")]          // in [0.5, 0.75)
    [InlineData(0.8, "FirearmUpgrade")] // in [0.75, 0.85)
    public void Ranged_weighted_pick_selects_by_roll(double roll, string expected)
    {
        var input = Input("Rifle", "rangedWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(new[] { roll }));

        var o = Assert.Single(outputs);
        Assert.Equal(expected, o.Id);
        Assert.Equal(1, o.PerUnit);
        Assert.Equal(1.0, o.Chance); // the pick already happened; give is deterministic
    }

    [Fact]
    public void Ranged_weighted_pick_can_return_nothing()
    {
        // A roll past the summed weights (0.85) lands in the "nothing" band.
        var input = Input("Rifle", "rangedWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(new[] { 0.9 }));

        Assert.Empty(outputs);
    }

    [Fact]
    public void Ranged_returns_at_most_one_item()
    {
        // Any single roll yields 0 or 1 outputs, never two rare parts together.
        var input = Input("Rifle", "rangedWeapon");
        foreach (var roll in new[] { 0.0, 0.3, 0.5, 0.74, 0.84, 0.99 })
        {
            var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(new[] { roll }));
            Assert.True(outputs.Count <= 1);
        }
    }

    // ---- Melee ----

    [Fact]
    public void Wooden_melee_returns_nothing()
    {
        var input = Input("MeleeBatWood", "meleeWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        Assert.Empty(outputs);
    }

    [Fact]
    public void Metal_melee_returns_weighted_pick()
    {
        var input = Input("MeleePipe", "meleeWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(new[] { 0.4 }));

        Assert.Equal("Scrap", Assert.Single(outputs).Id);
    }

    // ---- Attachments ----

    [Fact]
    public void Noncraftable_attachment_returns_scrap()
    {
        var input = Input("AttachSuppressorAR", "attachment");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Scrap", o.Id);
        Assert.Equal(1.0, o.Chance);
    }

    [Fact]
    public void Noncraftable_attachment_returns_nothing_when_disabled()
    {
        var cfg = DefaultConfig();
        cfg.EnableAttachments = false;
        var input = Input("AttachSuppressorAR", "attachment");

        var outputs = SalvageRules.Collect(input, cfg, NoRolls());

        Assert.Empty(outputs);
    }

    [Fact]
    public void Craftable_attachment_returns_recipe_input_not_scrap_rule()
    {
        // A craftable attachment matches the craftable rule first (one of its inputs), so the
        // non-craftable scrap rule never runs for it.
        var input = Input("AttachSuppressorOilFilter", "attachment", recipe: new[] { ("OilFilter", 1), ("Scrap", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), new FakeRng(ints: new[] { 0 }));

        var o = Assert.Single(outputs);
        Assert.Equal("OilFilter", o.Id);
        Assert.Equal(1.0, o.Chance);
    }

    // ---- Explicit battery, with its chance carried to the give step ----

    [Fact]
    public void Battery_returns_electronics_with_configured_chance()
    {
        var input = Input("Battery", "misc");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Electronics", o.Id);
        Assert.Equal(0.5, o.Chance); // not rolled at collect; applied in RollQuantities
    }

    // ---- Throwables (explicit per-item; only durable parts survive) ----

    [Fact]
    public void Molotov_returns_alcohol_only()
    {
        // The rag is a soaked wick, so it is never returned; only the alcohol survives.
        var input = Input("MolotovCocktail", "throwableWeapon", recipe: new[] { ("Rag", 1), ("Alcohol", 1) });

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Alcohol", o.Id);
        Assert.Equal(0.5, o.Chance); // carried to the give step, not rolled here
    }

    [Fact]
    public void CanBomb_returns_explosives_only()
    {
        // The punctured can is a loss; the explosive charge survives.
        var input = Input("CanBomb", "throwableWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        var o = Assert.Single(outputs);
        Assert.Equal("Explosive", o.Id);
        Assert.Equal(0.5, o.Chance);
    }

    [Fact]
    public void BeeperBomb_returns_explosives_and_electronics_independently()
    {
        // Two independent outputs: the charge and the trigger. Each rolls on its own at give time.
        var input = Input("BeeperBomb", "throwableWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        Assert.Equal(2, outputs.Count);
        Assert.Contains(outputs, o => o.Id == "Explosive" && o.Chance == 0.33f);
        Assert.Contains(outputs, o => o.Id == "Electronics" && o.Chance == 0.33f);
    }

    [Fact]
    public void BoxMine_returns_explosives_and_scrap_independently()
    {
        var input = Input("BoxMine", "throwableWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        Assert.Equal(2, outputs.Count);
        Assert.Contains(outputs, o => o.Id == "Explosive" && o.Chance == 0.33f);
        Assert.Contains(outputs, o => o.Id == "Scrap" && o.Chance == 0.33f);
    }

    [Fact]
    public void Throwable_toggle_off_returns_nothing()
    {
        var cfg = DefaultConfig();
        cfg.EnableThrowables = false;
        var input = Input("MolotovCocktail", "throwableWeapon", recipe: new[] { ("Rag", 1), ("Alcohol", 1) });

        var outputs = SalvageRules.Collect(input, cfg, NoRolls());

        Assert.Empty(outputs);
    }

    [Fact]
    public void Noncraftable_throwable_returns_nothing()
    {
        // A grenade has no explicit rule and no category rule (throwableWeapon is not covered), so
        // it disposes for nothing.
        var input = Input("Grenade", "throwableWeapon");

        var outputs = SalvageRules.Collect(input, DefaultConfig(), NoRolls());

        Assert.Empty(outputs);
    }

    // ---- Toggles ----

    [Fact]
    public void Ranged_toggle_off_returns_nothing()
    {
        var cfg = DefaultConfig();
        cfg.EnableRangedWeapons = false;
        var input = Input("Rifle", "rangedWeapon");

        var outputs = SalvageRules.Collect(input, cfg, NoRolls());

        Assert.Empty(outputs);
    }

    [Fact]
    public void Craftable_toggle_off_falls_through_to_category()
    {
        // With craftable off, a ranged weapon that has a recipe still gets the category pick.
        var cfg = DefaultConfig();
        cfg.EnableCraftable = false;
        var input = Input("ImprovisedShotgun", "rangedWeapon", recipe: new[] { ("Can", 1), ("FirearmUpgrade", 1) });

        var outputs = SalvageRules.Collect(input, cfg, new FakeRng(new[] { 0.4 }));

        Assert.Equal("Scrap", Assert.Single(outputs).Id);
    }

    // ---- Stack math (RollQuantities) ----

    [Fact]
    public void Deterministic_output_scales_by_quantity_without_rolling()
    {
        var outputs = new List<SalvageOutput> { new SalvageOutput("Can", 1, 1.0) };

        var rolled = SalvageRules.RollQuantities(outputs, 3, NoRolls());

        var r = Assert.Single(rolled);
        Assert.Equal("Can", r.Id);
        Assert.Equal(3, r.Count);
    }

    [Fact]
    public void PerUnit_multiplies_by_quantity()
    {
        var outputs = new List<SalvageOutput> { new SalvageOutput("Can", 2, 1.0) };

        var rolled = SalvageRules.RollQuantities(outputs, 2, NoRolls());

        Assert.Equal(4, Assert.Single(rolled).Count);
    }

    [Fact]
    public void Fractional_expectation_rounds_up_when_roll_below_frac()
    {
        // expected = 1 * 3 * 0.5 = 1.5 -> floor 1, frac 0.5; roll 0.4 < 0.5 rounds up to 2.
        var outputs = new List<SalvageOutput> { new SalvageOutput("Electronics", 1, 0.5) };

        var rolled = SalvageRules.RollQuantities(outputs, 3, new FakeRng(new[] { 0.4 }));

        Assert.Equal(2, Assert.Single(rolled).Count);
    }

    [Fact]
    public void Fractional_expectation_stays_down_when_roll_above_frac()
    {
        // Same 1.5 expectation; roll 0.6 >= 0.5 keeps the floor of 1.
        var outputs = new List<SalvageOutput> { new SalvageOutput("Electronics", 1, 0.5) };

        var rolled = SalvageRules.RollQuantities(outputs, 3, new FakeRng(new[] { 0.6 }));

        Assert.Equal(1, Assert.Single(rolled).Count);
    }

    [Fact]
    public void Zero_rolled_output_is_dropped()
    {
        // expected = 1 * 1 * 0.5 = 0.5 -> floor 0, frac 0.5; roll 0.6 keeps 0, so nothing is given.
        var outputs = new List<SalvageOutput> { new SalvageOutput("Electronics", 1, 0.5) };

        var rolled = SalvageRules.RollQuantities(outputs, 1, new FakeRng(new[] { 0.6 }));

        Assert.Empty(rolled);
    }
}
