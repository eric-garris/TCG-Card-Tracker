/**
 * A made-up example save, so a first-time visitor sees the tracker working before they load
 * their own file. Deterministic (seeded) so the example looks the same on every visit.
 * Everything built from it is labelled as an example in the UI.
 */

import { SETS } from './catalog';
import { parseSave, type ParsedSave } from './save';
import { makeSnapshot, type Snapshot } from './snapshot';

function rng(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

const GRADE_ODDS = [0.003, 0.003, 0.006, 0.012, 0.0592, 0.0432, 0.0864, 0.1728, 0.2304, 0.384];
const NON_FOIL_BASE = [0.2, 1, 2.5, 5, 15, 50];
const FOIL_BASE = [5, 10, 30, 100, 500, 1500];
// Chance to own a card in each edition (Basic .. Full Art), before set and foil factors.
const OWN = [0.92, 0.72, 0.48, 0.3, 0.12, 0.04];

/** `progress` 0..1 scales how far into the game the example shop is. */
export function exampleSaveJson(progress: number, day: number): Record<string, unknown> {
  const r = rng(20261007);
  const save: Record<string, unknown> = {
    m_PlayerName: 'Example Shop',
    m_CurrentDay: day,
    m_ShopLevel: Math.round(8 + 20 * progress),
    m_CoinAmountDouble: Math.round(5000 + 60000 * progress),
  };
  const setFactor: Record<string, number> = { tetramon: 1, destiny: 0.8, ascension: 0.45, ghost: 0.4 };
  for (const set of SETS) {
    if (set.hiddenUnlessOwned) continue;
    const per = set.perMonsterNoFoil * 2;
    set.lists.forEach((list) => {
      const counts: number[] = [];
      const market: unknown[] = [];
      for (let i = 0; i < set.shown.length * per; i++) {
        const v = i % per;
        const foil = v >= set.perMonsterNoFoil;
        const edition = set.perMonsterNoFoil === 1 ? 5 : v % set.perMonsterNoFoil;
        // Always draw both numbers so later saves own a superset of earlier ones.
        const roll = r();
        const amount = r();
        const own = OWN[edition]! * (foil ? 0.45 : 1) * (setFactor[set.key] ?? 0.5) * progress;
        counts.push(roll < own ? 1 + Math.floor(amount * (edition === 0 ? 8 : 3)) : 0);
        const base = (foil ? FOIL_BASE : NON_FOIL_BASE)[edition]! * (0.6 + r()) * (set.key === 'ghost' ? 3 : set.key === 'tetramon' ? 1 : 1.4);
        market.push({
          generatedMarketPrice: Math.round(base * 100) / 100,
          pricePercentChangeList: Math.round((r() * 30 - 15) * 10) / 10,
          pastPricePercentChangeList: [],
        });
      }
      save['m_CardCollectedList' + list.suffix] = counts;
      save['m_GenCardMarketPriceList' + list.marketSuffix] = market;
    });
  }
  const multipliers: number[] = [];
  const ranges = [[0.01, 0.15], [0.15, 0.25], [0.25, 0.35], [0.35, 0.45], [0.45, 0.65], [0.65, 1], [1, 1.25], [1.25, 1.5], [1.5, 3], [3, 7]] as const;
  for (let k = 0; k < 2000; k++) for (const [a, b] of ranges) multipliers.push(Math.round((a + r() * (b - a)) * 1000) / 1000);
  save.m_GenGradedCardPriceMultiplierList = multipliers;

  const grade = () => {
    const x = r();
    let acc = 0;
    for (let g = 0; g < 10; g++) {
      acc += GRADE_ODDS[g]!;
      if (x < acc) return g + 1;
    }
    return 10;
  };
  const graded: unknown[] = [];
  const nGraded = Math.round(220 * progress * progress);
  for (let k = 0; k < nGraded; k++) {
    const pick = r();
    const exp = pick < 0.55 ? 0 : pick < 0.8 ? 1 : pick < 0.93 ? 7 : 2;
    const idx = Math.floor(r() * (exp === 2 ? 40 : 1452));
    graded.push({ expansionType: exp, cardSaveIndex: idx, amount: grade(), gradedCardIndex: k + 1, isDestiny: exp === 2 && r() < 0.5 });
  }
  save.m_GradedCardInventoryList = graded;

  const card = (monsterType: number, expansionType: number, borderType: number, isFoil: boolean, cardGrade: number) => ({
    monsterType,
    expansionType,
    borderType,
    isFoil,
    isDestiny: expansionType === 1,
    cardGrade,
  });
  save.m_CardShelfSaveDataList = [25, 26, 40, 42].map((objectType) => ({
    objectType,
    cardDataList: Array.from({ length: 6 }, () =>
      r() < 0.75 * progress ? card(1 + Math.floor(r() * 121), r() < 0.7 ? 0 : 1, Math.floor(r() * 6), r() < 0.25, r() < 0.5 ? grade() : 0) : card(0, 0, 0, false, 0),
    ),
  }));
  save.m_DeckCompactCardDataList = [
    { deckName: 'Fire Deck', compactCardDataAmountList: Array.from({ length: 8 }, (_, k) => ({ expansionType: 0, cardSaveIndex: k * 48, amount: 2, gradedCardIndex: 0, isDestiny: false })) },
  ];
  return save;
}

export function exampleSave(): ParsedSave {
  return parseSave(exampleSaveJson(1, 42));
}

/** A short history for the Timeline tab, kept in memory only. */
export function exampleSnapshots(): Snapshot[] {
  return [
    [0.45, 12],
    [0.7, 24],
    [0.88, 34],
    [1, 42],
  ].map(([progress, day], i) =>
    makeSnapshot(parseSave(exampleSaveJson(progress!, day!)), { name: `Example day ${day}`, lastModified: i }, i),
  );
}

export const EXAMPLE_FILE = 'Example data';
