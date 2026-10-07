# TCG Card Tracker

A collection tracker for **TCG Card Shop Simulator**. For every set it shows how many different
cards you own **ungraded and at each grade from 1 to 10**, all in one table. Open any set to see
each edition and foil vs non-foil.

There are two ways to use it:

- **Web app** (`web/`): drop in your save file. It runs entirely in your browser, and your save is
  never uploaded. It also has the missing-card checklist, collection value and a timeline.
- **In-game mod** (`mod/`): a BepInEx plugin. Press F7 in your shop to see the same grid live.
  See [mod/README.md](mod/README.md).

## Features

- **Progress grid**: rows are Tetramon, Destiny, Ascension and Ghost. Columns are Ungraded,
  Grade 1–10, Any grade and Any form. Each cell shows `cards owned / cards in set` and the
  total number of copies. Expand a set to its editions (Basic, First Edition, Silver, Gold, EX,
  Full Art; Ghost White/Black), then to Non-foil / Foil.
- **Counts cards wherever they are**: binders, the graded binder, display shelves and cases,
  storage shelves, decks, cards at the grader, card boxes, your hand, bulk donation boxes and
  auto pack openers. You can turn each place on or off ("Counting: everywhere").
- **Missing-card checklist** for any set, edition, foil and condition (ungraded, a specific
  grade, any grade, any form), with market prices and CSV export. Click any grid cell to open
  its list.
- **Collection value** at the in-game market prices stored in your save, using the game's own
  formula for graded prices, broken down by set and by location, plus your most valuable cards.
- **Timeline**: every save you load is remembered in your browser as a small summary. Chart
  completion and value over in-game days, and compare any two saves card by card.

## Using it

1. Open the web app (see "Hosting" below).
2. Find your saves in `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator`.
   `savedGames_Release0.json` is the autosave and `1`–`3` are your manual slots. The
   `savedGames_ReleaseBackupFile<N>.json` files hold the previous save of each slot.
3. Drag the file(s) onto the page.

Saves from before the 1.0 update load too; they just have no Ascension cards. Game Pass saves
are stored differently and are not supported.

## Hosting

Pushing to `main` builds the app and publishes it to GitHub Pages
(`.github/workflows/pages.yml`). Turn it on once under **Settings → Pages → Build and
deployment → Source: GitHub Actions**. The site is then at
`https://<user>.github.io/TCG-Card-Tracker/`.

`web/dist` is a static site with relative paths, so any static host works.

## Development

```sh
cd web
npm install
npm run dev        # local dev server
npm test           # unit tests (Vitest)
npm run typecheck
npm run build      # production build in web/dist
```

The code layout:

- `web/src/core/` holds the game logic, kept separate from the UI so it can be ported to the in-game mod:
  - `catalog.ts`: sets, the 121 monsters and their rarity, editions, and save-index layout
  - `save.ts`: parses a save and counts copies per card, per location, ungraded and per grade
  - `collection.ts`: unique and copy aggregation, the drill-down tree, value roll-ups
  - `value.ts`: market price formula (ungraded and graded)
  - `snapshot.ts` / `storage.ts`: timeline snapshots and diffs, kept in IndexedDB
- `web/src/ui/`: Preact components
- `docs/SAVE_FORMAT.md`: everything we know about the save file's card data

## Notes

- The web app assumes the game's monster order is the standard card number order, as every
  community tool does. The mod reads the real order from the game. If the web app ever shows
  a card under the wrong monster, the order lives in `SHOWN_MAIN` / `SHOWN_GHOST` in
  `web/src/core/catalog.ts`.
- The mod is built by `.github/workflows/mod.yml`. Push a `mod-v<version>` tag to attach the zip
  to a GitHub release.

Fan-made and unofficial; not affiliated with OPNeon Games.
