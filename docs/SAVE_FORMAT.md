# TCG Card Shop Simulator save format (cards)

Reference for everything card-related the tracker reads. It is written for game v1.02
(post 1.0, September 2026). Facts were cross-checked against the disassembled game code and
several open-source mods and trackers (see "Sources" below).

## Files

- Folder: `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator` (Steam/PC).
- `savedGames_Release0.json` is the autosave, and `savedGames_Release1.json`–`3` are the manual slots.
- `savedGames_ReleaseBackupFile<N>.json` is the previous save of slot N. The game rotates it
  on every save, so it works as a free "before" snapshot.
- `savedGames_Release<N>.gd` is a .NET BinaryFormatter copy. The game only reads it when the
  JSON is missing or broken. Browsers cannot read it.
- The JSON is Unity `JsonUtility.ToJson(CGameData)`: field names are the `m_*` fields, enums
  are integers, there are no dictionaries, and the file may start with a UTF-8 BOM.
- The Game Pass / Microsoft Store build does not write these files.

## Sets (`ECardExpansionType`)

| value | set | lists suffix | cards |
|---|---|---|---|
| 0 | Tetramon | `` | 121 monsters × 12 = 1,452 |
| 1 | Destiny | `Destiny` | 1,452 (same monsters) |
| 2 | Ghost | `Ghost` (White) / `GhostBlack` (Black) | 20 × 2 × 2 = 80 |
| 3, 4, 5 | Megabot, FantasyRPG, CatJob | own suffixes | unreleased; only content mods use them |
| 6 | FoodieGO | none | not collectible |
| 7 | Ascension (1.0+) | `Ascension` | 1,452 (same monsters) |

## Card index (`CPlayerData.GetCardSaveIndex`)

```
perNoFoil = 6 (Ghost: 1)
saveIndex = shownPos * (perNoFoil * 2) + borderType + (isFoil ? perNoFoil : 0)
```

`shownPos` is the monster's position in the set's shown-monster list, which is game asset
data. Tetramon, Destiny and Ascension share `m_ShownMonsterList` (EMonsterType 1..121 in
order). Ghost uses `m_ShownGhostMonsterList`, the 20 Ghost monsters in enum order. Ghost
cards always have `borderType` 0. `isDestiny` selects the Ghost Black half and is ignored for
every other set.

`ECardBorderType`: 0 Base, 1 FirstEdition, 2 Silver, 3 Gold, 4 EX, 5 FullArt. Ascension
reuses the slots as Basic, Silver, Gold, Silver Full Art, EX Full Art, Borderless Full Art.

## Where cards are

| field | shape | meaning |
|---|---|---|
| `m_CardCollectedList{suffix}` | `int[]` | ungraded copies in the binder, at `saveIndex` (padded to ~2000–2100) |
| `m_IsCardCollectedList{suffix}` | `bool[]` | "ever collected"; never cleared when a card leaves |
| `m_GradedCardInventoryList` | compact rows | graded binder: **one row per copy, `amount` is the grade** |
| `m_CardShelfSaveDataList[].cardDataList` | CardData | every card display/shelf; empty slots have `monsterType` 0 |
| `m_CardItemCombiShelfSaveDataList[].cardDataList` | CardData | combo shelves, incl. tournament prize shelves |
| `m_CardStorageShelfSaveDataList[].compactCardDataAmountList` | compact | storage shelves |
| `m_DeckCompactCardDataList[].compactCardDataAmountList` | compact | cards locked in decks |
| `m_GradeCardInProgressList[].m_CardDataList` | CardData | at the grading company (padded to 8 slots) |
| `m_CurrentGradeCardSubmitSet.m_CardDataList` | CardData | staged on the grading website |
| `m_PackageBoxCardSaveDataList[].cardDataList` | CardData | card boxes, e.g. returned graded cards |
| `m_HoldCardDataList` | CardData | in the player's hand |
| `m_BulkDonationSaveDataList[].compactCardDataAmountList` | compact | bulk donation boxes |
| `m_AutoPackOpenerSaveDataList[].compactCardDataAmountList` | compact | auto pack openers |

`CardData` = `{monsterType, expansionType, borderType, isFoil, isDestiny, cardGrade, ...}`, with
`cardGrade` 0 meaning ungraded.
Compact = `{expansionType, cardSaveIndex, amount, gradedCardIndex, isDestiny}`. In
containers, `amount` is a copy count, except rows with `gradedCardIndex > 0`, which are one
graded copy whose `amount` is the grade.

These locations don't overlap: moving a card out of the binder removes it from the binder
lists. `m_TournamentData.m_PrizeDataList` mirrors prize-shelf cards, so the tracker skips it.

Vanilla grades are 1–10 (Very Poor, Poor, Fair, Good, Very Good, Fine, Excellent, Near
Mint, Mint, Gem Mint). Grading mods such as Grading Overhaul store larger encoded values,
which the tracker counts as "Other grade".

## Prices

- `m_GenCardMarketPriceList{suffix}[saveIndex]` =
  `{generatedMarketPrice, pricePercentChangeList (current % change), pastPricePercentChangeList[]}`.
  For Ghost the market lists are swapped: White reads `...GhostBlack` and Black reads `...Ghost`.
- Ungraded price = `RoundToInt(base * (1 + pct/100) * 100) / 100` (float32, half-to-even).
- Graded price: `m = m_GenGradedCardPriceMultiplierList[(saveIndex*10 + grade-1) % count]`,
  then `RoundToInt(m * base * (1 + pct/100) * 100) / 100 + bonus`, with
  bonus = `m × 12 / 8 / 4 / 2` for grade 10 / 9 / 8 / 7.
- Prices are in the base currency (USD). `m_CardPriceSetList{suffix}` holds the player's own
  asking prices, which the tracker doesn't use.

## Sources

- Game `Assembly-CSharp.dll` v1.02, disassembled for research only and not redistributed.
- [Rhiale/TCGGradedCardTracker](https://github.com/Rhiale/TCGGradedCardTracker) (monster names, graded parsing)
- [FyreDay Archipelago world](https://github.com/FyreDay/Archipelago-TCGCardShopSimulator) (monster rarities)
- [FyreDay/TCG-CardShop-Sim-APClient](https://github.com/FyreDay/TCG-CardShop-Sim-APClient),
  [DeliriumPulse/CardShopCoop](https://github.com/DeliriumPulse/CardShopCoop),
  [Kitsune-Den/KitsuneShopkeeper](https://github.com/Kitsune-Den/KitsuneShopkeeper) (game APIs)
