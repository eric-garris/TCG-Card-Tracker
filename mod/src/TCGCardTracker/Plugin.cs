using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TCGCardTracker.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCGCardTracker
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "tcgcardtracker.collection";
        public const string Name = "TCG Card Tracker";
        public const string Version = "0.2.1";

        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey = null!;
        internal static ConfigEntry<float> UiScale = null!;
        internal static ConfigEntry<bool> LiveShelves = null!;
        internal static ConfigEntry<bool> ShowCopies = null!;
        internal static ConfigEntry<bool> ShowPictures = null!;
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
            ShowPictures = Config.Bind("General", "CardPictures", true,
                "Show a picture of each card in the card list, drawn by the game itself. Hover a picture to enlarge it, click to pin it.");
            LiveShelves = Config.Bind("General", "LiveShelves", true,
                "Re-read display shelves, storage and boxes from the shop when the tracker opens or refreshes. Turn off to use the last save instead.");
            foreach (var l in Locations)
                CountIn[(int)l.loc] = Config.Bind("Counting", l.key, true, "Count cards in: " + l.hint);

            // This game destroys the BepInEx manager object and never ticks plugin Update, so input
            // and drawing run from our own object, re-spawned on every scene load, plus a copy on a
            // game object the game keeps alive. Nothing here may touch CSingleton<T>.Instance: on the
            // title screen that getter creates phantom game managers.
            Runner.Spawn();
            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} in your shop to open it.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Runner.Window.ForceClose();
            Runner.Spawn();
            Runner.AttachToGameHost();
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

    /// <summary>
    /// Drives the window. Several copies can be alive (our own object and one on a game object);
    /// whichever updates first in a frame handles input and draws, so nothing runs twice.
    /// </summary>
    internal sealed class Runner : MonoBehaviour
    {
        internal static readonly TrackerWindow Window = new TrackerWindow();
        private static Runner? _own;
        private static Runner? _driver;
        private static int _lastFrame = -1;

        public static void Spawn()
        {
            if (_own != null)
            {
                if (!_own.gameObject.activeSelf) _own.gameObject.SetActive(true);
                if (!_own.enabled) _own.enabled = true;
                return;
            }
            var go = new GameObject("TCGCardTracker");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _own = go.AddComponent<Runner>();
        }

        /// <summary>Second copy on the shop's LightManager object, found without the CSingleton getter.</summary>
        public static void AttachToGameHost()
        {
            try
            {
                var host = FindObjectOfType<LightManager>();
                if (host == null) return;
                var existing = host.gameObject.GetComponent<Runner>();
                if (existing == null) host.gameObject.AddComponent<Runner>();
                else if (!existing.enabled) existing.enabled = true;
            }
            catch (System.Exception e)
            {
                Plugin.LogOnce("host-attach", "Could not attach to a game object: " + e.Message);
            }
        }

        private void Update()
        {
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            _driver = this;
            try
            {
                if (Plugin.ToggleKey.Value.IsDown()) Window.Toggle();
                Window.Tick();
            }
            catch (System.Exception e)
            {
                Plugin.LogOnce("update", "Tracker update failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (_driver != this) return;
            Window.Draw();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_own, this)) _own = null;
            if (ReferenceEquals(_driver, this)) _driver = null;
        }
    }
}
