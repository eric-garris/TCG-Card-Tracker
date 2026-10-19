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
    // Load after TCG Studio when it is installed, so its trade hook exists to patch.
    [BepInDependency(CustomTrades.StudioGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // And after Point And Slots, so its catalog exists to patch.
    [BepInDependency(PointAndSlots.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "tcgcardtracker.collection";
        public const string Name = "TCG Card Tracker";
        public const string Version = "0.9.0";

        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey = null!;
        internal static ConfigEntry<float> UiScale = null!;
        internal static ConfigEntry<bool> LiveShelves = null!;
        internal static ConfigEntry<bool> ShowCopies = null!;
        internal static ConfigEntry<bool> ShowPictures = null!;
        internal static ConfigEntry<bool> NewCardPopup = null!;
        internal static ConfigEntry<bool> TradeBanners = null!;
        internal static ConfigEntry<float> PopupSeconds = null!;
        internal static ConfigEntry<float> PopupScale = null!;
        internal static ConfigEntry<float> CustomTradeChance = null!;
        internal static ConfigEntry<bool> PointAndSlotsSets = null!;
        internal static ConfigEntry<bool> PointAndSlotsRarities = null!;
        internal static ConfigEntry<string> PointAndSlotsAdded = null!;
        internal static ConfigEntry<bool> PointAndSlotsVariants = null!;
        internal static ConfigEntry<bool> PointAndSlotsKeepPoints = null!;
        internal static ConfigEntry<bool> PointAndSlotsPackPoints = null!;
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
            NewCardPopup = Config.Bind("General", "NewCardPopup", true,
                "After you open packs, briefly list the cards that are new to your collection (or now owned at a better grade); after a " +
                "Point And Slots reward, list every card you got with its version and how many copies you now own. " +
                "This hooks the game's pack opening; when it is off at startup, the hook is not installed at all.");
            PopupSeconds = Config.Bind("General", "PopupSeconds", 12f,
                "How long the new-card popup stays on screen, in seconds (2 - 120). Each extra card listed adds a second.");
            PopupScale = Config.Bind("General", "PopupScale", 1f,
                "Size of the new-card popup and the trade banners (0.5 - 3). They already grow with your screen resolution.");
            TradeBanners = Config.Bind("Trades", "TradeBanners", true,
                "While a customer's trade or sale offer is open, show a banner above their card, and above the card they want " +
                "from you in a trade, saying how many copies you own, ungraded and at each grade. " +
                "This hooks the game's trade screen; when it is off at startup, the hook is not installed at all.");
            CustomTradeChance = Config.Bind("Trades", "CustomTradeChance", 0.25f,
                "With TCG Studio installed: the chance (0 - 1) that a customer who comes to trade or sell brings a card from one of " +
                "its custom sets instead of a vanilla one. 0 leaves trades as TCG Studio makes them. " +
                "This hooks TCG Studio's trade code, and only when TCG Studio is installed.");
            PointAndSlotsSets = Config.Bind("Point And Slots", "CustomSets", true,
                "With TCG Studio and the Point And Slots mod installed: put TCG Studio's sets in Point And Slots' point shop and " +
                "slot machine (a set counts as unlocked once you own one of its pack licenses), with their real set and rarity names. " +
                "This hooks Point And Slots' code; when it is off at startup, the hook is not installed at all.");
            PointAndSlotsRarities = Config.Bind("Point And Slots", "AddRarities", true,
                "Add TCG Studio rarity names that Point And Slots doesn't list yet to its PromoRarities and ShopEligibleRarities " +
                "settings, with a ShopCostByRarity price by how rare they are in their set. Each name is added once: " +
                "delete one there and it stays deleted.");
            PointAndSlotsAdded = Config.Bind("Point And Slots", "AddedRarities", "",
                "Rarity names already added to Point And Slots' settings, so they aren't added again. Clear to add them all again.");
            PointAndSlotsVariants = Config.Bind("Point And Slots", "RandomVersions", true,
                "With the Point And Slots mod installed: a card bought in its point shop or won on its slot machine comes as a " +
                "random version (border and foil) with the same odds as a pack, instead of always the base card. The shop " +
                "still shows the base card. Only the versions a TCG Studio set has are given out.");
            PointAndSlotsKeepPoints = Config.Bind("Point And Slots", "KeepPointsOnSave", true,
                "With the Point And Slots mod installed: when the game saves to another slot than the one you loaded (such as " +
                "the autosave), keep your current points balance and save it with that slot. Point And Slots on its own swaps in " +
                "that slot's old balance, losing the points earned since loading.");
            PointAndSlotsPackPoints = Config.Bind("Point And Slots", "PackPoints", true,
                "With the Point And Slots mod installed: give its PointsPerPackOpened for each pack you open. Point And Slots " +
                "waits for a game event that some pack openings never send (no points at all); packs it does count are not " +
                "counted twice.");
            foreach (var l in Locations)
                CountIn[(int)l.loc] = Config.Bind("Counting", l.key, true, "Count cards in: " + l.hint);
            TrackerWindow.CaptureStartupSettings();

            // This game destroys the BepInEx manager object and never ticks plugin Update, so input
            // and drawing run from our own object, re-spawned on every scene load, plus a copy on a
            // game object the game keeps alive. Nothing here may touch CSingleton<T>.Instance: on the
            // title screen that getter creates phantom game managers.
            Runner.Spawn();
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (NewCardPopup.Value) PackWatcher.Install();
            if (TradeBanners.Value) TradeBanner.Install();
            CustomTrades.Install();
            PointAndSlots.Install();

            Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} in your shop to open it.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Runner.Window.ForceClose();
            PackWatcher.Reset();
            TradeBanner.Reset();
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
                if (!Window.Rebinding && Plugin.ToggleKey.Value.IsDown()) Window.Toggle();
                Window.Tick();
            }
            catch (System.Exception e)
            {
                Plugin.LogOnce("update", "Tracker update failed: " + e);
            }
            try
            {
                PackWatcher.Tick();
                TradeBanner.Tick();
                PointAndSlots.Tick();
            }
            catch (System.Exception e)
            {
                Plugin.LogOnce("pack-update", "New-card popup update failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (_driver != this) return;
            Window.Draw();
            PackWatcher.Toast.Draw();
            TradeBanner.Draw();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_own, this)) _own = null;
            if (ReferenceEquals(_driver, this)) _driver = null;
        }
    }
}
