using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// The Settings tab: every option from the cfg file (except the Counting places, which are
    /// chips above the other tabs), changed in game and saved to the cfg straight away.
    /// </summary>
    internal sealed partial class TrackerWindow
    {
        /// <summary>One row: the entry, a short label, and for numbers the range and step of the - / + buttons.</summary>
        private sealed class Setting
        {
            public readonly ConfigEntryBase Entry;
            public readonly string Label;
            public readonly float Min, Max, Step;
            /// <summary>Read once at startup (its hook is installed or skipped then), so a change needs a restart.</summary>
            public readonly bool Startup;

            public Setting(ConfigEntryBase entry, string label, bool startup = false, float min = 0f, float max = 0f, float step = 0f)
            {
                Entry = entry;
                Label = label;
                Startup = startup;
                Min = min;
                Max = max;
                Step = step;
            }
        }

        private static List<(string section, Setting[] rows)>? _settings;
        /// <summary>Startup settings' values when the game started, to tell when a restart is due.</summary>
        private static readonly Dictionary<ConfigEntryBase, object> StartupValues = new Dictionary<ConfigEntryBase, object>();
        private Vector2 _settingsScroll;
        private bool _rebinding;
        private GUIStyle? _wrapped;

        /// <summary>Called from Plugin.Awake once the config is bound.</summary>
        internal static void CaptureStartupSettings()
        {
            foreach (var (_, rows) in Settings())
                foreach (var s in rows)
                    if (s.Startup) StartupValues[s.Entry] = s.Entry.BoxedValue;
        }

        private static List<(string, Setting[])> Settings()
        {
            if (_settings != null) return _settings;
            _settings = new List<(string, Setting[])>
            {
                ("General", new[]
                {
                    new Setting(Plugin.ToggleKey, "Open / close key"),
                    new Setting(Plugin.UiScale, "Window size", min: 0.75f, max: 2f, step: 0.05f),
                    new Setting(Plugin.ShowPictures, "Card pictures"),
                    new Setting(Plugin.LiveShelves, "Live shelves"),
                }),
                ("Popups", new[]
                {
                    new Setting(Plugin.NewCardPopup, "New-card popup", startup: true),
                    new Setting(Plugin.PopupSeconds, "Popup seconds", min: 2f, max: 120f, step: 1f),
                    new Setting(Plugin.PopupScale, "Popup and banner size", min: 0.5f, max: 3f, step: 0.1f),
                }),
                ("Trades", new[]
                {
                    new Setting(Plugin.TradeBanners, "Trade banners", startup: true),
                    new Setting(Plugin.CustomTradeChance, "TCG Studio trade chance", min: 0f, max: 1f, step: 0.05f),
                    new Setting(Plugin.CustomRequestChance, "TCG Studio ask chance", min: 0f, max: 1f, step: 0.05f),
                }),
                ("Point And Slots", new[]
                {
                    new Setting(Plugin.PointAndSlotsSets, "TCG Studio sets", startup: true),
                    new Setting(Plugin.PointAndSlotsRarities, "Add rarity names"),
                    new Setting(Plugin.PointAndSlotsVariants, "Random versions"),
                    new Setting(Plugin.PointAndSlotsKeepPoints, "Keep points on save", startup: true),
                    new Setting(Plugin.PointAndSlotsPackPoints, "Points for packs", startup: true),
                }),
                ("TCG Marketplace", new[]
                {
                    new Setting(Plugin.MarketFasterSales, "Faster sales", startup: true),
                    new Setting(Plugin.MarketCustomerSeconds, "Seconds between customers", min: 0.25f, max: 10f, step: 0.25f),
                    new Setting(Plugin.MarketCardsPerCustomer, "Cards per customer", min: 1f, max: 50f, step: 1f),
                    new Setting(Plugin.MarketRecheckSeconds, "Check listings every (s)", min: 1f, max: 25f, step: 1f),
                }),
            };
            return _settings;
        }

        private void DrawSettings()
        {
            if (_wrapped == null) _wrapped = new GUIStyle(Styles.Muted) { wordWrap = true };
            bool restart = false;

            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll);
            foreach (var (section, rows) in Settings())
            {
                GUILayout.Space(6);
                GUILayout.Label(section, Styles.Header);
                foreach (var s in rows)
                {
                    GUILayout.BeginHorizontal(GUILayout.Height(TextRowHeight));
                    string hint = s.Entry.Description.Description;
                    bool pending = s.Startup && StartupValues.TryGetValue(s.Entry, out var was) && !Equals(was, s.Entry.BoxedValue);
                    restart |= pending;
                    GUILayout.Label(new GUIContent(s.Label + (pending ? "  *" : ""), hint), Styles.RowBody, GUILayout.Width(220));
                    switch (s.Entry)
                    {
                        case ConfigEntry<bool> b:
                            bool v = GUILayout.Toggle(b.Value, new GUIContent(b.Value ? "On" : "Off", hint), Styles.Chip, GUILayout.Width(60));
                            if (v != b.Value) b.Value = v;
                            break;
                        case ConfigEntry<float> f:
                            DrawNumber(f, s, hint);
                            break;
                        case ConfigEntry<KeyboardShortcut> k:
                            DrawKey(k, hint);
                            break;
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            // Always the same two labels, so the Layout and Repaint passes see the same controls.
            GUILayout.Label(restart ? "* Restart the game for this change to take effect." : "Changes are saved to the cfg file straight away.", Styles.Muted);
            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? "Hover a setting to see what it does." : GUI.tooltip, _wrapped, GUILayout.Height(56));
        }

        private static void DrawNumber(ConfigEntry<float> f, Setting s, string hint)
        {
            float cur = Mathf.Clamp(f.Value, s.Min, s.Max);
            float next = cur;
            if (GUILayout.Button(new GUIContent("-", hint), Styles.Chip, GUILayout.Width(28))) next = cur - s.Step;
            string text = s.Max <= 1f && s.Min >= 0f && s.Step < 0.1f ? $"{cur:P0}" : s.Step >= 1f ? $"{cur:0}" : $"{cur:0.00}";
            GUILayout.Label(new GUIContent(text, hint), Styles.HeaderCenter, GUILayout.Width(60));
            if (GUILayout.Button(new GUIContent("+", hint), Styles.Chip, GUILayout.Width(28))) next = cur + s.Step;
            // Snap to the step so repeated clicks don't drift (0.1 + 0.2 ...).
            next = Mathf.Clamp(Mathf.Round(next / s.Step) * s.Step, s.Min, s.Max);
            if (!Mathf.Approximately(next, f.Value)) f.Value = next;
        }

        private void DrawKey(ConfigEntry<KeyboardShortcut> k, string hint)
        {
            if (!_rebinding)
            {
                GUILayout.Label(new GUIContent(k.Value.ToString(), hint), Styles.HeaderCenter, GUILayout.Width(120));
                if (GUILayout.Button(new GUIContent("Change", hint), Styles.Chip, GUILayout.Width(70))) _rebinding = true;
                return;
            }
            GUILayout.Label("Press a key (Esc cancels)", Styles.HeaderCenter, GUILayout.Width(200));
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None || IsModifier(e.keyCode)) return;
            if (e.keyCode != KeyCode.Escape)
            {
                var mods = new List<KeyCode>();
                if (e.control) mods.Add(KeyCode.LeftControl);
                if (e.shift) mods.Add(KeyCode.LeftShift);
                if (e.alt) mods.Add(KeyCode.LeftAlt);
                k.Value = new KeyboardShortcut(e.keyCode, mods.ToArray());
            }
            _rebinding = false;
            e.Use();
        }

        private static bool IsModifier(KeyCode key) =>
            key == KeyCode.LeftControl || key == KeyCode.RightControl ||
            key == KeyCode.LeftShift || key == KeyCode.RightShift ||
            key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
            key == KeyCode.LeftCommand || key == KeyCode.RightCommand;
    }
}
