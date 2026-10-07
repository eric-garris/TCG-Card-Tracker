import { describe, expect, it } from 'vitest';
import { CATALOG, cardFromCompact, cardFromPhysical, cardLabel } from '../src/core/catalog';
import { COLS, COL_OTHER, SaveFormatError, parseSave, parseSaveText } from '../src/core/save';
import {
  ALL_LOCATIONS,
  aggregate,
  buildTree,
  combineLocations,
  copies,
  DEFAULT_FILTERS,
  valueOf,
  visibleSets,
} from '../src/core/collection';
import { marketPrice } from '../src/core/value';
import { diffSnapshots, fromSparse, makeSnapshot, snapshotTotals, toSparse } from '../src/core/snapshot';
import { compact, EMPTY_SLOT, emptySave, marketPrice as mp, padded, physical } from './fixtures';

const all = new Set(ALL_LOCATIONS);
const card = (set: string, list: number, idx: number) => CATALOG.byIndex.get(`${set}:${list}:${idx}`)!;

describe('catalog', () => {
  it('has the documented card counts per set', () => {
    expect(CATALOG.bySet.get('tetramon')).toHaveLength(1452);
    expect(CATALOG.bySet.get('destiny')).toHaveLength(1452);
    expect(CATALOG.bySet.get('ascension')).toHaveLength(1452);
    expect(CATALOG.bySet.get('ghost')).toHaveLength(80);
  });

  it('lays out save indices as monster*12 + foil*6 + border', () => {
    expect(cardLabel(card('tetramon', 0, 0))).toBe('Pigni · Basic');
    expect(cardLabel(card('tetramon', 0, 6))).toBe('Pigni · Basic · Foil');
    expect(cardLabel(card('tetramon', 0, 12 + 5))).toBe('Burpig · Full Art');
    expect(card('tetramon', 0, 1451).name).toBe('Toximuck');
    expect(cardLabel(card('ascension', 0, 5))).toBe('Pigni · Borderless Full Art');
  });

  it('splits Ghost into White and Black halves of 40', () => {
    expect(cardLabel(card('ghost', 0, 0))).toBe('Blazoar · White');
    expect(cardLabel(card('ghost', 0, 1))).toBe('Blazoar · White · Foil');
    expect(cardLabel(card('ghost', 1, 39))).toBe('Katengu · Black · Foil');
  });

  it('maps physical card data to the same index the game uses', () => {
    // Burpig (2), Gold (3), foil: 1*12 + 3 + 6
    expect(cardFromPhysical(0, 2, 3, true, false)?.saveIndex).toBe(21);
    // Katengu (102) is the 20th Ghost monster; Black foil -> list 1, index 39
    const k = cardFromPhysical(2, 102, 0, true, true)!;
    expect([k.list, k.saveIndex, k.name]).toEqual([1, 39, 'Katengu']);
    // Ghost cards ignore the stored border
    expect(cardFromPhysical(2, 4, 5, false, false)?.saveIndex).toBe(0);
    // Not a Ghost monster
    expect(cardFromPhysical(2, 1, 0, false, false)).toBeUndefined();
    // isDestiny does not matter outside Ghost
    expect(cardFromCompact(1, 30, true)).toBe(cardFromCompact(1, 30, false));
  });
});

