using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCardTracker
{
    /// <summary>
    /// CardGradeOverhaul's condition scores in the binder: a score badge beside each card's count, and a "Scan score"
    /// filter (any / scanned / below a threshold) in TCG Studio's enhanced binder panel, so low-scoring cards are easy to
    /// find and move to a card storage shelf to sell.
    ///
    /// CardGradeOverhaul keeps one record per copy (Copies.Live, a CopyStore; ById: id -> CopyRec with Exp, Index = the
    /// card's save index, Dim, Place 0 = in the binder, Know = what has been measured). The score the player sees is
    /// InspectMath.Score(Knowledge.Readings(id), Know): exact after a card scanner, a range after looking by hand. All of
    /// it is internal, so it is found by name; nothing of CardGradeOverhaul or TCG Studio is needed at build time.
    ///
    /// The filter runs after TCG Studio's BinderIndex.Build (the list the binder pages are filled from) and splits each
    /// card into the versions that pass, so the slot shows (and the shelf picker takes) exactly that version. Its chips
    /// live in the panel's filter list (BinderPanel.Group) and, like the panel's own, take effect on Apply.
    /// </summary>
    internal static class ScanScoreBinder
    {
        private const string StudioIndex = "TCGCustomCards.Binder.BinderIndex";
        private const string StudioEntry = "TCGCustomCards.Binder.BinderEntry";
        private const string StudioPanel = "TCGCustomCards.UI.BinderPanel";
        private const string BadgeName = "TCGCT_ScanScore";

        // CardGradeOverhaul
        private static FieldInfo? _live, _byId, _enabled;
        private static FieldInfo? _rId, _rExp, _rIndex, _rDim, _rPlace, _rKnow;
        private static MethodInfo? _readings, _score;
        private static FieldInfo? _svKnown, _svLo, _svHi;

        // TCG Studio
        private static FieldInfo? _entries, _stats, _statsMatching;
        private static FieldInfo? _eExp, _eDim, _ePer, _eFixed, _eOrder;
        private static MethodInfo? _eSaveIndex, _eCopy, _shown;
        private static MethodInfo? _group, _refresh, _rebuildFilters;
        private static FieldInfo? _panelGraded;

        // Game
        private static readonly FieldInfo? CountTextField = AccessTools.Field(typeof(Card3dUIGroup), "m_CardCountText");
        private static PropertyInfo? _textProp, _fontSizeProp;

        /// <summary>Filter picked in the open panel, and the one the binder list was last built with.</summary>
        private static ScanFilter _pending, _applied;
        private static float _pendingThreshold, _appliedThreshold;
        /// <summary>Apply is committing a change of ours: the panel's Changed() must say so, or Apply only closes.</summary>
        private static bool _committing;

        private static readonly Dictionary<long, List<CopyScore>> ByCard = new Dictionary<long, List<CopyScore>>();
        private static readonly Dictionary<long, int> Stored = new Dictionary<long, int>();
        private static float _readAt = -100f;
        private static readonly Dictionary<Card3dUIGroup, Component> Badges = new Dictionary<Card3dUIGroup, Component>();

        private static bool BadgesOn => Plugin.ScanScoreBadges.Value;

        public static void Install()
        {
            if (!Plugin.ScanScoreBadges.Value && !Plugin.ScanScoreFilter.Value) return;
            if (!GradingMod.Present || !FindCardGrade()) return;
            var harmony = new Harmony(Plugin.Guid);
            try
            {
                var single = AccessTools.Method(typeof(BinderPageGrp), "SetSingleCard");
                var set = AccessTools.Method(typeof(BinderPageGrp), "SetCard");
                if (single != null) harmony.Patch(single, postfix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterSetSingleCard)));
                if (set != null) harmony.Patch(set, postfix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterSetCard)));
                Plugin.Log.LogInfo("CardGradeOverhaul scan scores shown on binder cards.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook the binder pages; no scan score badges: " + e.Message);
            }
            if (Plugin.ScanScoreFilter.Value) InstallFilter(harmony);
        }

        // ---- CardGradeOverhaul ----

        private static bool FindCardGrade()
        {
            try
            {
                var copies = AccessTools.TypeByName("CardGradeOverhaul.Copies");
                var store = AccessTools.TypeByName("CardGradeOverhaul.CopyStore");
                var rec = AccessTools.TypeByName("CardGradeOverhaul.CopyRec");
                var knowledge = AccessTools.TypeByName("CardGradeOverhaul.Knowledge");
                var inspect = AccessTools.TypeByName("CardGradeOverhaul.InspectMath");
                var view = AccessTools.TypeByName("CardGradeOverhaul.ScoreView");
                _live = copies == null ? null : AccessTools.Field(copies, "Live");
                _enabled = copies == null ? null : AccessTools.Field(copies, "Enabled");
                _byId = store == null ? null : AccessTools.Field(store, "ById");
                if (rec != null)
                {
                    _rId = AccessTools.Field(rec, "Id");
                    _rExp = AccessTools.Field(rec, "Exp");
                    _rIndex = AccessTools.Field(rec, "Index");
                    _rDim = AccessTools.Field(rec, "Dim");
                    _rPlace = AccessTools.Field(rec, "Place");
                    _rKnow = AccessTools.Field(rec, "Know");
                }
                _readings = knowledge == null ? null : AccessTools.Method(knowledge, "Readings", new[] { typeof(int) });
                _score = inspect == null ? null : AccessTools.Method(inspect, "Score", new[] { typeof(float[]), typeof(int) });
                if (view != null)
                {
                    _svKnown = AccessTools.Field(view, "Known");
                    _svLo = AccessTools.Field(view, "Lo");
                    _svHi = AccessTools.Field(view, "Hi");
                }
                if (_live == null || _byId == null || _rId == null || _rExp == null || _rIndex == null || _rDim == null
                    || _rPlace == null || _rKnow == null || _readings == null || _score == null
                    || _svKnown == null || _svLo == null || _svHi == null)
                {
                    Plugin.Log.LogWarning("CardGradeOverhaul's copy records changed; no scan scores in the binder.");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read CardGradeOverhaul's copy records; no scan scores in the binder: " + e.Message);
                return false;
            }
        }

        private static long Key(int exp, bool dim, int index) =>
            ((long)(uint)exp << 25) | ((dim ? 1L : 0L) << 24) | (uint)(index & 0xFFFFFF);

        /// <summary>CardGradeOverhaul's "dimension" half: only Ghost's isDestiny list.</summary>
        private static bool DimOf(int exp, bool isDestiny) => exp == (int)ECardExpansionType.Ghost && isDestiny;

        /// <summary>Re-reads the scanned copies in the binder and the copies on card storage shelves (at most once a second unless forced).</summary>
        private static void Read(bool force)
        {
            if (!force && Time.unscaledTime - _readAt < 1f) return;
            _readAt = Time.unscaledTime;
            ByCard.Clear();
            Stored.Clear();
            try
            {
                if (_enabled != null && _enabled.GetValue(null) is bool on && !on) return; // card condition switched off
                var store = _live!.GetValue(null);
                if (store == null || !(_byId!.GetValue(store) is IDictionary byId)) return;
                var args = new object?[2];
                foreach (var rec in byId.Values)
                {
                    if (rec == null || Convert.ToInt32(_rPlace!.GetValue(rec)) != 0) continue;
                    int id = Convert.ToInt32(_rId!.GetValue(rec));
                    int know = Convert.ToInt32(_rKnow!.GetValue(rec));
                    if (know == 0) continue; // never looked at
                    var r = _readings!.Invoke(null, new object[] { id }) as float[];
                    if (r == null) continue;
                    args[0] = r;
                    args[1] = know;
                    var view = _score!.Invoke(null, args);
                    if (view == null || !(_svKnown!.GetValue(view) is bool known) || !known) continue;
                    var score = new CopyScore(Convert.ToDouble(_svLo!.GetValue(view)), Convert.ToDouble(_svHi!.GetValue(view)));
                    long key = Key(Convert.ToInt32(_rExp!.GetValue(rec)), (bool)_rDim!.GetValue(rec), Convert.ToInt32(_rIndex!.GetValue(rec)));
                    if (!ByCard.TryGetValue(key, out var list)) ByCard[key] = list = new List<CopyScore>();
                    list.Add(score);
                }
                ReadStorage();
            }
            catch (Exception e)
            {
                Plugin.LogOnce("scan-read", "Could not read CardGradeOverhaul's scanned copies: " + e.Message);
            }
        }

        private static void ReadStorage()
        {
            var shelves = UnityEngine.Object.FindObjectOfType<ShelfManager>();
            if (shelves != null && shelves.m_FinishLoadingObjectData && shelves.m_CardStorageShelfList != null)
            {
                foreach (var shelf in shelves.m_CardStorageShelfList)
                    if (shelf != null) AddStored(shelf.GetCompactCardDataAmountList());
                return;
            }
            if (CPlayerData.m_CardStorageShelfSaveDataList != null)
                foreach (var data in CPlayerData.m_CardStorageShelfSaveDataList)
                    if (data != null) AddStored(data.compactCardDataAmountList);
        }

        private static void AddStored(List<CompactCardDataAmount>? rows)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row == null || row.amount <= 0 || row.gradedCardIndex > 0) continue;
                int exp = (int)row.expansionType;
                long key = Key(exp, DimOf(exp, row.isDestiny), row.cardSaveIndex);
                Stored.TryGetValue(key, out int n);
                Stored[key] = n + row.amount;
            }
        }

        private static List<CopyScore> Remaining(long key)
        {
            if (!ByCard.TryGetValue(key, out var scores)) return new List<CopyScore>();
            Stored.TryGetValue(key, out int shelved);
            return ScanScores.Remaining(scores, shelved);
        }

        // ---- Badges ----

        private static void AfterSetSingleCard(BinderPageGrp __instance, int cardIndex, CardData cardData, int cardCount)
        {
            try
            {
                if (__instance == null || __instance.m_CardList == null || cardIndex < 0 || cardIndex >= __instance.m_CardList.Count) return;
                var slot = __instance.m_CardList[cardIndex];
                if (!BadgesOn || cardData == null || cardData.cardGrade > 0 || cardCount <= 0)
                {
                    Hide(slot);
                    return;
                }
                Read(false);
                int exp = (int)cardData.expansionType;
                var remaining = Remaining(Key(exp, DimOf(exp, cardData.isDestiny), CPlayerData.GetCardSaveIndex(cardData)));
                var text = ScanScores.Badge(remaining);
                if (text == null) Hide(slot);
                else Show(slot, text, ScanScores.Band(remaining[0].Lo));
            }
            catch (Exception e)
            {
                Plugin.LogOnce("scan-badge", "Scan score badge failed: " + e.Message);
            }
        }

        /// <summary>The game's own page fill (not TCG Studio's): no badges there.</summary>
        private static void AfterSetCard(BinderPageGrp __instance)
        {
            if (__instance?.m_CardList == null) return;
            foreach (var slot in __instance.m_CardList) Hide(slot);
        }

        private static void Hide(Card3dUIGroup? slot)
        {
            if (slot != null && Badges.TryGetValue(slot, out var b) && b != null && b.gameObject.activeSelf) b.gameObject.SetActive(false);
        }

        private static readonly Color[] BandColors =
        {
            new Color(0.30f, 0.80f, 0.42f), // 95+
            new Color(0.25f, 0.75f, 0.75f), // 90+
            new Color(0.95f, 0.80f, 0.25f), // 80+
            new Color(0.98f, 0.55f, 0.20f), // 70+
            new Color(0.95f, 0.30f, 0.28f), // lower
        };

        private static void Show(Card3dUIGroup slot, string text, int band)
        {
            var count = CountTextField?.GetValue(slot) as Component;
            if (count == null) return;
            var badge = Badge(slot, count);
            if (badge == null) return;
            _textProp!.SetValue(badge, text);
            if (badge is Graphic g) g.color = BandColors[Mathf.Clamp(band, 0, BandColors.Length - 1)];
            // Left of the count ("x3"), past the space TCG Studio keeps there for its favourite heart.
            float size = Convert.ToSingle(_fontSizeProp!.GetValue(count));
            float countWidth = count is ILayoutElement le ? le.preferredWidth : size;
            float badgeWidth = badge is ILayoutElement be ? be.preferredWidth : size * 3f;
            var crt = (RectTransform)count.transform;
            var brt = (RectTransform)badge.transform;
            brt.anchoredPosition = crt.anchoredPosition + new Vector2(-(countWidth / 2f + size * 1.4f + badgeWidth / 2f), 0f);
            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);
        }

        /// <summary>The slot's badge: a copy of its count text (same font and canvas), made once.</summary>
        private static Component? Badge(Card3dUIGroup slot, Component count)
        {
            if (Badges.TryGetValue(slot, out var b) && b != null) return b;
            var type = count.GetType();
            _textProp ??= type.GetProperty("text");
            _fontSizeProp ??= type.GetProperty("fontSize");
            if (_textProp == null || _fontSizeProp == null) return null;
            var go = UnityEngine.Object.Instantiate(count.gameObject, count.transform.parent, false);
            go.name = BadgeName;
            for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject); // TCG Studio's pips
            b = go.GetComponent(type);
            if (b == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            _fontSizeProp.SetValue(b, Convert.ToSingle(_fontSizeProp.GetValue(count)) * 0.85f);
            if (b is Graphic g) g.raycastTarget = false;
            var dead = new List<Card3dUIGroup>();
            foreach (var kv in Badges) if (kv.Key == null || kv.Value == null) dead.Add(kv.Key!);
            foreach (var d in dead) Badges.Remove(d);
            Badges[slot] = b;
            return b;
        }

        // ---- Filter (TCG Studio's enhanced binder) ----

        private static void InstallFilter(Harmony harmony)
        {
            try
            {
                var index = AccessTools.TypeByName(StudioIndex);
                var entry = AccessTools.TypeByName(StudioEntry);
                var panel = AccessTools.TypeByName(StudioPanel);
                if (index == null || entry == null || panel == null) return; // no TCG Studio
                var build = AccessTools.Method(index, "Build");
                _entries = AccessTools.Field(index, "Entries");
                _stats = AccessTools.Field(index, "Stats");
                _statsMatching = _stats == null ? null : AccessTools.Field(_stats.FieldType, "Matching");
                _shown = AccessTools.Method(index, "Shown", new[] { entry });
                _eExp = AccessTools.Field(entry, "Expansion");
                _eDim = AccessTools.Field(entry, "Dimension");
                _ePer = AccessTools.Field(entry, "Per");
                _eFixed = AccessTools.Field(entry, "Fixed");
                _eOrder = AccessTools.Field(entry, "Order");
                _eSaveIndex = AccessTools.Method(entry, "SaveIndex");
                _eCopy = AccessTools.Method(entry, "MemberwiseCopy");
                _group = AccessTools.Method(panel, "Group");
                _refresh = AccessTools.Method(panel, "Refresh");
                _rebuildFilters = AccessTools.Method(panel, "RebuildFilters");
                _panelGraded = AccessTools.Field(panel, "_graded");
                var changed = AccessTools.Method(panel, "Changed");
                var apply = AccessTools.Method(panel, "Apply", new[] { typeof(Action<CollectionBinderFlipAnimCtrl>) });
                var opened = AccessTools.Method(panel, "OnSortScreenOpened");
                var reset = AccessTools.Method(panel, "Reset");
                var gp = _group?.GetParameters();
                if (build == null || _entries == null || _shown == null || _eExp == null || _eDim == null || _ePer == null
                    || _eFixed == null || _eOrder == null || _eSaveIndex == null || _eCopy == null || _refresh == null
                    || _rebuildFilters == null || _panelGraded == null || changed == null || apply == null || opened == null
                    || gp == null || gp.Length != 2 || gp[1].ParameterType != typeof((string, Action, Func<bool>)[]))
                {
                    Plugin.Log.LogWarning("TCG Studio's binder changed; no scan score filter in the binder panel.");
                    return;
                }
                _pendingThreshold = _appliedThreshold = ScanScores.Clamp(Plugin.ScanScoreThreshold.Value);
                harmony.Patch(build, postfix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterBuild)));
                harmony.Patch(_rebuildFilters, postfix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterRebuildFilters)));
                harmony.Patch(changed, postfix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterChanged)));
                harmony.Patch(apply, prefix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(BeforeApply)),
                    finalizer: new HarmonyMethod(typeof(ScanScoreBinder), nameof(AfterApply)));
                harmony.Patch(opened, prefix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(BeforeOpened)));
                if (reset != null) harmony.Patch(reset, prefix: new HarmonyMethod(typeof(ScanScoreBinder), nameof(BeforeReset)));
                Plugin.Log.LogInfo("Scan score filter added to TCG Studio's binder panel.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook TCG Studio's binder panel; no scan score filter: " + e.Message);
            }
        }

        private static bool PanelGraded() => _panelGraded?.GetValue(null) is bool g && g;

        /// <summary>Panel opens: its chips start from what the binder shows.</summary>
        private static void BeforeOpened()
        {
            _pending = _applied;
            _pendingThreshold = _appliedThreshold;
        }

        /// <summary>The panel's Reset clears its filters: ours too.</summary>
        private static void BeforeReset()
        {
            if (!PanelGraded()) _pending = ScanFilter.Off;
        }

        /// <summary>Apply with only our filter changed still rebuilds the binder.</summary>
        private static void AfterChanged(ref bool __result)
        {
            if (!__result && !PanelGraded() && (_committing || PendingDiffers())) __result = true;
        }

        private static bool PendingDiffers() =>
            _pending != _applied || (_pending == ScanFilter.Below && _pendingThreshold != _appliedThreshold);

        private static void BeforeApply()
        {
            if (PanelGraded()) return;
            _committing = PendingDiffers();
            _applied = _pending;
            _appliedThreshold = _pendingThreshold;
        }

        private static void AfterApply() => _committing = false;

        /// <summary>Our chips at the end of the panel's filter list (not on the graded album, whose slabs have no copy score).</summary>
        private static void AfterRebuildFilters()
        {
            if (PanelGraded()) return;
            try
            {
                var chips = new (string, Action, Func<bool>)[]
                {
                    ("Any score", () => _pending = ScanFilter.Off, () => _pending == ScanFilter.Off),
                    ("Scanned", () => _pending = ScanFilter.Scanned, () => _pending == ScanFilter.Scanned),
                    (ScanScores.BelowLabel(_pendingThreshold), () => _pending = ScanFilter.Below, () => _pending == ScanFilter.Below),
                    ("- 5%", () => Step(-ScanScores.ThresholdStep), () => false),
                    ("+ 5%", () => Step(ScanScores.ThresholdStep), () => false),
                };
                _group!.Invoke(null, new object[] { "Scan score", chips });
                _refresh!.Invoke(null, null);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                Plugin.LogOnce("scan-chips", "Could not add the scan score filter to TCG Studio's binder panel: " + inner.Message);
            }
        }

        private static void Step(float delta)
        {
            _pendingThreshold = ScanScores.Clamp(_pendingThreshold + delta);
            _pending = ScanFilter.Below;
            Plugin.ScanScoreThreshold.Value = _pendingThreshold;
            try { _rebuildFilters!.Invoke(null, null); } // new label on the threshold chip
            catch (Exception e) { Plugin.LogOnce("scan-step", "Could not update the scan score filter: " + e.Message); }
        }

        /// <summary>After TCG Studio built the binder's list: keep only the card versions that pass, one slot each.</summary>
        private static void AfterBuild()
        {
            if (_applied == ScanFilter.Off) return;
            try
            {
                if (!(_entries!.GetValue(null) is IList entries)) return;
                Read(true);
                var kept = new List<object>();
                var one = new object[1];
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    int exp = Convert.ToInt32(_eExp!.GetValue(e));
                    bool dim = DimOf(exp, (bool)_eDim!.GetValue(e));
                    int per = Convert.ToInt32(_ePer!.GetValue(e));
                    int fixedV = Convert.ToInt32(_eFixed!.GetValue(e));
                    for (int v = fixedV >= 0 ? fixedV : 0; v < (fixedV >= 0 ? fixedV + 1 : per); v++)
                    {
                        one[0] = v;
                        int index = Convert.ToInt32(_eSaveIndex!.Invoke(e, one));
                        if (!ScanScores.Passes(Remaining(Key(exp, dim, index)), _applied, _appliedThreshold)) continue;
                        var copy = _eCopy!.Invoke(e, null);
                        _eFixed.SetValue(copy, v);
                        one[0] = copy;
                        if (Convert.ToInt32(_shown!.Invoke(null, one)) < 0) continue; // hidden by the panel's own version filters
                        _eOrder!.SetValue(copy, kept.Count);
                        kept.Add(copy);
                    }
                }
                entries.Clear();
                foreach (var k in kept) entries.Add(k);
                if (_stats != null && _statsMatching != null)
                {
                    var stats = _stats.GetValue(null);
                    _statsMatching.SetValue(stats, kept.Count);
                    _stats.SetValue(null, stats);
                }
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                Plugin.LogOnce("scan-filter", "Scan score filter failed, showing every card: " + inner.Message);
            }
        }
    }
}
