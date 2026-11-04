using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCardTracker
{
    /// <summary>
    /// "All sets" for the game's quick fill screen (bulk boxes, card storage shelves and donation bins).
    ///
    /// The game's quick fill (BulkDonationBoxQuickFillScreen.RunBundleCardBulkFunction) only looks at the one set picked
    /// on that screen. The tracker draws an "All sets" button beside the set button; while it is on, the set button reads
    /// "All sets" and a Harmony prefix runs the same fill over every set instead: base-game sets, Ghost dimension cards
    /// and TCG Studio custom sets. The price range, rarity and "keep at least" settings work as usual. Picking a set on
    /// the set button turns "All sets" off again. Boxes and shelves share one choice, donation bins have their own, like
    /// the game's own quick fill settings.
    /// </summary>
    internal static class QuickFillAllSets
    {
        private const string ScreenType = "BulkDonationBoxQuickFillScreen";
        private const string Label = "All sets";

        private static Type? _type;
        private static FieldInfo? _expansionText, _priceLimit, _priceMinimum, _rarityLimit, _minimumCard, _capacity, _maxTypes,
            _working, _removing, _donation;
        private static MonoBehaviour? _screen;
        private static bool _backPressed;
        private static GUIStyle? _style, _styleOn;
        private static CardExpansionSelectScreen? _setPicker;
        private static CardRaritySelectScreen? _rarityPicker;

        public static void Install()
        {
            try
            {
                _type = AccessTools.TypeByName(ScreenType);
                if (_type == null) return;
                _expansionText = AccessTools.Field(_type, "m_CardExpansionText");
                _priceLimit = AccessTools.Field(_type, "m_PriceLimit");
                _priceMinimum = AccessTools.Field(_type, "m_PriceMinimum");
                _rarityLimit = AccessTools.Field(_type, "m_RarityLimit");
                _minimumCard = AccessTools.Field(_type, "m_MinimumCardLimit");
                _capacity = AccessTools.Field(_type, "m_MaxCapacity");
                _maxTypes = AccessTools.Field(_type, "m_MaxTypeCount");
                _working = AccessTools.Field(_type, "m_IsWorkingOnTask");
                _removing = AccessTools.Field(_type, "m_IsRemovingAllCard");
                _donation = AccessTools.Field(_type, "m_IsDonationQuickFill");
                if (_priceLimit == null || _priceMinimum == null || _rarityLimit == null || _minimumCard == null || _capacity == null ||
                    _maxTypes == null || _working == null || _removing == null)
                {
                    Plugin.Log.LogWarning("Quick fill \"All sets\" is off: this game version's quick fill screen looks different.");
                    return;
                }
                var h = new Harmony(Plugin.Guid + ".quickfill");
                h.Patch(AccessTools.Method(_type, "OpenScreen"), postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(AfterOpen)));
                h.Patch(AccessTools.Method(_type, "CloseScreen"), postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(RefreshLabel)));
                h.Patch(AccessTools.Method(_type, "OnPressChangeCardExpannsionButton"),
                    prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(PickerOpening)));
                h.Patch(AccessTools.Method(_type, "OnCardExpansionUpdated"),
                    prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BeforeSetPicked)),
                    postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(RefreshLabel)));
                h.Patch(AccessTools.Method(_type, "RunBundleCardBulkFunction"),
                    prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BeforeFill)) { priority = Priority.High });
                var back = AccessTools.Method(typeof(CardExpansionSelectScreen), "OnPressBackButton");
                if (back != null) h.Patch(back, prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BackPressed)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not add \"All sets\" to quick fill: " + e.Message);
            }
        }

        /// <summary>Boxes and shelves share one choice; donation bins have their own.</summary>
        private static ConfigEntry<bool> Choice(object screen) =>
            _donation?.GetValue(screen) is bool d && d ? Plugin.QuickFillAllSetsDonations : Plugin.QuickFillAllSetsBoxes;

        private static bool IsOn(object screen) => Plugin.QuickFillAllSetsButton.Value && Choice(screen).Value;

        private static void SetOn(object screen, bool on) => Choice(screen).Value = on;

        private static void AfterOpen(MonoBehaviour __instance)
        {
            _screen = __instance;
            RefreshLabel(__instance);
        }

        /// <summary>The set list is shared with other screens, so a back press there must not count here.</summary>
        private static void PickerOpening() => _backPressed = false;

        private static void BackPressed() => _backPressed = true;

        /// <summary>Picking a set (not backing out of the set list) switches "All sets" off.</summary>
        private static void BeforeSetPicked(MonoBehaviour __instance)
        {
            if (!_backPressed && Plugin.QuickFillAllSetsButton.Value) SetOn(__instance, false);
            _backPressed = false;
        }

        /// <summary>Shows "All sets" on the set button while it is on (the game writes the set name back on open and close).</summary>
        private static void RefreshLabel(MonoBehaviour __instance)
        {
            try
            {
                var text = _expansionText?.GetValue(__instance);
                if (text == null) return;
                var exp = (ECardExpansionType)AccessTools.Field(_type, "m_CurrentCardExpansionType").GetValue(__instance);
                string name = IsOn(__instance) ? Label : InventoryBase.GetCardExpansionName(exp);
                Traverse.Create(text).Property("text").SetValue(name);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("quickfill-label", "Quick fill set label failed: " + e.Message);
            }
        }

        // ---- The fill ----

        private struct Slot
        {
            public int Index;
            public ECardExpansionType Expansion;
            public bool Destiny;
        }

        private static bool BeforeFill(MonoBehaviour __instance)
        {
            if (!IsOn(__instance)) return true;
            try
            {
                float priceLimit = (float)_priceLimit!.GetValue(__instance);
                float priceMinimum = (float)_priceMinimum!.GetValue(__instance);
                var rarity = (ERarity)_rarityLimit!.GetValue(__instance);
                int keep = (int)_minimumCard!.GetValue(__instance);
                int capacity = (int)_capacity!.GetValue(__instance);
                int maxTypes = (int)_maxTypes!.GetValue(__instance);

                var candidates = new List<QuickFillPlan.Candidate<Slot>>();
                int sets = 0;
                foreach (var exp in Expansions())
                {
                    int before = candidates.Count;
                    try
                    {
                        AddCandidates(exp, priceLimit, priceMinimum, rarity, keep, candidates);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogOnce("quickfill-set-" + (int)exp, "Quick fill skipped set " + (int)exp + ": " + e.Message);
                    }
                    if (candidates.Count > before) sets++;
                }

                var plan = QuickFillPlan.Plan(candidates, capacity, maxTypes, UnityEngine.Random.Range);
                var output = new List<CompactCardDataAmount>();
                int total = 0;
                foreach (var (slot, amount) in plan)
                {
                    output.Add(new CompactCardDataAmount
                    {
                        cardSaveIndex = slot.Index,
                        expansionType = slot.Expansion,
                        isDestiny = slot.Destiny,
                        gradedCardIndex = 0,
                        amount = amount,
                    });
                    total += amount;
                }
                Plugin.Log.LogInfo($"Quick fill (all sets): {total} cards, {output.Count} kinds, from {sets} sets.");

                // What the game's own fill does once its list is ready: lock the sliders and start the progress circle.
                var t = Traverse.Create(__instance);
                foreach (var name in new[] { "m_SliderPriceLimit", "m_SliderPriceMinimum", "m_SliderMinCard" })
                    if (t.Field(name).GetValue() is Selectable s) s.interactable = false;
                t.Field("m_CompactCardDataList").SetValue(output);
                _removing!.SetValue(__instance, false);
                _working!.SetValue(__instance, true);
                (t.Field("m_TaskFinishCirlceGrp").GetValue() as GameObject)?.SetActive(true);
                return false;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("quickfill-all", "Quick fill from all sets failed, filled from the picked set instead: " + e);
                return true;
            }
        }

        /// <summary>Base-game sets (whatever this game version has) then TCG Studio sets.</summary>
        private static IEnumerable<ECardExpansionType> Expansions()
        {
            foreach (ECardExpansionType e in Enum.GetValues(typeof(ECardExpansionType)))
                if ((int)e >= 0 && e.ToString() != "MAX") yield return e;
            foreach (int e in GameReader.CustomExpansions()) yield return (ECardExpansionType)e;
        }

        /// <summary>The game's own filters, per card slot of one set (Ghost also has its dimension cards).</summary>
        private static void AddCandidates(ECardExpansionType exp, float priceLimit, float priceMinimum, ERarity rarity, int keep,
            List<QuickFillPlan.Candidate<Slot>> into)
        {
            var shown = InventoryBase.GetShownMonsterList(exp);
            if (shown == null || shown.Count == 0) return;
            int slots = shown.Count * CPlayerData.GetCardAmountPerMonsterType(exp);
            int passes = exp == ECardExpansionType.Ghost ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                bool destiny = pass == 1;
                for (int i = 0; i < slots; i++)
                {
                    int amount = CPlayerData.GetCardAmountByIndex(i, exp, destiny);
                    if (amount <= keep) continue;
                    float price = CPlayerData.GetCardMarketPrice(i, exp, destiny, 0);
                    if (price >= priceLimit || price < priceMinimum) continue;
                    if (rarity != ERarity.None &&
                        InventoryBase.GetMonsterData(CPlayerData.GetMonsterTypeFromCardSaveIndex(i, exp)).Rarity != rarity) continue;
                    into.Add(new QuickFillPlan.Candidate<Slot>(new Slot { Index = i, Expansion = exp, Destiny = destiny }, amount - keep));
                }
            }
        }

        // ---- The button ----

        /// <summary>Called from OnGUI: the "All sets" button to the right of the set button while the quick fill screen is open.</summary>
        public static void Draw()
        {
            var screen = _screen;
            if (screen == null || !Plugin.QuickFillAllSetsButton.Value) return;
            try
            {
                var grp = Traverse.Create(screen).Field("m_ScreenGrp").GetValue() as GameObject;
                if (grp == null || !grp.activeInHierarchy) return;
                if (PickerOpen()) return;
                var text = _expansionText?.GetValue(screen) as Component;
                var anchor = text == null ? null : (Component?)text.GetComponentInParent<Button>() ?? text;
                var r = TradeBanner.ScreenRect(anchor);
                if (r == null) return;
                var a = r.Value;

                float h = Mathf.Clamp(a.height, 24f, 120f);
                EnsureStyles(Mathf.RoundToInt(h * 0.42f));
                float w = Mathf.Max(h * 2.8f, _style!.CalcSize(new GUIContent(Label + ": Off")).x + 16f);
                float gap = Mathf.Max(6f, h * 0.15f);
                float x = a.xMax + gap;
                if (x + w > Screen.width) x = a.xMin - gap - w;
                var rect = new Rect(x, a.y + (a.height - h) / 2f, w, h);

                bool on = IsOn(screen);
                bool busy = _working?.GetValue(screen) is bool b && b;
                GUI.enabled = !busy;
                if (GUI.Button(rect, Label + (on ? ": On" : ": Off"), on ? _styleOn : _style))
                {
                    SetOn(screen, !on);
                    RefreshLabel(screen);
                    try { SoundManager.GenericConfirm(); } catch { }
                }
                GUI.enabled = true;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("quickfill-draw", "Quick fill \"All sets\" button failed: " + e.Message);
            }
        }

        /// <summary>The set or rarity list covers the screen: hide the button meanwhile.</summary>
        private static bool PickerOpen()
        {
            if (_setPicker == null) _setPicker = UnityEngine.Object.FindObjectOfType<CardExpansionSelectScreen>(true);
            if (_rarityPicker == null) _rarityPicker = UnityEngine.Object.FindObjectOfType<CardRaritySelectScreen>(true);
            return (_setPicker != null && _setPicker.m_ScreenGrp != null && _setPicker.m_ScreenGrp.activeInHierarchy) ||
                   (_rarityPicker != null && _rarityPicker.m_ScreenGrp != null && _rarityPicker.m_ScreenGrp.activeInHierarchy);
        }

        private static void EnsureStyles(int fontSize)
        {
            if (_style != null && _style.fontSize == fontSize) return;
            _style = new GUIStyle(GUI.skin.button) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _styleOn = new GUIStyle(_style);
            var green = Solid(new Color(0.16f, 0.55f, 0.27f, 0.95f));
            var greenHover = Solid(new Color(0.2f, 0.65f, 0.32f, 0.95f));
            _styleOn.normal.background = green;
            _styleOn.hover.background = greenHover;
            _styleOn.active.background = green;
            _styleOn.normal.textColor = _styleOn.hover.textColor = _styleOn.active.textColor = Color.white;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
