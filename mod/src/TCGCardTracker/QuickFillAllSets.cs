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
    /// "All sets" for the game's two set-picking card screens:
    /// - quick fill of bulk boxes, card storage shelves and donation bins (BulkDonationBoxQuickFillScreen);
    /// - bundling cards into a bulk box at the workbench (WorkbenchUIScreen).
    ///
    /// Both screens' RunBundleCardBulkFunction only look at the one set picked on the screen. The tracker draws an
    /// "All sets" button just left of the set button's gear; while it is on, the set line reads "All sets" and a Harmony
    /// prefix runs the same job over every set instead: base-game sets and TCG Studio custom sets (quick fill also takes
    /// Ghost dimension cards, as the game's own quick fill does for Ghost; the workbench never bundles those). The price
    /// range, rarity and "keep at least" settings work as usual. Picking a set with the gear turns "All sets" off again.
    /// Boxes and shelves share one choice, donation bins and the workbench each have their own, like the game's settings.
    /// </summary>
    internal static class QuickFillAllSets
    {
        private const string Label = "All sets";

        /// <summary>One of the two screens, with the fields the tracker reads from it.</summary>
        private sealed class Kind
        {
            public readonly Type Type;
            public readonly string ChangeSetMethod = "OnPressChangeCardExpannsionButton";
            public FieldInfo? Text, Working, Expansion, ScreenGrp, PriceLimit, PriceMinimum, Rarity, Keep;
            public Func<object, ConfigEntry<bool>> Choice = _ => Plugin.QuickFillAllSetsBoxes;
            public Func<object, bool> Hidden = _ => false;
            /// <summary>The set button's gear, found once per screen object.</summary>
            public Component? Gear;
            public object? GearFor;

            public Kind(Type type)
            {
                Type = type;
                Text = AccessTools.Field(type, "m_CardExpansionText");
                Working = AccessTools.Field(type, "m_IsWorkingOnTask");
                Expansion = AccessTools.Field(type, "m_CurrentCardExpansionType");
                ScreenGrp = AccessTools.Field(type, "m_ScreenGrp");
                PriceLimit = AccessTools.Field(type, "m_PriceLimit");
                PriceMinimum = AccessTools.Field(type, "m_PriceMinimum");
                Rarity = AccessTools.Field(type, "m_RarityLimit");
                Keep = AccessTools.Field(type, "m_MinimumCardLimit");
            }

            public bool Complete => Text != null && Working != null && Expansion != null && ScreenGrp != null && PriceLimit != null &&
                                    PriceMinimum != null && Rarity != null && Keep != null;
        }

        private static readonly Dictionary<Type, Kind> Kinds = new Dictionary<Type, Kind>();
        private static FieldInfo? _capacity, _maxTypes, _removing, _donation, _workbench, _editingDeck;
        private static MonoBehaviour? _screen;
        private static bool _backPressed;
        private static GUIStyle? _style, _styleOn;
        private static CardExpansionSelectScreen? _setPicker;
        private static CardRaritySelectScreen? _rarityPicker;

        public static void Install()
        {
            var h = new Harmony(Plugin.Guid + ".quickfill");
            InstallQuickFill(h);
            InstallWorkbench(h);
            try
            {
                var back = AccessTools.Method(typeof(CardExpansionSelectScreen), "OnPressBackButton");
                if (back != null && Kinds.Count > 0) h.Patch(back, prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BackPressed)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not watch the set list's back button: " + e.Message);
            }
        }

        private static void InstallQuickFill(Harmony h)
        {
            try
            {
                var type = AccessTools.TypeByName("BulkDonationBoxQuickFillScreen");
                if (type == null) return;
                var kind = new Kind(type);
                _capacity = AccessTools.Field(type, "m_MaxCapacity");
                _maxTypes = AccessTools.Field(type, "m_MaxTypeCount");
                _removing = AccessTools.Field(type, "m_IsRemovingAllCard");
                _donation = AccessTools.Field(type, "m_IsDonationQuickFill");
                if (!kind.Complete || _capacity == null || _maxTypes == null || _removing == null)
                {
                    Plugin.Log.LogWarning("Quick fill \"All sets\" is off: this game version's quick fill screen looks different.");
                    return;
                }
                kind.Choice = s => _donation?.GetValue(s) is bool d && d ? Plugin.QuickFillAllSetsDonations : Plugin.QuickFillAllSetsBoxes;
                Kinds[type] = kind;
                h.Patch(AccessTools.Method(type, "OpenScreen"), postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(AfterOpen)));
                PatchCommon(h, kind);
                h.Patch(AccessTools.Method(type, "RunBundleCardBulkFunction"),
                    prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BeforeQuickFill)) { priority = Priority.High });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not add \"All sets\" to quick fill: " + e.Message);
            }
        }

        private static void InstallWorkbench(Harmony h)
        {
            try
            {
                var type = typeof(WorkbenchUIScreen);
                var kind = new Kind(type);
                _workbench = AccessTools.Field(type, "m_CurrentInteractableWorkbench");
                _editingDeck = AccessTools.Field(type, "m_IsEditingDeck");
                if (!kind.Complete || _workbench == null)
                {
                    Plugin.Log.LogWarning("Workbench \"All sets\" is off: this game version's workbench screen looks different.");
                    return;
                }
                kind.Choice = _ => Plugin.QuickFillAllSetsWorkbench;
                kind.Hidden = s => _editingDeck?.GetValue(s) is bool b && b;
                Kinds[type] = kind;
                h.Patch(AccessTools.Method(type, "OpenScreen"), postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(AfterWorkbenchOpen)));
                PatchCommon(h, kind);
                h.Patch(AccessTools.Method(type, "RunBundleCardBulkFunction"),
                    prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BeforeBundle)) { priority = Priority.High });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not add \"All sets\" to the workbench: " + e.Message);
            }
        }

        private static void PatchCommon(Harmony h, Kind kind)
        {
            h.Patch(AccessTools.Method(kind.Type, "CloseScreen"), postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(RefreshLabel)));
            h.Patch(AccessTools.Method(kind.Type, kind.ChangeSetMethod), prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(PickerOpening)));
            h.Patch(AccessTools.Method(kind.Type, "OnCardExpansionUpdated"),
                prefix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(BeforeSetPicked)),
                postfix: new HarmonyMethod(typeof(QuickFillAllSets), nameof(RefreshLabel)));
        }

        private static Kind? KindOf(object? screen) => screen != null && Kinds.TryGetValue(screen.GetType(), out var k) ? k : null;

        private static bool IsOn(object screen) => Plugin.QuickFillAllSetsButton.Value && KindOf(screen) is Kind k && k.Choice(screen).Value;

        private static void SetOn(object screen, bool on)
        {
            if (KindOf(screen) is Kind k) k.Choice(screen).Value = on;
        }

        private static void AfterOpen(MonoBehaviour __instance)
        {
            _screen = __instance;
            RefreshLabel(__instance);
        }

        /// <summary>The workbench's OpenScreen is static: the screen is its singleton.</summary>
        private static void AfterWorkbenchOpen()
        {
            var screen = UnityEngine.Object.FindObjectOfType<WorkbenchUIScreen>(true);
            if (screen != null) AfterOpen(screen);
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

        /// <summary>Shows "All sets" on the set line while it is on (the game writes the set name back on open and close).</summary>
        private static void RefreshLabel(MonoBehaviour __instance)
        {
            try
            {
                var kind = KindOf(__instance);
                var text = kind?.Text?.GetValue(__instance);
                if (kind == null || text == null) return;
                var exp = (ECardExpansionType)kind.Expansion!.GetValue(__instance);
                string name = IsOn(__instance) ? Label : InventoryBase.GetCardExpansionName(exp);
                Traverse.Create(text).Property("text").SetValue(name);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("quickfill-label", "\"All sets\" label failed: " + e.Message);
            }
        }

        // ---- The jobs ----

        private struct Slot
        {
            public int Index;
            public ECardExpansionType Expansion;
            public bool Destiny;
        }

        private static List<QuickFillPlan.Candidate<Slot>> Candidates(Kind kind, object screen, int keep, bool dimension, out int sets)
        {
            float priceLimit = (float)kind.PriceLimit!.GetValue(screen);
            float priceMinimum = (float)kind.PriceMinimum!.GetValue(screen);
            var rarity = (ERarity)kind.Rarity!.GetValue(screen);
            var candidates = new List<QuickFillPlan.Candidate<Slot>>();
            sets = 0;
            foreach (var exp in Expansions())
            {
                int before = candidates.Count;
                try
                {
                    AddCandidates(exp, priceLimit, priceMinimum, rarity, keep, dimension, candidates);
                }
                catch (Exception e)
                {
                    Plugin.LogOnce("quickfill-set-" + (int)exp, "\"All sets\" skipped set " + (int)exp + ": " + e.Message);
                }
                if (candidates.Count > before) sets++;
            }
            return candidates;
        }

        private static void LockSliders(object screen)
        {
            var t = Traverse.Create(screen);
            foreach (var name in new[] { "m_SliderPriceLimit", "m_SliderPriceMinimum", "m_SliderMinCard" })
                if (t.Field(name).GetValue() is Selectable s) s.interactable = false;
        }

        private static bool BeforeQuickFill(MonoBehaviour __instance)
        {
            var kind = KindOf(__instance);
            if (kind == null || !IsOn(__instance)) return true;
            try
            {
                int keep = (int)kind.Keep!.GetValue(__instance);
                int capacity = (int)_capacity!.GetValue(__instance);
                int maxTypes = (int)_maxTypes!.GetValue(__instance);
                var candidates = Candidates(kind, __instance, keep, dimension: true, out int sets);

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
                LockSliders(__instance);
                Traverse.Create(__instance).Field("m_CompactCardDataList").SetValue(output);
                _removing!.SetValue(__instance, false);
                kind.Working!.SetValue(__instance, true);
                (Traverse.Create(__instance).Field("m_TaskFinishCirlceGrp").GetValue() as GameObject)?.SetActive(true);
                return false;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("quickfill-all", "Quick fill from all sets failed, filled from the picked set instead: " + e);
                return true;
            }
        }

        /// <summary>The workbench: 100 cards into one bulk box, up to 10 of a card per turn, its price picks the box grade.</summary>
        private static bool BeforeBundle(MonoBehaviour __instance)
        {
            var kind = KindOf(__instance);
            if (kind == null || !IsOn(__instance)) return true;
            try
            {
                const int boxSize = 100;
                var candidates = Candidates(kind, __instance, CPlayerData.m_WorkbenchMinimumCardLimit, dimension: false, out int sets);
                // Shuffled, so the first sets in the list don't fill the whole box (the game goes in card order within one set).
                for (int i = candidates.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
                var plan = QuickFillPlan.Bundle(candidates, boxSize, 10, UnityEngine.Random.Range);
                if (plan.Count == 0)
                {
                    NotEnoughResourceTextPopup.ShowText(ENotEnoughResourceText.NotEnoughCardForBundle);
                    return false;
                }

                LockSliders(__instance);
                var shown = new List<CardData>();
                float price = 0f;
                var bySet = new HashSet<ECardExpansionType>();
                foreach (var (slot, amount) in plan)
                {
                    if (shown.Count < 10) shown.Add(CPlayerData.GetCardData(slot.Index, slot.Expansion, slot.Destiny));
                    price += CPlayerData.GetCardMarketPrice(slot.Index, slot.Expansion, slot.Destiny, 0) * amount;
                    CPlayerData.ReduceCardUsingIndex(slot.Index, slot.Expansion, slot.Destiny, amount);
                    bySet.Add(slot.Expansion);
                }
                Plugin.Log.LogInfo($"Workbench bundle (all sets): {boxSize} cards from {bySet.Count} sets, worth {price:0.00}.");

                var bench = _workbench!.GetValue(__instance) as InteractableWorkbench;
                var exp = (ECardExpansionType)kind.Expansion!.GetValue(__instance);
                bench?.PlayBundlingCardBoxSequence(shown, exp, price);
                kind.Working!.SetValue(__instance, true);
                (Traverse.Create(__instance).Field("m_TaskFinishCirlceGrp").GetValue() as GameObject)?.SetActive(true);
                try { Traverse.Create(AccessTools.TypeByName("PlatformManager")).Property("Instance").Method("PlayVibration", "CardBundle").GetValue(); }
                catch { }
                return false;
            }
            catch (Exception e)
            {
                Plugin.LogOnce("workbench-all", "Workbench bundle from all sets failed, bundled from the picked set instead: " + e);
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

        /// <summary>The game's own filters, per card slot of one set (Ghost also has its dimension cards, if wanted).</summary>
        private static void AddCandidates(ECardExpansionType exp, float priceLimit, float priceMinimum, ERarity rarity, int keep,
            bool dimension, List<QuickFillPlan.Candidate<Slot>> into)
        {
            var shown = InventoryBase.GetShownMonsterList(exp);
            if (shown == null || shown.Count == 0) return;
            int slots = shown.Count * CPlayerData.GetCardAmountPerMonsterType(exp);
            int passes = dimension && exp == ECardExpansionType.Ghost ? 2 : 1;
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

        /// <summary>Called from OnGUI: the "All sets" button just left of the set button's gear while a screen is open.</summary>
        public static void Draw()
        {
            var screen = _screen;
            if (screen == null || !Plugin.QuickFillAllSetsButton.Value) return;
            try
            {
                var kind = KindOf(screen);
                if (kind == null) return;
                var grp = kind.ScreenGrp!.GetValue(screen) as GameObject;
                if (grp == null || !grp.activeInHierarchy || kind.Hidden(screen) || PickerOpen()) return;
                var text = kind.Text!.GetValue(screen) as Component;
                var textRect = TradeBanner.ScreenRect(text);
                var gearRect = TradeBanner.ScreenRect(Gear(kind, screen, grp));
                if (textRect == null && gearRect == null) return;

                var a = gearRect ?? textRect!.Value;
                float h = Mathf.Clamp((textRect ?? a).height * 0.9f, 24f, 90f);
                if (gearRect != null) h = Mathf.Clamp(Mathf.Min(h, gearRect.Value.height * 0.6f), 24f, 90f);
                EnsureStyles(Mathf.RoundToInt(h * 0.45f));
                float w = Mathf.Max(h * 2.8f, _style!.CalcSize(new GUIContent(Label + ": Off")).x + 16f);
                float gap = Mathf.Max(6f, h * 0.25f);
                // Left of the gear, level with its middle; without a gear, over the right end of the set line.
                float x = gearRect != null ? a.xMin - gap - w : a.xMax - w;
                var rect = new Rect(Mathf.Max(0f, x), a.y + (a.height - h) / 2f, w, h);

                bool on = IsOn(screen);
                bool busy = kind.Working!.GetValue(screen) is bool b && b;
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
                Plugin.LogOnce("quickfill-draw", "\"All sets\" button failed: " + e.Message);
            }
        }

        /// <summary>The button whose click opens the set list (scene-wired to OnPressChangeCardExpannsionButton).</summary>
        private static Component? Gear(Kind kind, object screen, GameObject grp)
        {
            if (ReferenceEquals(kind.GearFor, screen) && kind.Gear != null) return kind.Gear;
            kind.GearFor = screen;
            kind.Gear = null;
            foreach (var b in grp.GetComponentsInChildren<Button>(true))
            {
                int n = b.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                    if (b.onClick.GetPersistentMethodName(i) == kind.ChangeSetMethod)
                    {
                        kind.Gear = b;
                        return b;
                    }
            }
            return null;
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