describe('parseSave', () => {
  it('rejects files that are not saves', () => {
    expect(() => parseSaveText('not json')).toThrow(SaveFormatError);
    expect(() => parseSaveText('{"m_PlayerName":"x"}')).toThrow(SaveFormatError);
  });

  it('reads a save with a BOM and the Archipelago wrapper', () => {
    const save = emptySave();
    (save.m_CardCollectedList as number[])[0] = 3;
    const s1 = parseSaveText('﻿' + JSON.stringify(save));
    const s2 = parseSaveText(JSON.stringify({ UnityGameData: JSON.stringify(save), ModData: {} }));
    for (const s of [s1, s2]) expect(s.counts.binder[card('tetramon', 0, 0).i * COLS]).toBe(3);
    expect(s1.meta).toMatchObject({ playerName: 'Tester', day: 12, coins: 1234.5, hasAscension: true });
  });

  it('counts album copies, graded album rows and every other location', () => {
    const save = emptySave();
    (save.m_CardCollectedList as number[])[21] = 2; // Burpig Gold foil x2
    (save.m_CardCollectedListGhostBlack as number[])[39] = 1; // Katengu Black foil
    (save.m_CardCollectedList as number[])[2000] = 99; // padding beyond the real cards is ignored
    save.m_GradedCardInventoryList = [
      compact(0, 21, 10, { gradedCardIndex: 1 }), // grade 10
      compact(0, 21, 10, { gradedCardIndex: 2 }), // a second grade 10 copy
      compact(0, 21, 7, { gradedCardIndex: 3 }),
      compact(2, 1, 9, { isDestiny: true, gradedCardIndex: 4 }), // Blazoar Black foil, grade 9
    ];
    save.m_CardShelfSaveDataList = [
      { objectType: 25, cardDataList: [physical({ monsterType: 2, borderType: 3, isFoil: true, cardGrade: 8 }), EMPTY_SLOT] },
      { objectType: 47, cardDataList: [physical({ monsterType: 1, expansionType: 7, borderType: 5 })] },
    ];
    save.m_CardItemCombiShelfSaveDataList = [{ objectType: 55, cardDataList: [physical({ monsterType: 1, cardGrade: 3 })] }];
    save.m_CardStorageShelfSaveDataList = [
      { compactCardDataAmountList: [compact(1, 0, 5), compact(1, 0, 6, { gradedCardIndex: 9 })] },
    ];
    save.m_DeckCompactCardDataList = [{ deckName: 'A', compactCardDataAmountList: [compact(0, 0, 4)] }];
    save.m_GradeCardInProgressList = [{ m_CardDataList: padded([physical({ monsterType: 1 })], EMPTY_SLOT, 8), m_ServiceLevel: 1 }];
    save.m_CurrentGradeCardSubmitSet = { m_CardDataList: [physical({ monsterType: 1 })] };
    save.m_PackageBoxCardSaveDataList = [{ cardDataList: [physical({ monsterType: 1, cardGrade: 10 })] }];
    save.m_HoldCardDataList = [physical({ monsterType: 1 }), physical({ monsterType: 9999 })];
    save.m_BulkDonationSaveDataList = [{ compactCardDataAmountList: [compact(0, 0, 2)] }];
    save.m_AutoPackOpenerSaveDataList = [{ compactCardDataAmountList: [compact(0, 0, 1)] }];

    const s = parseSave(save);
    const burpig = card('tetramon', 0, 21);
    const pigni = card('tetramon', 0, 0);
    const at = (loc: keyof typeof s.counts, c: typeof pigni, col: number) => s.counts[loc][c.i * COLS + col];

    expect(at('binder', burpig, 0)).toBe(2);
    expect(at('binder', burpig, 10)).toBe(2);
    expect(at('binder', burpig, 7)).toBe(1);
    expect(at('binder', card('ghost', 1, 39), 0)).toBe(1);
    expect(at('binder', card('ghost', 1, 1), 9)).toBe(1);
    expect(at('displays', burpig, 8)).toBe(1);
    expect(at('displays', card('ascension', 0, 5), 0)).toBe(1);
    expect(at('displays', pigni, 3)).toBe(1);
    expect(at('storage', card('destiny', 0, 0), 0)).toBe(5);
    expect(at('storage', card('destiny', 0, 0), 6)).toBe(1);
    expect(at('decks', pigni, 0)).toBe(4);
    expect(at('grading', pigni, 0)).toBe(2);
    expect(at('boxes', pigni, 10)).toBe(1);
    expect(at('hand', pigni, 0)).toBe(1);
    expect(at('donation', pigni, 0)).toBe(2);
    expect(at('openers', pigni, 0)).toBe(1);
    expect(s.unknown).toHaveLength(1);
    expect(s.unknown[0]).toMatchObject({ location: 'hand', reason: 'Unknown card' });
  });

  it('buckets grades outside 1-10 as other grades', () => {
    const save = emptySave();
    save.m_GradedCardInventoryList = [compact(0, 0, 380009117, { gradedCardIndex: 1 })];
    const s = parseSave(save);
    expect(s.counts.binder[card('tetramon', 0, 0).i * COLS + COL_OTHER]).toBe(1);
    expect(s.moddedGradeCount).toBe(1);
    expect(s.warnings.some((w) => w.includes('outside 1-10'))).toBe(true);
  });

  it('handles pre-1.0 saves without Ascension or grading lists', () => {
    const save = emptySave();
    delete save.m_CardCollectedListAscension;
    delete save.m_GradedCardInventoryList;
    const s = parseSave(save);
    expect(s.meta.hasAscension).toBe(false);
    expect(s.warnings.length).toBe(2);
  });
});

