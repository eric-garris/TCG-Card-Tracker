using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// "New for your collection": the only places the mod hooks the game. A prefix on
    /// CardOpeningSequence.GetPackContent (the pack roll) snapshots what you own; once the opening
    /// screen closes, a second read shows which cards are new, or now owned at a better grade, and a
    /// short toast lists them. A postfix on CustomerTradeCardScreen.SetCustomer does the same for the
    /// card a customer offers to trade or sell. The hooks only read, and any error is logged while the
    /// game carries on.
    /// </summary>
    internal static class PackWatcher
    {
        private const float WaitForScreen = 10f;
        private const float SettleAfterClose = 0.5f;
        private const float ShelfRefreshEvery = 60f;

        internal static readonly Toast Toast = new Toast();

        private static Catalog? _catalog;
        private static int[]? _before;
        private static CardOpeningSequence? _sequence;
        private static float _rolledAt;
        private static bool _sawScreen;
        private static float _closedAt = -1f;
        private static float _shelvesRefreshedAt = float.NegativeInfinity;

        public static void Install()
        {
            Harmony harmony;
            try
            {
                harmony = new Harmony(Plugin.Guid);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not start Harmony; the new-card popup is off: " + e.Message);
                return;
            }
            Patch(harmony, typeof(CardOpeningSequence), "GetPackContent", nameof(BeforeRoll), prefix: true, "pack opening");
            Patch(harmony, typeof(CustomerTradeCardScreen), "SetCustomer", nameof(AfterOffer), prefix: false, "trade offers");
        }

        private static void Patch(Harmony harmony, Type type, string method, string handler, bool prefix, string what)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"Could not find the game's {type.Name}.{method}; the new-card popup won't cover {what}.");
                    return;
                }
                var patch = new HarmonyMethod(typeof(PackWatcher), handler);
                if (prefix) harmony.Patch(target, prefix: patch);
                else harmony.Patch(target, postfix: patch);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook {what}; the new-card popup won't cover them: " + e.Message);
            }
        }

        /// <summary>Forgets the shop's catalog and any pack in progress (on scene load).</summary>
        public static void Reset()
        {
            _catalog = null;
            _before = null;
            _sequence = null;
            _shelvesRefreshedAt = float.NegativeInfinity;
            Toast.Clear();
        }

        /// <summary>Harmony prefix. Never skips the game's code, and never lets an error reach it.</summary>
        private static void BeforeRoll(CardOpeningSequence __instance)
        {
            try
            {
                // A multi-pack rolls several times on one screen: keep the snapshot from the first.
                if (_before != null || !Plugin.NewCardPopup.Value || !GameReader.IsReady()) return;
                _before = Snapshot();
                _sequence = __instance;
                _rolledAt = Time.realtimeSinceStartup;
                _sawScreen = false;
                _closedAt = -1f;
            }
            catch (Exception e)
            {
                _before = null;
                Plugin.LogOnce("pack-roll", "New-card popup could not read the collection before a pack: " + e);
            }
        }

        /// <summary>
        /// Harmony postfix on the trade screen opening for a customer. The customer's card is the left one
        /// (m_CardData_L) whether they trade or sell, read from the trade data passed in, else from the
        /// screen. Fields are read by name, so a renamed one is just skipped.
        /// </summary>
        private static void AfterOffer(CustomerTradeCardScreen __instance, object[] __args)
        {
            try
            {
                if (!Plugin.NewCardPopup.Value || !GameReader.IsReady()) return;
                object source = __instance;
                foreach (var a in __args)
                    if (a is CustomerTradeData) source = a;
                var offer = Traverse.Create(source);
                var offered = offer.Field("m_CardData_L").GetValue() as CardData;
                if (offered == null || (int)offered.monsterType <= 0) return;
                bool trading = offer.Field("m_IsTrading").GetValue() as bool? ?? true;

                var owned = Snapshot();
                var card = _catalog!.FromPhysical((int)offered.expansionType, (int)offered.monsterType, (int)offered.borderType, offered.isFoil, offered.isDestiny);
                if (card == null) return;
                var gain = CardQueries.GainFrom(owned, card, Collection.ColumnOf(offered.cardGrade));
                if (gain == null) return;
                Plugin.Log.LogInfo($"{(trading ? "Trade" : "Sale")} offer is new for your collection: {card.Label}");
                Toast.Show(new List<Gain> { gain.Value }, trading ? "Trade offer" : "Sale offer");
            }
            catch (Exception e)
            {
                Plugin.LogOnce("trade-offer", "New-card popup could not check a trade offer: " + e);
            }
        }

        /// <summary>What you own now, refreshing the shelves from the shop at most once a minute.</summary>
        private static int[] Snapshot()
        {
            _catalog ??= GameReader.BuildCatalog();
            if (Plugin.LiveShelves.Value && Time.realtimeSinceStartup - _shelvesRefreshedAt >= ShelfRefreshEvery)
            {
                GameReader.RefreshShelfData();
                _shelvesRefreshedAt = Time.realtimeSinceStartup;
            }
            return Owned(_catalog);
        }

        /// <summary>Called every frame: once the opening screen has closed, compares and shows the toast.</summary>
        public static void Tick()
        {
            Toast.Tick();
            if (_before == null || _catalog == null) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                bool open = ScreenOpen();
                if (open)
                {
                    _sawScreen = true;
                    _closedAt = -1f;
                    return;
                }
                if (!_sawScreen && now - _rolledAt < WaitForScreen) return;
                if (_closedAt < 0f) _closedAt = now;
                if (now - _closedAt < SettleAfterClose) return;

                var before = _before;
                _before = null;
                _sequence = null;
                if (!GameReader.IsReady()) return;
                var gains = CardQueries.Gains(_catalog, before, Owned(_catalog));
                if (gains.Count == 0) return;
                Plugin.Log.LogInfo($"Pack opened: {gains.Count} new for your collection.");
                if (Plugin.NewCardPopup.Value) Toast.Show(gains);
            }
            catch (Exception e)
            {
                _before = null;
                _sequence = null;
                Plugin.LogOnce("pack-compare", "New-card popup could not compare the collection after a pack: " + e);
            }
        }

        private static int[] Owned(Catalog catalog) => GameReader.Read(catalog).Combine(Plugin.IncludedLocations());

        /// <summary>Kept separate so a game version without IsActive fails here, not in Tick.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ScreenOpen()
        {
            try
            {
                return _sequence != null && _sequence.IsActive();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>A short, click-through list of what a pack added, drawn at the top of the screen.</summary>
    internal sealed class Toast
    {
        private const int MaxLines = 6;
        private const float FadeSeconds = 0.6f;

        private readonly List<string> _lines = new List<string>();
        private string _title = "";
        private float _until = -1f;
        private float _duration;
        private GUIStyle? _box, _titleStyle, _lineStyle;

        public void Clear() => _until = -1f;

        /// <summary>Lists the gains; `source` (e.g. "Trade offer") prefixes the title.</summary>
        public void Show(List<Gain> gains, string? source = null)
        {
            _lines.Clear();
            int news = 0;
            foreach (var g in gains)
                if (g.IsNew) news++;
            _title = news == gains.Count ? (news == 1 ? "New for your collection" : $"{news} new for your collection")
                : news == 0 ? "New best grade"
                : $"New for your collection: {news} new, {gains.Count - news} better grade";
            if (source != null) _title = source + ": " + (news == 0 ? "better grade than yours" : "new for your collection");
            for (int i = 0; i < gains.Count && i < MaxLines; i++)
                _lines.Add(Line(gains[i]));
            if (gains.Count > MaxLines) _lines.Add($"and {gains.Count - MaxLines} more (F7 to see your collection)");
            _duration = Mathf.Clamp(Plugin.PopupSeconds.Value, 2f, 120f) + Math.Min(gains.Count, MaxLines) - 1;
            _until = Time.realtimeSinceStartup + _duration;
        }

        private static string Line(Gain g)
        {
            string text = $"{g.Card.Label}  ({g.Card.Set.Name})";
            if (!g.IsNew) text += $"  now {new Column(g.BestCol).LongLabel}";
            else if (g.BestCol > 0) text += $"  {new Column(g.BestCol).LongLabel}";
            if (g.Copies > 1) text += $"  x{g.Copies}";
            return text;
        }

        public void Tick()
        {
            if (_until >= 0f && Time.realtimeSinceStartup > _until) _until = -1f;
        }

        public void Draw()
        {
            if (_until < 0f) return;
            var saved = GUI.matrix;
            var color = GUI.color;
            try
            {
                Ensure();
                // Sized for 1080p and scaled up with the screen, so it reads the same on 1440p and 4K.
                float scale = Mathf.Clamp(Plugin.PopupScale.Value, 0.5f, 3f) * Mathf.Max(1f, Screen.height / 1080f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                float left = _until - Time.realtimeSinceStartup;
                float shown = _duration - left;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(left, shown) / FadeSeconds));

                float w = _titleStyle!.CalcSize(new GUIContent(_title)).x;
                foreach (var l in _lines) w = Mathf.Max(w, _lineStyle!.CalcSize(new GUIContent(l)).x);
                const float pad = 22f, lineH = 30f, titleH = 40f;
                w = Mathf.Min(w + pad * 2f, sw - 40f);
                float h = pad + titleH + lineH * _lines.Count + pad;
                var r = new Rect((sw - w) / 2f, sh * 0.1f, w, h);

                GUI.Box(r, GUIContent.none, _box);
                GUI.Label(new Rect(r.x + pad, r.y + pad, w - pad * 2f, titleH), _title, _titleStyle);
                for (int i = 0; i < _lines.Count; i++)
                    GUI.Label(new Rect(r.x + pad, r.y + pad + titleH + lineH * i, w - pad * 2f, lineH), _lines[i], _lineStyle);
            }
            catch (Exception e)
            {
                _until = -1f;
                Plugin.LogOnce("toast-draw", "New-card popup failed to draw: " + e);
            }
            finally
            {
                GUI.matrix = saved;
                GUI.color = color;
            }
        }

        private void Ensure()
        {
            if (_box != null) return;
            var skin = GUI.skin;
            _box = new GUIStyle(skin.box);
            var bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.12f, 0.92f));
            bg.Apply();
            _box.normal.background = bg;
            _titleStyle = new GUIStyle(skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = false };
            _titleStyle.normal.textColor = new Color(1f, 0.84f, 0.35f);
            _lineStyle = new GUIStyle(skin.label) { fontSize = 20, wordWrap = false, clipping = TextClipping.Clip };
            _lineStyle.normal.textColor = Color.white;
        }
    }
}
