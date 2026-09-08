using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DeadReckoned.Core.Events;
using DeadReckoned.Localization;
using Game;
using Game.Data.Collections;
using Game.Data.Items;
using Game.Data.Items.Models;
using Game.Data.Models.Actors.Actions;
using Game.Data.Models.Crafting;
using Game.Data.States;
using Game.UI.Missions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Dismantle;

// Turns the inventory "Dispose" action into a salvage. Rules in priority order: a consumable
// gives the byproduct it leaves on use (a can, a bottle); a specific item override wins next
// (bandages give a rag, a battery gives electronics, a craftable throwable gives its surviving
// parts); a craftable weapon or attachment gives
// exactly one of its recipe inputs, at random; otherwise the category decides - a ranged weapon gives
// scrap/gear/firearm parts, a metal melee weapon gives scrap/tape/melee parts. Wooden melee and
// items with no rule dispose as before. Weapon and material salvage is chance-based.
// The UI label stays "Dispose".
//
// The decision logic lives in SalvageRules.cs (no game types), so it is unit-tested; this file is
// the game adapter that reads item/recipe data into it and gives the chosen outputs.
//
// Seam (confirmed by decompiling GameAssembly.dll, see decompile/NOTES.md): every dispose
// reduces the stack via Item.SetQuantity(int value, bool raiseChangeEvent) - the field-writing
// mutator - and that call sits INSIDE the two dispose commit handlers. SetQuantity fires for
// everything (eating, crafting, ammo), so it is gated by a synchronous flag: the commit
// handlers raise a "disposing" depth on entry and drop it on exit, and SetQuantity grants only
// while that depth is up. Because the call is synchronous inside the handler, the gate is exact
// - no timing window, no polling.
[BepInPlugin(PluginGuid, "Dismantle", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.ivmakk.tlsa.dismantle";
    internal static new ManualLogSource Log;

    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> EnableConsumableByproducts;
    internal static ConfigEntry<bool> EnableCraftable;
    internal static ConfigEntry<bool> EnableRangedWeapons;
    internal static ConfigEntry<bool> EnableMeleeWeapons;
    internal static ConfigEntry<bool> EnableBattery;
    internal static ConfigEntry<bool> EnableThrowables;
    internal static ConfigEntry<bool> EnableAttachments;

    internal static ConfigEntry<float> RangedScrapChance;
    internal static ConfigEntry<float> RangedGearChance;
    internal static ConfigEntry<float> RangedFirearmPartsChance;
    internal static ConfigEntry<float> MeleeScrapChance;
    internal static ConfigEntry<float> MeleeTapeChance;
    internal static ConfigEntry<float> MeleePartsChance;
    internal static ConfigEntry<float> BatteryElectronicsChance;
    internal static ConfigEntry<float> MolotovChance;
    internal static ConfigEntry<float> CanBombChance;
    internal static ConfigEntry<float> BeeperBombChance;
    internal static ConfigEntry<float> BoxMineChance;
    internal static ConfigEntry<float> AttachmentScrapChance;

    public override void Load()
    {
        Log = base.Log;

        Verbose = Config.Bind(
            "General", "Verbose", false,
            "Log each dispose: the disposed item id and category, the rule hit or miss, and the produced salvage. Keep off in normal play.");
        EnableConsumableByproducts = Config.Bind(
            "Rules", "EnableConsumableByproducts", true,
            "When true, disposing a consumable that leaves an item on use (a can, a bottle) gives that same item, one per unit disposed.");
        EnableCraftable = Config.Bind(
            "Rules", "EnableCraftable", true,
            "When true, disposing a craftable weapon, attachment or crafting part returns one of its recipe inputs, chosen at random.");
        EnableRangedWeapons = Config.Bind(
            "Rules", "EnableRangedWeapons", true,
            "When true, disposing a non-craftable ranged weapon returns scrap, gear and firearm parts at their configured chances.");
        EnableMeleeWeapons = Config.Bind(
            "Rules", "EnableMeleeWeapons", true,
            "When true, disposing a non-craftable metal melee weapon returns scrap, tape and melee parts at their configured chances. Wooden/primitive melee returns nothing.");
        EnableBattery = Config.Bind(
            "Rules", "EnableBattery", true,
            "When true, disposing a Battery returns electronics at BatteryElectronicsChance.");
        EnableThrowables = Config.Bind(
            "Rules", "EnableThrowables", true,
            "When true, disposing a craftable throwable returns its surviving parts: a molotov's alcohol, a bomb's explosives and metal/electronics. The soaked rag wick and punctured cans are lost. Non-craftable throwables (grenades, bricks) return nothing.");
        EnableAttachments = Config.Bind(
            "Rules", "EnableAttachments", true,
            "When true, disposing a non-craftable attachment (looted suppressors, scopes, sights, magazines, ammo mods) returns scrap at AttachmentScrapChance. Craftable attachments return one of their recipe inputs (EnableCraftable) instead.");

        // Salvage chances (0..1). A non-craftable weapon returns at most one material: the three
        // weights below are a single weighted pick, and their leftover (1 - their sum) is the
        // chance of nothing. For a stackable output (Battery) the expected return over a stack of
        // N is N * chance, given as floor(N*chance) plus one extra with the fractional probability.
        // Each chance is clamped to 0..1 on set, so an out-of-range edit cannot over- or under-give,
        // and the ConfigurationManager UI renders a slider from the range.
        ConfigEntry<float> BindChance(string key, float def, string desc) =>
            Config.Bind("Chances", key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(0f, 1f)));

        RangedScrapChance = BindChance("RangedScrapChance", 0.9f, "Weight that a non-craftable ranged weapon returns 1 scrap. Ranged weights share one pick; 0.9 + firearm parts 0.1 sums to 1, so a ranged dispose always returns one material.");
        RangedGearChance = BindChance("RangedGearChance", 0f, "Weight that a non-craftable ranged weapon returns 1 gear. Off by default; raise it to add gear to the pool.");
        RangedFirearmPartsChance = BindChance("RangedFirearmPartsChance", 0.1f, "Weight that a non-craftable ranged weapon returns 1 firearm parts.");
        MeleeScrapChance = BindChance("MeleeScrapChance", 0.95f, "Weight that a non-craftable metal melee weapon returns 1 scrap. Melee weights share one pick; 0.95 + melee parts 0.05 sums to 1, so a melee dispose always returns one material.");
        MeleeTapeChance = BindChance("MeleeTapeChance", 0f, "Weight that a non-craftable metal melee weapon returns 1 tape. Off by default; raise it to add tape to the pool.");
        MeleePartsChance = BindChance("MeleePartsChance", 0.05f, "Weight that a non-craftable metal melee weapon returns 1 melee parts.");
        BatteryElectronicsChance = BindChance("BatteryElectronicsChance", 0.5f, "Chance a disposed Battery returns 1 electronics.");
        MolotovChance = BindChance("MolotovChance", 0.75f, "Chance a disposed Molotov Cocktail returns 1 alcohol. The soaked rag wick is lost.");
        CanBombChance = BindChance("CanBombChance", 0.5f, "Chance a disposed Can Bomb returns 1 explosives. The punctured can is lost.");
        BeeperBombChance = BindChance("BeeperBombChance", 0.33f, "Chance a disposed Beeper Bomb returns each of 1 explosives and 1 electronics, rolled independently.");
        BoxMineChance = BindChance("BoxMineChance", 0.33f, "Chance a disposed Box Mine returns each of 1 explosives and 1 scrap, rolled independently.");
        AttachmentScrapChance = BindChance("AttachmentScrapChance", 1f, "Chance a disposed non-craftable attachment returns 1 scrap.");

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        DisposeGate.InstallHandlerHooks(harmony);

        Log.LogInfo("Dismantle loaded. Dispose now salvages consumables, craftables, weapons and batteries.");
    }
}

