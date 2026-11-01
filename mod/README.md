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
  once the opening screen closes, so it never spoils a reveal. Cards from an auto pack opener get
  the same popup when you collect them from the machine.
  Turn it off with `NewCardPopup`. `PopupSeconds` (12 by default) and `PopupScale` set how long
  it stays and how big it is.
- Custom card sets added by a card-set mod such as [TCG Studio](https://github.com/GrimGrab/TCG-Studio)
  are tracked too, and count for the new-card popup and the trade banners.
- **Trade banners:** while a customer's trade or sale offer is open, a banner above their card
  says how many copies you own, ungraded and at each grade (or that you own none), and whether it
  would be new for your collection. In a trade, a second banner above the card they want from you
  does the same. Turn them off with `TradeBanners`; `PopupScale` sets their size too.
- The popups and trade banners name each card by its rarity, then its edition (left out for
  Basic), then Foil: "Pigni · Common · Silver · Foil". TCG Studio sets use their own rarity names.
- **TCG Studio cards in trades:** on its own, TCG Studio only lets customers bring its cards to
  trade or sell when the vanilla sets are hidden. With TCG Studio installed, this mod gives each
  trading or selling customer a `CustomTradeChance` (25% by default) of bringing a card from one of
  your custom sets instead, while the vanilla sets stay on. Set it to 0 to turn this off.
  In a trade, the card the customer asks for in return also has a `CustomRequestChance` (25% by
  default) of being one of your custom-set cards, picked from cards you own at a fair price for
  what they offer (75% to 150% of it, as the game does). With none, they ask for a vanilla card.
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
  Studio set only gives the versions it has. Turn it off with `[Point And Slots] RandomVersions`. After
  every purchase or win a popup lists each card you got, its rarity, version and market value, and how many copies you now own
  (counting the new ones).
- **Point And Slots points across saves:** Point And Slots keeps a points balance per save slot, and when the
  game autosaves to another slot it swaps in that slot's old balance, losing what you earned since loading. The
  tracker keeps your current balance and saves it with the slot being saved. Turn it off with
  `[Point And Slots] KeepPointsOnSave`.
- **Point And Slots points for packs:** Point And Slots' points per opened pack depend on a game event that
  not every opening sends, so the tracker counts each pack it sees opened and adds the points it missed (never
  twice). Turn it off with `[Point And Slots] PackPoints`.
- **Point And Slots payout table:** the slot machine's payout table is a picture, so once it is fully open the
  tracker writes over each row the rarities that combination pays and its chance per spin, worked out from your
  `CombinationRewards` and `LossWeight` settings. Edit those and the table follows.
- **Point And Slots rewards with a version:** a slot reward rarity in `CombinationRewards` can name the exact
  version it pays, as `Rarity - Version`: for example `che2|800|Common - First Edition` or
  `7gold|50|Legendary - Full Art Foil`. The versions are Base, First Edition, Silver, Gold, EX and Full Art, each
  optionally followed by Foil (`Common - Foil` is a Base foil). Such a reward gives that version rather than a random
  one; if a TCG Studio set doesn't have it, you get the closest version it has. The slot win screen names the
  version won.
- **Bulk amounts in the Point And Slots shop:** hold Shift while clicking a card's - or + to change the amount
  by 10, or Ctrl for 100. Or click the amount between the buttons, type a number and press Enter to put exactly
  that many in the cart (0 takes the card out). Turn it off with `[Point And Slots] BulkAmounts`.
- **Faster TCG Marketplace sales:** with the TCG Marketplace mod installed, listings sell much faster than its
  own pace of a customer every 2 - 4 seconds buying 1 - 2 cards. By default a customer comes about every second
  (`[TCG Marketplace] CustomerSeconds`) and buys up to 10 cards (`CardsPerCustomer`), and each listing is checked
  for a buyer about every 2 seconds instead of 15 - 25 (`RecheckSeconds`). A customer buys at most one copy from
  each listing, so with few listings `RecheckSeconds` and `SaleChance` (a multiplier on each check's chance of a buyer,
  1 by default) set the pace. Prices and payments are unchanged. A customer's cards are also saved in one go instead of one by one, which was the real limit on
  bulk sales. Once a minute while cards sell, the BepInEx log notes how many sold. Turn it off with `[TCG Marketplace] FasterSales`.
- **TCG Studio pack and box art on game 1.03+:** the game's Unity 6 build draws all booster packs and boxes from
  one stacked texture (a texture array), each pack picking its layer, and TCG Studio's art never reaches it, so custom
  packs and boxes looked vanilla. The tracker gives each custom pack and box its own one-layer array of TCG Studio's art,
  on the shelf, in hand, in bags and boxes and while opening; box art is also moved from TCG Studio's old box layout to
  the 1.03 box's. **This is a temporary workaround** for TCG Studio 1.14.0 and earlier on game 1.03: when a TCG Studio
  update supports the new game version, turn it off with `[TCG Studio] FixPackSkins` (or in the F7 Settings tab) and,
  if TCG Studio's own art then shows correctly, it can be removed from the tracker.
- **TCG Studio label icons on game 1.03+:** since 1.03 the labels on shelves and delivery boxes draw their item icon
  from one prebaked icon atlas, which only knows vanilla items, so TCG Studio's packs and boxes showed no icon. The
  tracker adds each custom item's icon to a copy of that atlas (also under `FixPackSkins`, same temporary workaround).
- **Day clock with TCG Studio on game 1.03+:** TCG Studio 1.14.0's shop sign code crashes on the game's Unity 6
  build, and because it runs while the shop's lighting and clock object starts up, the game switched that object off:
  no clock, and the day never ends. The tracker catches that error (noted once in the BepInEx log), so the clock runs;
  the shop sign stays vanilla. **This is a temporary workaround** until TCG Studio supports game 1.03.
- It reads the game's own card list, names and editions, so it stays correct across game updates.
- It never changes your save or your cards. The new-card popup and the trade banners hook the game's
  code (pack opening, collecting from pack openers and the trade screen) and only read; with `NewCardPopup` or `TradeBanners` off
  that hook isn't installed. The
  one thing it changes is which set a trading customer's card, and the card they ask for, comes
  from, and only with TCG Studio installed and `CustomTradeChance` or `CustomRequestChance` above 0. With Point And Slots installed, it also adds your
  custom sets to that mod's card list and, once per new rarity name, to its rarity settings. With TCG Marketplace
  installed, it changes how fast that mod's listings sell. With TCG Studio installed, it changes which picture its
  custom packs and boxes draw, and their label icons (`FixPackSkins`).

## Install

1. Install **BepInEx 5** for the game. The easiest way is r2modman or the Thunderstore Mod Manager
   (choose "TCG Card Shop Simulator"). To do it by hand, extract BepInEx 5.4.23.5 (Windows x64) into
   the game folder, next to `Card Shop Simulator.exe`, and start the game once.
2. Download the zip from the latest `mod-v...` release on the
   [Releases page](https://github.com/eric-garris/TCG-Card-Tracker/releases).
   Put `TCGCardTracker.dll` in `<game>\BepInEx\plugins\TCGCardTracker\`. With a mod manager, import
   the release zip instead.
3. Start the game, load your shop, and press **F7**.

Most settings can be changed in game on the tracker's **Settings** tab (press F7, then Settings).
Hover a setting to see what it does; changes are saved straight away. A setting that shows `*` after you change it needs a
game restart. They are all also in `<game>\BepInEx\config\tcgcardtracker.collection.cfg`. You can change the hotkey,
window size (`UiScale`), copy counts, card pictures (`CardPictures`), the new-card popup
(`NewCardPopup`), the trade banners (`TradeBanners`), TCG Studio cards in trades (`CustomTradeChance`, `CustomRequestChance`), TCG Studio pack art (`FixPackSkins`), TCG Studio sets in Point And Slots (`CustomSets`, `AddRarities`), random reward versions (`RandomVersions`), points across saves (`KeepPointsOnSave`), points for packs (`PackPoints`), TCG Marketplace sale speed (`FasterSales`, `CustomerSeconds`, `CardsPerCustomer`, `RecheckSeconds`, `SaleChance`), which places are counted, and
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
