using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// Puts TCG Studio's custom sets into the Point And Slots mod (SebaYeta, plugin "Promo Rewards Mod").
    ///
    /// Its card catalog (PromoCardCatalog) only reads the eight base-game sets plus Enhanced Prefab Loader's sets, and
    /// gives up on all of them when EPL isn't loaded. It names rarities and sets by enum name, which for TCG Studio's
    /// values (sets 100+, rarities 1000+) is just a number. Postfixes here add each custom set's cards to the
    /// catalog (and the base-game cards when EPL is missing), count a custom set as unlocked when one of its pack
    /// licenses is, answer the real set and rarity names, and add new rarity names to its rarity settings.
    /// Everything is found by name, so neither mod is needed at build time.
    /// </summary>
    internal static class PointAndSlots
    {
        internal const string Guid = "SebaYeta.tcgshop.promorewards";
        private const string Catalog = "PromoRewardsMod.Core.PromoCardCatalog";
        private const string EntryType = "PromoRewardsMod.Core.PromoCatalogEntry";
        private const string Naming = "PromoRewardsMod.Core.RarityNaming";
        private const string Shop = "PromoRewardsMod.Core.PromoShopScreen";
        private const string Slots = "PromoRewardsMod.Core.PromoSlotScreen";
        private const string PluginType = "PromoRewardsMod.Plugin";
        private const string GroupName = "TCG Studio";

        private static Type _catalog = null!;
        private static Type _entry = null!;
        private static object? _augmented;
        private static readonly Dictionary<int, string> RarityNames = new Dictionary<int, string>();
        private static HashSet<int> _unlocked = new HashSet<int>();
        private static int _unlockedFrame = -1;

        public static void Install()
        {
            try
            {
                var catalog = AccessTools.TypeByName(Catalog);
                if (catalog == null || AccessTools.TypeByName(CustomTrades.StudioHook) == null) return; // Either mod missing: nothing to do.
                _catalog = catalog;
                _entry = AccessTools.TypeByName(EntryType);

                var harmony = new Harmony(Plugin.Guid);
                Patch(harmony, catalog, "GetAllCards", nameof(AfterGetAllCards), required: true);
                Patch(harmony, catalog, "IsExpansionUnlocked", nameof(AfterIsExpansionUnlocked), required: true);
                Patch(harmony, catalog, "GetUnlockedCards", nameof(AfterGetUnlockedCards));
                Patch(harmony, AccessTools.TypeByName(Naming), "GetPrettyName", nameof(AfterGetPrettyName), required: true);
                Patch(harmony, AccessTools.TypeByName(Shop), "PrettyExpansionName", nameof(AfterPrettyExpansionName));
                Patch(harmony, AccessTools.TypeByName(Shop), "GetModGroupFor", nameof(AfterGroup));
                Patch(harmony, AccessTools.TypeByName(Slots), "GetExpansionGroup", nameof(AfterGroup));
                var prettyName = AccessTools.Method(AccessTools.TypeByName(Slots), "PrettyName", new[] { typeof(string) });
                if (prettyName != null)
                    harmony.Patch(prettyName, prefix: new HarmonyMethod(typeof(PointAndSlots), nameof(BeforePrettyName)));
                Plugin.Log.LogInfo("TCG Studio sets added to Point And Slots.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook Point And Slots; TCG Studio sets stay out of it: " + e.Message);
            }
        }

        private static void Patch(Harmony harmony, Type? type, string method, string postfix, bool required = false)
        {
            var target = type == null ? null : AccessTools.Method(type, method);
            if (target == null)
            {
                if (required) throw new MissingMethodException((type?.Name ?? "?") + "." + method + " (a newer Point And Slots?)");
                Plugin.LogOnce("pas:" + method, $"Point And Slots has no {method}; custom set names may show as numbers there.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PointAndSlots), postfix));
        }

        /// <summary>
        /// Postfix on PromoCardCatalog.GetAllCards: once per catalog list, add the custom sets' cards, and the base-game
        /// cards when it has none. When EPL is missing the catalog comes back empty and uncached; then the list is cached.
        /// </summary>
        private static void AfterGetAllCards(object __result)
        {
            try
            {
                if (!(__result is IList list) || ReferenceEquals(list, _augmented)) return;
                var custom = GameReader.CustomShownLists().ToList();
                if (custom.Count == 0) return; // Not in a shop yet (or no sets): try again next call.

                bool eplMissing = Field("_cache").GetValue(null) == null;
                var expansions = new HashSet<ECardExpansionType>();
                int before = list.Count;
                if (!HasCards(list, e => e < GameReader.CustomExpansionBase && e >= 0 && e < 8)) AddBaseGameCards(list, expansions);
                int baseCards = list.Count - before;
                if (!HasCards(list, e => e >= GameReader.CustomExpansionBase)) AddCustomCards(list, custom, expansions);
                Plugin.Log.LogInfo($"Point And Slots catalog: {before} cards of its own, {baseCards} base-game cards and " +
                    $"{list.Count - before - baseCards} TCG Studio cards added ({custom.Count} sets, {RarityNames.Count} rarities).");
                _augmented = list;

                var known = Field("_catalogExpansions").GetValue(null) as List<ECardExpansionType> ?? new List<ECardExpansionType>();
                Field("_catalogExpansions").SetValue(null, known.Union(expansions).OrderBy(e => e.ToString(), StringComparer.Ordinal).ToList());
                if (eplMissing) Field("_cache").SetValue(null, list);
                AccessTools.Method(_catalog, "InvalidateLicenseCache")?.Invoke(null, null);

                AddRarityNames(custom);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pas-catalog", "Could not add TCG Studio sets to Point And Slots: " + e.Message);
            }
        }

        /// <summary>
        /// The base-game cards, read the way Point And Slots reads them but through the game's static lookups: its own
        /// reader goes through InventoryBase.m_Instance, which can be unset (Eric's 1.0 classic build: no base-game cards found).
        /// </summary>
        private static void AddBaseGameCards(IList list, HashSet<ECardExpansionType> expansions)
        {
            var baseSets = Field("BaseGameExpansions").GetValue(null) as IEnumerable<ECardExpansionType>
                ?? Enumerable.Range(0, 8).Select(i => (ECardExpansionType)i);
            var data = Inventory() is InventoryBase inventory ? inventory.m_MonsterData_SO : null;
            foreach (var e in baseSets)
            {
                // Set 6 keeps its own lists on the monster data asset (fields newer than our build references).
                var ids = (int)e == 6
                    ? (data == null ? null : AccessTools.Field(data.GetType(), "m_ShownPOCGList")?.GetValue(data) as List<EMonsterType>)
                    : SafeShownList(e);
                var pocg = (int)e == 6 && data != null ? AccessTools.Field(data.GetType(), "m_POCGDataList")?.GetValue(data) as List<MonsterData> : null;
                if (ids == null) continue;
                foreach (var id in ids)
                {
                    var monster = (int)e == 6 ? pocg?.FirstOrDefault(m => m != null && m.MonsterType == id) : InventoryBase.GetMonsterData(id);
                    if (monster == null) continue;
                    list.Add(NewEntry(e, id, monster));
                    expansions.Add(e);
                }
            }
        }

        private static List<EMonsterType>? SafeShownList(ECardExpansionType e)
        {
            try
            {
                return InventoryBase.GetShownMonsterList(e);
            }
            catch
            {
                return null;
            }
        }

        private static InventoryBase? _inventory;

        /// <summary>The shop's InventoryBase, found without CSingleton (which would create one outside the shop).</summary>
        private static InventoryBase? Inventory()
        {
            if (_inventory != null) return _inventory;
            _inventory = InventoryBase.m_Instance != null ? InventoryBase.m_Instance : UnityEngine.Object.FindObjectOfType<InventoryBase>();
            return _inventory;
        }

        private static object NewEntry(ECardExpansionType expansion, EMonsterType id, MonsterData monster)
        {
            var entry = Activator.CreateInstance(_entry);
            AccessTools.Property(_entry, "ExpansionType").SetValue(entry, expansion);
            AccessTools.Property(_entry, "MonsterType").SetValue(entry, id);
            AccessTools.Property(_entry, "MonsterData").SetValue(entry, monster);
            return entry;
        }

        private static bool HasCards(IList list, Func<int, bool> expansion)
        {
            var prop = AccessTools.Property(_entry, "ExpansionType");
            foreach (var item in list)
                if (item != null && expansion((int)(ECardExpansionType)prop.GetValue(item))) return true;
            return false;
        }

        private static void AddCustomCards(IList list, List<(int expansion, int[] shown)> custom, HashSet<ECardExpansionType> expansions)
        {
            foreach (var (e, shown) in custom)
            {
                foreach (int id in shown)
                {
                    var data = InventoryBase.GetMonsterData((EMonsterType)id);
                    if (data == null) continue;
                    list.Add(NewEntry((ECardExpansionType)e, (EMonsterType)id, data));
                    expansions.Add((ECardExpansionType)e);
                    int rarity = (int)data.Rarity;
                    if (!RarityNames.ContainsKey(rarity)) RarityNames[rarity] = data.GetRarityName();
                }
            }
        }

        /// <summary>Adds the custom sets' rarity names to Point And Slots' settings (see <see cref="PromoRarityPlan"/>).</summary>
        private static void AddRarityNames(List<(int expansion, int[] shown)> custom)
        {
            if (!Plugin.PointAndSlotsRarities.Value) return;
            var config = AccessTools.Field(AccessTools.TypeByName(PluginType), "Config")?.GetValue(null);
            if (config == null) return;
            var promo = Setting<string>(config, "_promoRarities");
            var shop = Setting<string>(config, "_shopEligibleRarities");
            var costs = Setting<string>(config, "_shopCostByRarity");
            if (promo == null || shop == null || costs == null) return;

            // Each set's rarities, most common first (TCG Studio numbers them in that order), with their tier.
            var tiers = new List<KeyValuePair<string, int>>();
            foreach (var (_, shown) in custom)
            {
                var values = shown.Select(id => InventoryBase.GetMonsterData((EMonsterType)id))
                    .Where(d => d != null).Select(d => (int)d.Rarity).Distinct().OrderBy(v => v).ToList();
                for (int i = 0; i < values.Count; i++)
                    if (RarityNames.TryGetValue(values[i], out var name))
                        tiers.Add(new KeyValuePair<string, int>(name, PromoRarityPlan.Tier(i, values.Count)));
            }
            // A name shared by several sets takes its highest tier.
            var byName = tiers.GroupBy(t => t.Key).Select(g => new KeyValuePair<string, int>(g.Key, g.Max(t => t.Value)));

            var already = new HashSet<string>(Plugin.PointAndSlotsAdded.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);
            var plan = PromoRarityPlan.Plan(promo.Value, shop.Value, costs.Value, already, byName);
            if (!plan.Changed) return;
            promo.Value = plan.Promo;
            shop.Value = plan.Shop;
            costs.Value = plan.Costs;
            already.UnionWith(plan.Added);
            Plugin.PointAndSlotsAdded.Value = string.Join(", ", already.OrderBy(s => s, StringComparer.Ordinal));
            Plugin.Log.LogInfo("Added TCG Studio rarities to Point And Slots: " + string.Join(", ", plan.Added));
        }

        private static BepInEx.Configuration.ConfigEntry<T>? Setting<T>(object config, string field) =>
            AccessTools.Field(config.GetType(), field)?.GetValue(config) as BepInEx.Configuration.ConfigEntry<T>;

        private static object? _reportedUnlocked;

        /// <summary>Postfix on PromoCardCatalog.GetUnlockedCards: logs, once per list, how many cards it counts as unlocked.</summary>
        private static void AfterGetUnlockedCards(object __result)
        {
            if (!(__result is IList list) || ReferenceEquals(list, _reportedUnlocked) || _augmented == null) return;
            _reportedUnlocked = list;
            var expansionProp = AccessTools.Property(_entry, "ExpansionType");
            var sets = new HashSet<int>();
            foreach (var item in list)
                if (item != null) sets.Add((int)(ECardExpansionType)expansionProp.GetValue(item));
            Plugin.Log.LogInfo($"Point And Slots counts {list.Count} cards as unlocked, in sets: {string.Join(", ", sets.OrderBy(e => e))}.");
        }

        /// <summary>Postfix on PromoCardCatalog.IsExpansionUnlocked: a custom set is unlocked when one of its pack licenses is.</summary>
        private static void AfterIsExpansionUnlocked(ECardExpansionType expansion, ref bool __result)
        {
            if (__result || (int)expansion < GameReader.CustomExpansionBase) return;
            try
            {
                __result = UnlockedCustomSets().Contains((int)expansion);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("pas-license", "Could not read custom set licenses for Point And Slots: " + e.Message);
            }
        }

        /// <summary>Custom sets with an unlocked license row (pack or box), worked out once per frame.</summary>
        private static HashSet<int> UnlockedCustomSets()
        {
            if (_unlockedFrame == Time.frameCount) return _unlocked;
            var set = new HashSet<int>();
            var licenses = CPlayerData.m_IsItemLicenseUnlocked;
            var inventory = Inventory();
            var rows = inventory == null || inventory.m_StockItemData_SO == null ? null : inventory.m_StockItemData_SO.m_RestockDataList;
            // License rows line up with the restock list (TCG Studio appends its packs' rows to both).
            for (int i = 0; licenses != null && rows != null && i < licenses.Count && i < rows.Count; i++)
            {
                if (!licenses[i]) continue;
                try
                {
                    var row = rows[i];
                    if (row == null) continue;
                    int e = (int)InventoryBase.GetCardExpansionType(InventoryBase.ItemTypeToCollectionPackType(row.itemType));
                    if (e >= GameReader.CustomExpansionBase) set.Add(e);
                }
                catch
                {
                    // Not a card pack row.
                }
            }
            _unlocked = set;
            _unlockedFrame = Time.frameCount;
            return set;
        }

        /// <summary>Postfix on RarityNaming.GetPrettyName: a custom rarity's own name instead of its number.</summary>
        private static void AfterGetPrettyName(ERarity rarity, ref string __result)
        {
            if (RarityNames.TryGetValue((int)rarity, out var name) && !string.IsNullOrEmpty(name)) __result = name;
        }

        /// <summary>Postfix on the shop's PrettyExpansionName: the custom set's name instead of its number.</summary>
        private static void AfterPrettyExpansionName(ECardExpansionType expansion, ref string __result)
        {
            if ((int)expansion >= GameReader.CustomExpansionBase) __result = GameReader.ExpansionName((int)expansion);
        }

        /// <summary>Prefix on the slot machine's PrettyName(string), which gets the set's enum text ("100").</summary>
        private static bool BeforePrettyName(string raw, ref string __result)
        {
            if (!int.TryParse((raw ?? "").Trim(), out int e) || e < GameReader.CustomExpansionBase) return true;
            __result = GameReader.ExpansionName(e);
            return false;
        }

        /// <summary>Postfix on the shop's and slot machine's set-group lookup: custom sets get their own group.</summary>
        private static void AfterGroup(ECardExpansionType expansion, ref string __result)
        {
            if ((int)expansion >= GameReader.CustomExpansionBase && __result == "Others") __result = GroupName;
        }

        private static FieldInfo Field(string name) =>
            AccessTools.Field(_catalog, name) ?? throw new MissingFieldException(Catalog, name);
    }
}