// The game adapter for the salvage rules. Reads item and recipe data from the game into the plain
// SalvageInput / SalvageConfig that SalvageRules works on, then resolves the chosen output ids back
// to game items and gives them. The decision logic itself lives in SalvageRules.cs (game-free).
public static class Salvage
{
    private static readonly IRng s_rng = new SystemRng();

    // Name -> ItemModel, scanned once. Salvage output ids are resolved here.
    private static Dictionary<string, ItemModel> s_models;
    // Output item name -> its recipe inputs (id, qty), built once from CraftingRecipeModel.AllRecipes.
    private static Dictionary<string, List<(string id, int qty)>> s_recipes;
    // Output names whose recipe is a repair (an input is the item's own "<name>_Damaged" version).
    private static HashSet<string> s_repairOutputs;

    // Decide the salvage for a disposed model. Reads the game data into a SalvageInput and runs the
    // rules. The outputs carry ids only; Give resolves them.
    public static List<SalvageOutput> Collect(ItemModel model)
    {
        var input = new SalvageInput(
            model.name,
            model.Category?.Identifier,
            ReadByproducts(model),
            ReadRecipeInputs(model.name),
            IsRepairRecipe(model.name));
        return SalvageRules.Collect(input, CurrentConfig(), s_rng);
    }

