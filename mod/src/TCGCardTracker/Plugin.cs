using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "tcgcardtracker.collection";
        public const string Name = "TCG Card Tracker";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey = null!;
        internal static ConfigEntry<float> UiScale = null!;
        internal static ConfigEntry<bool> LiveShelves = null!;
        internal static ConfigEntry<bool> ShowCopies = null!;
        internal static readonly ConfigEntry<bool>[] CountIn = new ConfigEntry<bool>[Collection.LocCount];

        private static readonly HashSet<string> Logged = new HashSet<string>();

        internal static readonly (Loc loc, string key, string label, string hint)[] Locations =
        {
            (Loc.Binder, "Binders", "Binders", "Card album and graded card album"),
            (Loc.Displays, "Displays", "Displays", "Card shelves, display cases, wall displays, projectors, prize shelves"),
            (Loc.Storage, "Storage", "Storage", "Card storage shelves"),
            (Loc.Decks, "Decks", "Decks", "Cards locked in saved decks"),
            (Loc.Grading, "Grading", "At grading", "Cards sent to (or staged for) grading"),
            (Loc.Boxes, "Boxes", "Card boxes", "Card package boxes, e.g. returned graded cards"),
            (Loc.Hand, "Hand", "In hand", "Cards you are holding"),
            (Loc.Donation, "Donation", "Donation box", "Bulk donation boxes"),
            (Loc.Openers, "Openers", "Pack openers", "Auto pack openers"),
        };

        private void Awake()
        {
            Log = Logger;
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F7), "Key that opens and closes the tracker.");
            UiScale = Config.Bind("General", "UiScale", 1f, "Size of the tracker window (0.75 - 2).");
            ShowCopies = Config.Bind("General", "ShowCopies", true, "Show the total number of copies under each count.");
            LiveShelves = Config.Bind("General", "LiveShelves", true,
                "Re-read display shelves, storage and boxes from the shop when the tracker opens or refreshes. Turn off to use the last save instead.");
            foreach (var l in Locations)
                CountIn[(int)l.loc] = Config.Bind("Counting", l.key, true, "Count cards in: " + l.hint);

            // A dedicated object that survives scene loads drives input and drawing.
            var host = new GameObject("TCGCardTracker");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<Runner>();

            Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} in your shop to open it.");
        }

        internal static void LogOnce(string key, string message)
        {
            if (Logged.Add(key)) Log.LogWarning(message);
        }

        internal static bool[] IncludedLocations()
        {
            var inc = new bool[Collection.LocCount];
            for (int i = 0; i < inc.Length; i++) inc[i] = CountIn[i]?.Value ?? true;
            return inc;
        }
    }

    internal sealed class Runner : MonoBehaviour
    {
        private readonly TrackerWindow _window = new TrackerWindow();

        private void Update()
        {
            if (Plugin.ToggleKey.Value.IsDown()) _window.Toggle();
            _window.Tick();
        }

        private void OnGUI() => _window.Draw();
    }
}
