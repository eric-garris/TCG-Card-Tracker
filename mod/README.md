# TCG Card Tracker (in-game mod)

A BepInEx mod for **TCG Card Shop Simulator** that shows your collection progress inside the game.
Press **F7** in your shop to open it.

- For every set (Tetramon, Destiny, Ascension, Ghost) it shows how many different cards you own
  **ungraded and at each grade 1–10**, plus "any grade" and "any form", with total copies.
- Expand a set into editions (Basic … Full Art; Ghost White/Black), then into Non-foil / Foil.
- Click any cell to list the missing (or owned) cards for it, with today's market price. Hover a
  cell to see where those copies are (binder, displays, storage and so on).
- The card list shows where each card's copies sit, has a search box (name, edition, "foil" or a
  card number) and sorts by number, name, price or rarity.
- **Spares** lists the copies beyond the best one you own in the chosen condition, with what they
  are worth, so you know what is safe to sell or put on display.
- Each card in the list has a picture drawn by the game itself, the same way the binder draws it,
  with its edition border, rarity and foil. Hover a picture to enlarge it, or click it to pin it in
  its own window (drag it anywhere, click the picture again or `x` to close).
- It counts cards wherever they are: binders, displays and shelves, storage, decks, at grading,
  card boxes, in hand, donation boxes and pack openers. Each place can be switched off.
- **New for your collection:** after you open packs, a short popup lists the cards that are new to
  you (no copy anywhere you count) or that you now own at a better grade than before. It appears
  once the opening screen closes, so it never spoils a reveal.
  Turn it off with `NewCardPopup`. `PopupSeconds` (12 by default) and `PopupScale` set how long
  it stays and how big it is.
- Custom card sets added by a card-set mod such as [TCG Studio](https://github.com/GrimGrab/TCG-Studio)
  are tracked too, and count for the new-card popup and the trade banners.
- **Trade banners:** while a customer's trade or sale offer is open, a banner above their card
  says how many copies you own, ungraded and at each grade (or that you own none), and whether it
  would be new for your collection. In a trade, a second banner above the card they want from you
  does the same. Turn them off with `TradeBanners`; `PopupScale` sets their size too.
- **TCG Studio cards in trades:** on its own, TCG Studio only lets customers bring its cards to
  trade or sell when the vanilla sets are hidden. With TCG Studio installed, this mod gives each
  trading or selling customer a `CustomTradeChance` (25% by default) of bringing a card from one of
  your custom sets instead, while the vanilla sets stay on. Set it to 0 to turn this off.
- **TCG Studio sets in Point And Slots:** with TCG Studio and the
  [Point And Slots](https://www.nexusmods.com/tcgcardshopsimulator/mods/1649) mod installed, your
  custom sets show up in its point shop and slot machine, under a "TCG Studio" group with their real
  set and rarity names. A set counts as unlocked once you own one of its pack licenses. New sets are
  picked up automatically. Rarity names Point And Slots doesn't list yet are added once to its
  `PromoRarities` and `ShopEligibleRarities` settings, with a `ShopCostByRarity` price by how rare
  they are in their set; delete one there and it stays deleted. If Enhanced Prefab Loader isn't
  loaded, Point And Slots normally shows no sets at all; this also gives it the base-game sets.
  Turn it off with `[Point And Slots] CustomSets`, or just the rarity names with `AddRarities`.
  With the Art Expander mod installed, the shop and slot
  machine show its art for base-game cards too.
- **Point And Slots rewards:** a card bought in its point shop or won on its slot machine comes as a random
  version (border and foil) with the same odds as a pack, though the shop still shows the base card; a TCG
  Studio set only gives the versions it has. Turn it off with `[Point And Slots] RandomVersions`. The
  "new for your collection" popup also shows after these rewards.
- It reads the game's own card list, names and editions, so it stays correct across game updates.
- It never changes your save or your cards. The new-card popup and the trade banners hook the game's
  code (pack opening and the trade screen) and only read; with `NewCardPopup` or `TradeBanners` off
  that hook isn't installed. The
  one thing it changes is which set a trading customer's card comes from, and only with TCG Studio
  installed and `CustomTradeChance` above 0. With Point And Slots installed, it also adds your
  custom sets to that mod's card list and, once per new rarity name, to its rarity settings.

## Install

1. Install **BepInEx 5** for the game. The easiest way is r2modman or the Thunderstore Mod Manager
   (choose "TCG Card Shop Simulator"). To do it by hand, extract BepInEx 5.4.23.5 (Windows x64) into
   the game folder, next to `Card Shop Simulator.exe`, and start the game once.
2. Put `TCGCardTracker.dll` in `<game>\BepInEx\plugins\TCGCardTracker\`. With a mod manager, import
   the release zip instead.
3. Start the game, load your shop, and press **F7**.

Settings are in `<game>\BepInEx\config\tcgcardtracker.collection.cfg`. You can change the hotkey,
window size (`UiScale`), copy counts, card pictures (`CardPictures`), the new-card popup
(`NewCardPopup`), the trade banners (`TradeBanners`), TCG Studio cards in trades (`CustomTradeChance`), TCG Studio sets in Point And Slots (`CustomSets`, `AddRarities`), random reward versions (`RandomVersions`), which places are counted, and
`LiveShelves` there.
`LiveShelves` re-reads displays and storage from the shop each time the panel opens or refreshes.
When it is off, the mod uses the shelf contents from your last save.

## Build

The mod compiles against [TCGGameLibs](https://www.nuget.org/packages/TCGGameLibs), the published
reference assemblies for the game, and BepInEx 5 from `nuget.bepinex.dev`. You don't need the game
installed to build it.

```sh
cd mod
dotnet test tests/TCGCardTracker.Tests   # core counting logic
scripts/package.sh                       # -> dist/TCG_Card_Tracker-<version>.zip
```

To build against your own install instead, run `scripts/package.sh -p:GameDir="C:\...\TCG Card Shop Simulator"`.

Built for game 1.0+ and also works on 0.70. Members that only exist in 1.0 (Ascension) are read by
name, so one DLL covers both.

Fan-made and unofficial; not affiliated with OPNeon Games.