    // Give the salvage to the active survivor. Rolls the per-stack amounts, resolves each output id
    // to a game item, gives it, and raises the "added to inventory" toast.
    public static void Give(List<SalvageOutput> outputs, int quantity)
    {
        var survivor = GameManager.ActiveGameState?.ActiveCharacterState;
        if (survivor == null)
        {
            Plugin.Log.LogWarning("Dismantle: no active survivor, salvage dropped.");
            return;
        }
        var rolled = SalvageRules.RollQuantities(outputs, quantity, s_rng);
        foreach (var (id, count) in rolled)
        {
            var model = ModelByName(id);
            if (model == null)
            {
                continue; // ModelByName logs the miss
            }
            // Use the game's factory, not new Item(model): the factory picks the correct Item
            // subtype for the model (a WeaponItem for a weapon, and so on). A plain Item for a
            // weapon model makes the inventory UI throw on its Item->WeaponItem cast and freeze.
            var salvage = Item.Factory.Create(model, count);
            if (salvage == null)
            {
                Plugin.Log.LogWarning($"Dismantle: could not create item '{id}'; that salvage is skipped.");
                continue;
            }
            string name = salvage.LocalizedName; // capture before give: the item may merge into a stack on give
            survivor.GiveItem(salvage);
            Notify(name, count);
            if (Plugin.Verbose.Value)
            {
                Plugin.Log.LogInfo($"Dismantle: gave {id} x{count}");
            }
        }
    }

    // Snapshot the config toggles and chances into the plain struct the rules read.
    private static SalvageConfig CurrentConfig() => new SalvageConfig
    {
        EnableConsumableByproducts = Plugin.EnableConsumableByproducts.Value,
        EnableCraftable = Plugin.EnableCraftable.Value,
        EnableRangedWeapons = Plugin.EnableRangedWeapons.Value,
        EnableMeleeWeapons = Plugin.EnableMeleeWeapons.Value,
        EnableBattery = Plugin.EnableBattery.Value,
        EnableThrowables = Plugin.EnableThrowables.Value,
        EnableAttachments = Plugin.EnableAttachments.Value,
        RangedScrapChance = Plugin.RangedScrapChance.Value,
        RangedGearChance = Plugin.RangedGearChance.Value,
        RangedFirearmPartsChance = Plugin.RangedFirearmPartsChance.Value,
        MeleeScrapChance = Plugin.MeleeScrapChance.Value,
        MeleeTapeChance = Plugin.MeleeTapeChance.Value,
        MeleePartsChance = Plugin.MeleePartsChance.Value,
        BatteryElectronicsChance = Plugin.BatteryElectronicsChance.Value,
        MolotovChance = Plugin.MolotovChance.Value,
        CanBombChance = Plugin.CanBombChance.Value,
        BeeperBombChance = Plugin.BeeperBombChance.Value,
        BoxMineChance = Plugin.BoxMineChance.Value,
        AttachmentScrapChance = Plugin.AttachmentScrapChance.Value,
    };

