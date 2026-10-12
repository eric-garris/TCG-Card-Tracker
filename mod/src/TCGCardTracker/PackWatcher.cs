using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// "New for your collection": the one place the mod hooks the game. A prefix on
    /// CardOpeningSequence.GetPackContent (the pack roll) snapshots what you own; once the opening
    /// screen closes, a second read shows which cards are new, or now owned at a better grade, and a
    /// short toast lists them. The hook only reads, and any error is logged while the game carries on.
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
            try
            {
                var harmony = new Harmony(Plugin.Guid);
                var roll = AccessTools.Method(typeof(CardOpeningSequence), "GetPackContent");
                if (roll == null)
                {
                    Plugin.Log.LogWarning("Could not find the game's pack opening code; the new-card popup is off.");
                    return;
                }
                harmony.Patch(roll, prefix: new HarmonyMethod(typeof(PackWatcher), nameof(BeforeRoll)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook pack opening; the new-card popup is off: " + e.Message);
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
                _catalog ??= GameReader.BuildCatalog();
                if (Plugin.LiveShelves.Value && Time.realtimeSinceStartup - _shelvesRefreshedAt >= ShelfRefreshEvery)
                {
                    GameReader.RefreshShelfData();
                    _shelvesRefreshedAt = Time.realtimeSinceStartup;
                }
                _before = Owned(_catalog);
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
        private const float BaseSeconds = 5f;
        private const float FadeSeconds = 0.6f;

        private readonly List<string> _lines = new List<string>();
        private string _title = "";
        private float _until = -1f;
        private float _duration;
        private GUIStyle? _box, _titleStyle, _lineStyle;

        public void Clear() => _until = -1f;

        public void Show(List<Gain> gains)
        {
            _lines.Clear();
            int news = 0;
            foreach (var g in gains)
                if (g.IsNew) news++;
            _title = news == gains.Count ? (news == 1 ? "New for your collection" : $"{news} new for your collection")
                : news == 0 ? "New best grade"
                : $"New for your collection: {news} new, {gains.Count - news} better grade";
            for (int i = 0; i < gains.Count && i < MaxLines; i++)
                _lines.Add(Line(gains[i]));
            if (gains.Count > MaxLines) _lines.Add($"and {gains.Count - MaxLines} more (F7 to see your collection)");
            _duration = BaseSeconds + 0.5f * Math.Min(gains.Count, MaxLines);
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
                float scale = Mathf.Clamp(Plugin.UiScale.Value, 0.75f, 2f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float sw = Screen.width / scale;
                float left = _until - Time.realtimeSinceStartup;
                float shown = _duration - left;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(left, shown) / FadeSeconds));

                float w = _titleStyle!.CalcSize(new GUIContent(_title)).x;
                foreach (var l in _lines) w = Mathf.Max(w, _lineStyle!.CalcSize(new GUIContent(l)).x);
                w = Mathf.Min(w + 28f, sw - 40f);
                const float lineH = 20f, titleH = 24f;
                float h = 12f + titleH + lineH * _lines.Count + 8f;
                var r = new Rect((sw - w) / 2f, 24f, w, h);

                GUI.Box(r, GUIContent.none, _box);
                GUI.Label(new Rect(r.x + 14f, r.y + 8f, w - 28f, titleH), _title, _titleStyle);
                for (int i = 0; i < _lines.Count; i++)
                    GUI.Label(new Rect(r.x + 14f, r.y + 8f + titleH + lineH * i, w - 28f, lineH), _lines[i], _lineStyle);
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
            _titleStyle = new GUIStyle(skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            _titleStyle.normal.textColor = new Color(1f, 0.84f, 0.35f);
            _lineStyle = new GUIStyle(skin.label) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip };
            _lineStyle.normal.textColor = Color.white;
        }
    }
}
