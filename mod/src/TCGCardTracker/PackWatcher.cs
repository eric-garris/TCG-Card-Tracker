using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// "New for your collection" after opening packs. A prefix on
    /// CardOpeningSequence.GetPackContent (the pack roll) snapshots what you own; once the opening
    /// screen closes, a second read shows which cards are new, or now owned at a better grade, and a
    /// short toast lists them. The hook only reads, and any error is logged while the game carries on.
    /// Trade offers get their own banners instead (TradeBanner).
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
        private static bool _screenReadable = true;

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
            Patch(harmony, typeof(CardOpeningSequence), "GetPackContent", typeof(PackWatcher), nameof(BeforeRoll), prefix: true, "the new-card popup", "pack opening");
            OpenerWatcher.Install(harmony);
        }

        internal static void Patch(Harmony harmony, Type type, string method, Type owner, string handler, bool prefix, string feature, string what)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"Could not find the game's {type.Name}.{method}; {feature} won't cover {what}.");
                    return;
                }
                var patch = new HarmonyMethod(owner, handler);
                if (prefix) harmony.Patch(target, prefix: patch);
                else harmony.Patch(target, postfix: patch);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook {what}; {feature} won't cover it: " + e.Message);
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
            OpenerWatcher.Reset();
        }

        /// <summary>Harmony prefix. Never skips the game's code, and never lets an error reach it.</summary>
        private static void BeforeRoll(CardOpeningSequence __instance)
        {
            try
            {
                // A multi-pack rolls several times on one screen: keep the snapshot from the first.
                if (_before != null || !Plugin.NewCardPopup.Value || !GameReader.IsReady()) return;
                _before = Snapshot(out _);
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

        /// <summary>What you own now, refreshing the shelves from the shop at most once a minute.</summary>
        internal static int[] Snapshot(out Catalog catalog) => Snapshot(out catalog, null);

        /// <summary>As Snapshot, leaving out the cards in one place (e.g. still inside an auto pack opener).</summary>
        internal static int[] Snapshot(out Catalog catalog, Loc? without)
        {
            catalog = _catalog ??= GameReader.BuildCatalog();
            if (Plugin.LiveShelves.Value && Time.realtimeSinceStartup - _shelvesRefreshedAt >= ShelfRefreshEvery)
            {
                GameReader.RefreshShelfData();
                _shelvesRefreshedAt = Time.realtimeSinceStartup;
            }
            return Owned(_catalog, without);
        }

        /// <summary>Called every frame: once the opening screen has closed, compares and shows the toast.</summary>
        public static void Tick()
        {
            Toast.Tick();
            OpenerWatcher.Tick();
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
                if (!_sawScreen)
                {
                    if (now - _rolledAt < WaitForScreen) return;
                    // Rolled with no opening screen: an auto pack opener, whose cards are shown when you collect
                    // them (OpenerWatcher). Comparing here would only pick up whatever else came in meanwhile,
                    // e.g. a trade. Only if the screen can't be read at all is the comparison made anyway.
                    if (_screenReadable)
                    {
                        _before = null;
                        _sequence = null;
                        return;
                    }
                }
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

        /// <summary>Shows the toast for what was added since <paramref name="before"/> (a Snapshot), e.g. a mod's card reward.</summary>
        internal static void ShowGainsSince(int[] before, string what, Loc? without = null)
        {
            try
            {
                if (_catalog == null || !GameReader.IsReady()) return;
                var gains = CardQueries.Gains(_catalog, before, Owned(_catalog, without));
                if (gains.Count == 0) return;
                Plugin.Log.LogInfo($"{what}: {gains.Count} new for your collection.");
                if (Plugin.NewCardPopup.Value) Toast.Show(gains);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("gains:" + what, $"New-card popup could not compare the collection after a {what}: " + e.Message);
            }
        }

        private static int[] Owned(Catalog catalog, Loc? without = null)
        {
            var included = Plugin.IncludedLocations();
            if (without.HasValue) included[(int)without.Value] = false;
            return GameReader.Read(catalog).Combine(included);
        }

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
                _screenReadable = false;
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
        private bool _left;
        private GUIStyle? _box, _titleStyle, _lineStyle;

        public void Clear() => _until = -1f;

        /// <summary>Lists the gains.</summary>
        public void Show(List<Gain> gains)
        {
            _lines.Clear();
            _left = false;
            int news = 0;
            foreach (var g in gains)
                if (g.IsNew) news++;
            _title = news == gains.Count ? (news == 1 ? "New for your collection" : $"{news} new for your collection")
                : news == 0 ? "New best grade"
                : $"New for your collection: {news} new, {gains.Count - news} better grade";
            for (int i = 0; i < gains.Count && i < MaxLines; i++)
                _lines.Add(Line(gains[i]));
            if (gains.Count > MaxLines) _lines.Add($"and {gains.Count - MaxLines} more (F7 to see your collection)");
            _duration = Mathf.Clamp(Plugin.PopupSeconds.Value, 2f, 120f) + Math.Min(gains.Count, MaxLines) - 1;
            _until = Time.realtimeSinceStartup + _duration;
        }

        /// <summary>
        /// Shows a title and ready-made lines (e.g. what a Point And Slots reward gave); <paramref name="left"/> puts it
        /// at the left edge, clear of a card shown in the middle of the screen.
        /// </summary>
        public void ShowLines(string title, List<string> lines, bool left = false)
        {
            _lines.Clear();
            _left = left;
            _title = title;
            for (int i = 0; i < lines.Count && i < MaxLines; i++)
                _lines.Add(lines[i]);
            if (lines.Count > MaxLines) _lines.Add("and more (F7 to see your collection)");
            _duration = Mathf.Clamp(Plugin.PopupSeconds.Value, 2f, 120f) + Math.Min(lines.Count, MaxLines) / 2;
            _until = Time.realtimeSinceStartup + _duration;
        }

        private static string Line(Gain g)
        {
            string text = $"{g.Card.RarityLabel}  ({g.Card.Set.Name})";
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
                w = _left ? Mathf.Min(w, sw * 0.35f) : w;
                var r = _left ? new Rect(20f, (sh - h) / 2f, w, h) : new Rect((sw - w) / 2f, sh * 0.1f, w, h);

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