    // A consumable's use-time byproducts as (output id, count). Each CreateItemAction makes an item
    // (its m_Item descriptor): the exact item and count the game gives when the item is used.
    // Returns null when the model is not a consumable or leaves nothing.
    private static List<(string, int)> ReadByproducts(ItemModel model)
    {
        var consumable = model.TryCast<ConsumableItemModel>();
        var actions = consumable?.Actions;
        if (actions == null)
        {
            return null;
        }
        List<(string, int)> list = null;
        for (int i = 0; i < actions.Count; i++)
        {
            var create = actions[i]?.TryCast<CreateItemAction>();
            var desc = create?.m_Item;
            var outModel = desc?.Model;
            int perUnit = desc != null ? desc.Quantity : 0;
            if (outModel != null && perUnit > 0)
            {
                (list ??= new List<(string, int)>()).Add((outModel.name, perUnit));
            }
        }
        return list;
    }

    // The recipe inputs for an output item name, or null when it has no recipe.
    private static IReadOnlyList<(string, int)> ReadRecipeInputs(string name)
    {
        var recipes = Recipes();
        if (recipes != null && recipes.TryGetValue(name, out var inputs))
        {
            return inputs;
        }
        return null;
    }

    // True when the item's recipe is a repair recipe (an input is its own "<name>_Damaged" version).
    // Builds the recipe map on first use, so the repair set is ready alongside it.
    private static bool IsRepairRecipe(string name)
    {
        Recipes();
        return s_repairOutputs != null && s_repairOutputs.Contains(name);
    }

    // Raise the game's "<item> added to inventory" toast - the same one shown when a consumable
    // leaves a byproduct on use. Mirrors CreateItemAction.Execute: pull a pooled event from the
    // cache, set its text from the notification_itemadded localization key, and raise it. With no
    // listener (dispose outside a mission) the raise is a no-op, so it is safe everywhere.
    // The game's own toast is always single-unit, so it carries no count; for a stack dispose we
    // append "xN" to the name (the game shows stack counts as separate UI fields, never in a
    // toast phrase, so there is no native format to reuse here).
    private static void Notify(string localizedName, int total)
    {
        try
        {
            if (string.IsNullOrEmpty(localizedName))
            {
                return;
            }
            string label = total > 1 ? $"{localizedName} x{total}" : localizedName;
            string text = Localization.Get(LocalizationKey.notification_itemadded, (Il2CppSystem.String)label);
            var evt = EventCache.Get<QueueTextNotificationEvent>();
            if (evt == null)
            {
                return;
            }
            var notif = evt.Notification;
            notif.LocalizationKey = null;
            notif.Text = text;
            notif.TextProvider = null;
            evt.Notification = notif;
            EventManager.Raise(evt.Cast<IEvent>());
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Dismantle: toast for '{localizedName}' failed: {e.Message}");
        }
    }

    // Resolve an item model by its asset name, scanning every loaded ItemModel once.
    private static ItemModel ModelByName(string name)
    {
        if (s_models == null)
        {
            s_models = new Dictionary<string, ItemModel>();
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ItemModel>());
            for (int i = 0; i < all.Count; i++)
            {
                var model = all[i]?.TryCast<ItemModel>();
                if (model != null && !s_models.ContainsKey(model.name))
                {
                    s_models[model.name] = model;
                }
            }
        }
        if (s_models.TryGetValue(name, out var found))
        {
            return found;
        }
        Plugin.Log.LogWarning($"Dismantle: item model '{name}' not found; that salvage is skipped.");
        return null;
    }

    // Build the output-name -> recipe-inputs map from the game's recipe data, once. Returns null
    // until the recipe data has loaded (it is ready by the time an item can be disposed in a
    // mission), so callers treat null as "no recipe" and fall through to the category rules.
    private static Dictionary<string, List<(string id, int qty)>> Recipes()
    {
        if (s_recipes != null)
        {
            return s_recipes;
        }
        var recipes = CraftingRecipeModel.AllRecipes;
        if (recipes == null || recipes.Count == 0)
        {
            return null;
        }
        var map = new Dictionary<string, List<(string id, int qty)>>();
        var repair = new HashSet<string>();
        for (int i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var outModel = recipe?.OutputItem?.Model;
            if (outModel == null)
            {
                continue;
            }
            string damagedName = outModel.name + "_Damaged";
            bool isRepair = false;
            var inputs = new List<(string id, int qty)>();
            int count = recipe.InputItemCount;
            for (int j = 0; j < count; j++)
            {
                var desc = recipe.GetInputItemDescriptor(j);
                var inModel = desc?.Model;
                int qty = desc != null ? desc.Quantity : 0;
                if (inModel != null && qty > 0)
                {
                    inputs.Add((inModel.name, qty));
                    if (inModel.name == damagedName)
                    {
                        isRepair = true;
                    }
                }
            }
            if (inputs.Count > 0)
            {
                map[outModel.name] = inputs;
                if (isRepair)
                {
                    repair.Add(outModel.name);
                }
            }
        }
        s_recipes = map;
        s_repairOutputs = repair;
        return s_recipes;
    }
}