describe('aggregation', () => {
  it('reports unique owned and total copies per column and roll-up', () => {
    const save = emptySave();
    const album = save.m_CardCollectedList as number[];
    album[0] = 3;
    album[1] = 1;
    save.m_GradedCardInventoryList = [compact(0, 0, 10, { gradedCardIndex: 1 }), compact(0, 2, 10, { gradedCardIndex: 2 }), compact(0, 2, 9, { gradedCardIndex: 3 })];
    const s = parseSave(save);
    const totals = combineLocations(s.counts, all);
    const cards = CATALOG.bySet.get('tetramon')!;
    expect(aggregate(cards, totals, 0)).toEqual({ total: 1452, owned: 2, copies: 4 });
    expect(aggregate(cards, totals, 10)).toEqual({ total: 1452, owned: 2, copies: 2 });
    expect(aggregate(cards, totals, 'graded')).toEqual({ total: 1452, owned: 2, copies: 3 });
    expect(aggregate(cards, totals, 'any')).toEqual({ total: 1452, owned: 3, copies: 7 });
    expect(copies(totals, cards[2]!, 'graded')).toBe(2);
  });

  it('respects excluded locations', () => {
    const save = emptySave();
    (save.m_CardCollectedList as number[])[0] = 1;
    save.m_DeckCompactCardDataList = [{ compactCardDataAmountList: [compact(0, 0, 4)] }];
    const s = parseSave(save);
    const binderOnly = combineLocations(s.counts, new Set(['binder'] as const));
    expect(copies(binderOnly, card('tetramon', 0, 0), 0)).toBe(1);
    expect(copies(combineLocations(s.counts, all), card('tetramon', 0, 0), 0)).toBe(5);
  });

  it('builds the set > edition > foil drill-down', () => {
    const tree = buildTree(visibleSets(null), DEFAULT_FILTERS);
    expect(tree.map((r) => r.label)).toEqual(['Tetramon', 'Destiny', 'Ascension', 'Ghost']);
    const tetramon = tree[0]!;
    expect(tetramon.children.map((r) => r.label)).toEqual(['Basic', 'First Edition', 'Silver', 'Gold', 'EX', 'Full Art']);
    expect(tetramon.children[0]!.cards).toHaveLength(242);
    expect(tetramon.children[0]!.children.map((r) => [r.label, r.cards.length])).toEqual([
      ['Non-foil', 121],
      ['Foil', 121],
    ]);
    const ghost = tree[3]!;
    expect(ghost.children.map((r) => [r.label, r.cards.length])).toEqual([
      ['White', 40],
      ['Black', 40],
    ]);
  });

  it('filters by rarity tier', () => {
    const tree = buildTree(visibleSets(null), { ...DEFAULT_FILTERS, tiers: new Set(['Legendary'] as const) });
    expect(tree[0]!.cards).toHaveLength(26 * 12);
    expect(tree[3]!.cards).toHaveLength(80);
  });

  it('reveals mod-only sets only when the save has their cards', () => {
    const save = emptySave();
    (save.m_CardCollectedListMegabot as number[])[0] = 1;
    expect(visibleSets(parseSave(save)).map((s) => s.key)).toContain('megabot');
    expect(visibleSets(parseSave(emptySave())).map((s) => s.key)).not.toContain('megabot');
  });
});

