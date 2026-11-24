using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// The Prize Vault: unopened packs won on the duel ladders, opened from the F7 window's Prizes tab with no opening
    /// screen. Packs are rolled the way the game's auto pack openers roll them (CardOpeningSequence.GetPackContentCardDataList:
    /// real odds, editions and foils, TCG Studio packs too), a few dozen a frame so the game keeps running, and the cards
    /// go into the binder in one go per card version once every pack is rolled. Packs leave the vault only then, so a
    /// save made halfway keeps both the packs and the cards as they were.
    /// </summary>
    internal static class PrizeVault
    {
        private static MethodInfo? _roll;

        private static bool _opening;
        private static readonly List<(int itemType, int left)> Plan = new List<(int, int)>();
        private static readonly Dictionary<(int exp, bool destiny, int index), (CardData card, int count)> Rolled =
            new Dictionary<(int, bool, int), (CardData, int)>();
        private static readonly Dictionary<int, int> PacksRolled = new Dictionary<int, int>();
        private static int _total, _done, _cards;
        private static int[]? _before;

        /// <summary>True while vault packs are being rolled: pack hooks (popup, Point And Slots points) leave these alone.</summary>
        internal static bool Opening => _opening;
        internal static int Done => _done;
        internal static int Total => _total;

        internal static void Install(Harmony harmony)
        {
            _roll = AccessTools.Method(typeof(CardOpeningSequence), "GetPackContentCardDataList");
            if (_roll == null)
                Plugin.Log.LogWarning("Prize Vault: could not find the game's pack roll (GetPackContentCardDataList); packs can be won but not opened.");
        }

        internal static bool CanOpen => _roll != null && !_opening && GameReader.IsReady();

        // ---------------------------------------------------------------- prizes in

        /// <summary>Puts <paramref name="packs"/> random packs from the sets you've unlocked into the vault. Returns how many.</summary>
        internal static int AddPrize(int packs)
        {
            if (packs <= 0) return 0;
            var kinds = UnlockedPackTypes();
            if (kinds.Count == 0)
            {
                Plugin.LogOnce("vault-no-packs", "Prize Vault: no card packs unlocked to give; prize skipped.");
                return 0;
            }
            var counts = new int[kinds.Count];
            for (int i = 0; i < packs; i++) counts[UnityEngine.Random.Range(0, kinds.Count)]++;
            for (int i = 0; i < kinds.Count; i++) DuelLadder.Data.AddPacks(kinds[i], counts[i]);
            return packs;
        }

        /// <summary>Card pack item types whose license is unlocked (base game and TCG Studio); all packs if none read.</summary>
        internal static List<int> UnlockedPackTypes()
        {
            var result = new List<int>();
            try
            {
                var inventory = InventoryBase.m_Instance != null ? InventoryBase.m_Instance : UnityEngine.Object.FindObjectOfType<InventoryBase>();
                var so = inventory == null ? null : inventory.m_StockItemData_SO;
                if (so == null) return result;
                var packList = Traverse.Create(so).Field("m_CardPackItemTypeList").GetValue() as System.Collections.IList;
                var packs = new HashSet<int>();
                if (packList != null)
                    foreach (var p in packList)
                        packs.Add(Convert.ToInt32(p));
                var licenses = CPlayerData.m_IsItemLicenseUnlocked;
                var rows = so.m_RestockDataList;
                var seen = new HashSet<int>();
                // License rows line up with the restock list (TCG Studio appends its packs' rows to both).
                for (int i = 0; licenses != null && rows != null && i < licenses.Count && i < rows.Count; i++)
                {
                    if (!licenses[i] || rows[i] == null) continue;
                    int item = (int)rows[i].itemType;
                    if (packs.Contains(item) && seen.Add(item)) result.Add(item);
                }
                if (result.Count == 0) result.AddRange(packs);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("vault-unlocked", "Prize Vault could not read your unlocked packs: " + e.Message);
            }
            return result;
        }

        internal static string PackName(int itemType)
        {
            try
            {
                var data = InventoryBase.GetItemData((EItemType)itemType);
                var name = data?.GetName();
                return string.IsNullOrEmpty(name) ? ((EItemType)itemType).ToString() : name!;
            }
            catch
            {
                return ((EItemType)itemType).ToString();
            }
        }

        // ---------------------------------------------------------------- opening

        /// <summary>Starts opening every vault pack, or only one kind.</summary>
        internal static void Open(int? itemType = null)
        {
            if (!CanOpen) return;
            Plan.Clear();
            Rolled.Clear();
            PacksRolled.Clear();
            _total = _done = _cards = 0;
            foreach (var v in DuelLadder.Data.vault)
            {
                if (itemType.HasValue && v.itemType != itemType.Value) continue;
                if (v.count <= 0) continue;
                Plan.Add((v.itemType, v.count));
                _total += v.count;
            }
            if (_total == 0) return;
            try
            {
                _before = PackWatcher.Snapshot(out _);
            }
            catch (Exception e)
            {
                _before = null;
                Plugin.LogOnce("vault-snapshot", "Prize Vault could not read your collection first (no new-card list): " + e.Message);
            }
            _opening = true;
            Plugin.Log.LogInfo($"Prize Vault: opening {_total:N0} packs.");
        }

        /// <summary>Drops an opening in progress (scene change). Nothing was added and the vault is untouched.</summary>
        internal static void Cancel()
        {
            _opening = false;
            Plan.Clear();
            Rolled.Clear();
            PacksRolled.Clear();
            _before = null;
        }

        /// <summary>Called every frame: rolls the next packs, and adds the cards once all are rolled.</summary>
        internal static void Tick()
        {
            if (!_opening) return;
            try
            {
                var sequence = CSingleton<CardOpeningSequence>.Instance;
                // Never roll while you are opening a pack yourself: the roll shares the opening screen's card list.
                if (sequence == null || sequence.IsActive()) return;
                int budget = Mathf.Clamp(Mathf.RoundToInt(Plugin.LadderPacksPerFrame.Value), 1, 1000);
                while (budget-- > 0 && Plan.Count > 0)
                {
                    var (item, left) = Plan[0];
                    RollOne(sequence, item);
                    _done++;
                    PacksRolled[item] = PacksRolled.TryGetValue(item, out var n) ? n + 1 : 1;
                    if (left <= 1) Plan.RemoveAt(0);
                    else Plan[0] = (item, left - 1);
                }
                if (Plan.Count == 0) Commit();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Prize Vault: opening stopped, nothing was added and your packs are still in the vault: " + e);
                Cancel();
            }
        }

        private static void RollOne(CardOpeningSequence sequence, int itemType)
        {
            var packType = InventoryBase.ItemTypeToCollectionPackType((EItemType)itemType);
            var args = _roll!.GetParameters().Length >= 2 ? new object[] { packType, false } : new object[] { packType };
            if (!(_roll.Invoke(sequence, args) is List<CardData> cards)) return;
            foreach (var card in cards)
            {
                if (card == null) continue;
                var key = ((int)card.expansionType, card.isDestiny, CPlayerData.GetCardSaveIndex(card));
                Rolled[key] = Rolled.TryGetValue(key, out var have) ? (have.card, have.count + 1) : (PointAndSlots.Copy(card), 1);
                _cards++;
            }
        }

        private static void Commit()
        {
            _opening = false;
            int versions = 0;
            foreach (var r in Rolled.Values)
            {
                CPlayerData.AddCard(r.card, r.count);
                versions++;
            }
            foreach (var p in PacksRolled) DuelLadder.Data.TakePacks(p.Key, p.Value);
            DuelLadder.Data.packsOpened += _done;
            DuelLadder.Data.cardsOpened += _cards;
            Plugin.Log.LogInfo($"Prize Vault: opened {_done:N0} packs, {_cards:N0} cards ({versions:N0} different versions) added to your binder.");
            ShowSummary();
            Rolled.Clear();
            PacksRolled.Clear();
            _before = null;
        }

        private static void ShowSummary()
        {
            string title = $"Prize Vault: {_done:N0} packs, {_cards:N0} cards";
            var lines = new List<string>();
            try
            {
                if (_before != null && PackWatcher.Catalog != null && GameReader.IsReady())
                {
                    var gains = CardQueries.Gains(PackWatcher.Catalog, _before, PackWatcher.OwnedNow());
                    int news = 0;
                    foreach (var g in gains)
                        if (g.IsNew) news++;
                    lines.Add(news == 0 ? "Nothing new for your collection" : $"{news:N0} new for your collection");
                    for (int i = 0; i < gains.Count && lines.Count < 6; i++) lines.Add(Toast.Line(gains[i]));
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("vault-gains", "Prize Vault could not list what was new: " + e.Message);
            }
            PackWatcher.Toast.ShowLines(title, lines);
        }
    }
}