// The synchronous dispose gate. The two dispose commit handlers raise Depth on entry and drop
// it on exit; the SetQuantity that reduces the stack runs between, so it is captured exactly.
// Salvage is recorded while Depth is up and given when the outermost handler exits - a safe
// point, after the dispose (including Item.Dispose for a full stack) has fully committed.
public static class DisposeGate
{
    public static int Depth;

    private struct Pending
    {
        public List<SalvageOutput> Outputs;
        public int Quantity;
    }

    private static readonly List<Pending> s_pending = new List<Pending>();

    public static void InstallHandlerHooks(Harmony harmony)
    {
        Hook(harmony, "Game.UI.Missions.Dialogs.DisposeItemDialog+__c__DisplayClass1_0", "_QuantityPrompt_b__0");
        Hook(harmony, "Game.UI.Missions.Dialogs.DisposeItemDialog+__c__DisplayClass2_0", "_ConfirmPrompt_b__1");
    }

    private static void Hook(Harmony harmony, string typeName, string method)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            var target = type != null ? AccessTools.Method(type, method) : null;
            if (target == null)
            {
                Plugin.Log.LogWarning($"Dismantle: could not hook {typeName}.{method}; dispose salvage will not fire.");
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(DisposeGate), nameof(Enter)),
                postfix: new HarmonyMethod(typeof(DisposeGate), nameof(Exit)));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Dismantle: failed to hook {typeName}.{method}: {e.Message}");
        }
    }

    public static void Enter()
    {
        Depth++;
    }

    public static void Exit()
    {
        if (Depth > 0)
        {
            Depth--;
        }
        if (Depth != 0 || s_pending.Count == 0)
        {
            return;
        }
        var batch = s_pending.ToArray();
        s_pending.Clear();
        foreach (var p in batch)
        {
            // Guard each give: Exit runs as a Harmony postfix on the dispose handler, so an escaping
            // exception would cancel the remaining postfixes in the game's chain. Log and continue.
            try
            {
                Salvage.Give(p.Outputs, p.Quantity);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Dismantle: give failed, salvage dropped: {e.Message}");
            }
        }
    }

    // Record salvage for a disposed model. Called from the SetQuantity prefix while a dispose
    // handler is on the stack. The grant is flushed by Exit after the dispose commits.
    public static void Record(ItemModel model, int disposed)
    {
        if (model == null || disposed <= 0)
        {
            return;
        }
        var outputs = Salvage.Collect(model);
        if (outputs.Count == 0)
        {
            return;
        }
        s_pending.Add(new Pending { Outputs = outputs, Quantity = disposed });
        if (Plugin.Verbose.Value)
        {
            Plugin.Log.LogInfo($"dispose {model.name} x{disposed}: {outputs.Count} rule(s) hit");
        }
    }
}

// The real dispose mutator. Bind the overload explicitly by arg types - SetQuantity is private
// in the game, and a name-only patch does not resolve it. Grant only inside a dispose handler
// (DisposeGate.Depth > 0), so eating, crafting, and ammo use are ignored. disposed = old - new.
[HarmonyPatch(typeof(Item), "SetQuantity", new Type[] { typeof(int), typeof(bool) })]
public static class SetQuantityPatch
{
    [HarmonyPrefix]
    public static void Prefix(Item __instance, int value)
    {
        try
        {
            if (DisposeGate.Depth <= 0 || __instance == null)
            {
                return;
            }
            int old = __instance.Quantity;
            if (value >= old)
            {
                return; // not a reduction
            }
            DisposeGate.Record(__instance.Model, old - value);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Dismantle SetQuantity hook failed: {e.Message}");
        }
    }
}