describe('value', () => {
  const pigni = card('tetramon', 0, 0);

  it('applies the daily percent change and rounds to cents', () => {
    expect(marketPrice(pigni, 0, mp(20, -25), [])).toBe(15);
    expect(marketPrice(pigni, 0, mp(100, 20), [])).toBe(120);
    expect(marketPrice(pigni, 0, mp(0.2), [])).toBe(0.2);
  });

  it('prices graded copies with the per-card multiplier plus the grade bonus', () => {
    const mult = Array(20000).fill(2);
    expect(marketPrice(pigni, 10, mp(100, 20), mult)).toBe(264); // 240 + 2*12
    expect(marketPrice(pigni, 9, mp(100, 20), mult)).toBe(256); // 240 + 2*8
    expect(marketPrice(pigni, 6, mp(100, 20), mult)).toBe(240);
    // multiplier index = (saveIndex*10 + grade-1) % count
    const varied = Array.from({ length: 20000 }, (_, i) => (i === 21 * 10 + 4 ? 0.5 : 1));
    expect(marketPrice(card('tetramon', 0, 21), 5, mp(10), varied)).toBe(5);
    expect(marketPrice(pigni, 10, mp(100), [])).toBeNull();
  });

  it('reads Ghost prices from the swapped market list', () => {
    const save = emptySave();
    save.m_GenCardMarketPriceListGhostBlack = [mp(111)]; // White half reads ...GhostBlack
    save.m_GenCardMarketPriceListGhost = [mp(222)]; // Black half reads ...Ghost
    (save.m_CardCollectedListGhost as number[])[0] = 1;
    (save.m_CardCollectedListGhostBlack as number[])[0] = 2;
    const s = parseSave(save);
    const v = valueOf(s, combineLocations(s.counts, all), CATALOG.bySet.get('ghost')!);
    expect(v).toEqual({ ungraded: 111 + 2 * 222, graded: 0, unpriced: 0 });
  });
});

describe('snapshots', () => {
  it('round-trips sparse counts', () => {
    const a = new Int32Array(CATALOG.cards.length * COLS);
    a[5] = 2;
    a[a.length - 1] = 7;
    expect(Array.from(fromSparse(toSparse(a)))).toEqual(Array.from(a));
  });

  it('dedupes identical saves and diffs two saves', () => {
    const s1 = emptySave();
    (s1.m_CardCollectedList as number[])[0] = 1;
    const s2 = emptySave();
    (s2.m_CardCollectedList as number[])[0] = 2;
    (s2.m_CardCollectedList as number[])[1] = 1;
    s2.m_GradedCardInventoryList = [compact(0, 0, 10, { gradedCardIndex: 1 })];
    s2.m_CurrentDay = 20;
    const p1 = parseSave(s1);
    const a = makeSnapshot(p1, { name: 'savedGames_Release0.json', lastModified: 1 }, 1);
    const a2 = makeSnapshot(p1, { name: 'copy.json', lastModified: 2 }, 2);
    const b = makeSnapshot(parseSave(s2), { name: 'savedGames_Release0.json', lastModified: 3 }, 3);
    expect(a.id).toBe(a2.id);
    expect(a.id).not.toBe(b.id);
    expect(copies(snapshotTotals(b, all), card('tetramon', 0, 0), 'any')).toBe(3);

    const d = diffSnapshots(a, b, all, [0, 10], ['tetramon']);
    expect(d.sets).toEqual([
      { set: 'tetramon', col: 0, ownedFrom: 1, ownedTo: 2, copiesFrom: 1, copiesTo: 3 },
      { set: 'tetramon', col: 10, ownedFrom: 0, ownedTo: 1, copiesFrom: 0, copiesTo: 1 },
    ]);
    expect(d.cards.map((c) => [cardLabel(c.card), c.col, c.from, c.to])).toEqual([
      ['Pigni · Basic', 10, 0, 1],
      ['Pigni · First Edition', 0, 0, 1],
      ['Pigni · Basic', 0, 1, 2],
    ]);
  });
});
