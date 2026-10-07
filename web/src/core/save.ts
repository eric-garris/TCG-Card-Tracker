/**
 * Reads a TCG Card Shop Simulator save (savedGames_Release<N>.json, written by Unity's
 * JsonUtility from CGameData) and normalises everything card-related.
 *
 * Nothing here trusts the file: every field is optional, wrong types are ignored, and
 * unknown cards are counted rather than thrown on.
 */

import {
  CATALOG,
  cardFromCompact,
  cardFromPhysical,
  setForExpansion,
  type CardDef,
  type SetKey,
} from './catalog';

/** Where a card copy sits in the shop. */
export type LocationKey =
  | 'binder'
  | 'displays'
  | 'storage'
  | 'decks'
  | 'grading'
  | 'boxes'
  | 'hand'
  | 'donation'
  | 'openers';

export const LOCATIONS: readonly { key: LocationKey; label: string; hint: string }[] = [
  { key: 'binder', label: 'Binders', hint: 'Card album and graded card album' },
  { key: 'displays', label: 'Displays & shelves', hint: 'Card shelves, display cases, wall displays, projectors, prize shelves' },
  { key: 'storage', label: 'Storage shelves', hint: 'Card storage shelves' },
  { key: 'decks', label: 'Decks', hint: 'Cards locked in saved decks' },
  { key: 'grading', label: 'At grading', hint: 'Cards sent to (or staged for) the grading service' },
  { key: 'boxes', label: 'Card boxes', hint: 'Unopened card package boxes, e.g. returned graded cards' },
  { key: 'hand', label: 'In hand', hint: 'Cards the player is holding' },
  { key: 'donation', label: 'Bulk donation box', hint: 'Cards placed in bulk donation boxes' },
  { key: 'openers', label: 'Auto pack openers', hint: 'Cards waiting in auto pack openers' },
];

/** Count columns: 0 = ungraded, 1..10 = grade, 11 = grade from a grading mod (not 1-10). */
export const COLS = 12;
export const COL_OTHER = 11;

export interface MarketPrice {
  generatedMarketPrice: number;
  pricePercentChangeList: number;
  pastPricePercentChangeList: number[];
}

export interface SaveMeta {
  playerName: string | null;
  day: number | null;
  shopLevel: number | null;
  coins: number | null;
  saveIndex: number | null;
  hasAscension: boolean;
}

export interface UnknownCard {
  location: LocationKey;
  reason: string;
  detail: string;
}

export interface ParsedSave {
  meta: SaveMeta;
  /** Per-location copy counts, each Int32Array(cards.length * COLS). */
  counts: Record<LocationKey, Int32Array>;
  /** Per card: market price record, or null when absent. Indexed by CardDef.i. */
  market: (MarketPrice | null)[];
  gradedMultipliers: number[];
  /** Card indices the game has ever marked as collected (m_IsCardCollectedList*). */
  everCollected: Uint8Array;
  /** Sets that have any data in the save (used to reveal mod-only sets). */
  setsWithData: Set<SetKey>;
  unknown: UnknownCard[];
  /** Graded copies whose grade is not 1-10 (e.g. Grading Overhaul's encoded grades). */
  moddedGradeCount: number;
  warnings: string[];
}

export class SaveFormatError extends Error {}

type Json = Record<string, unknown>;

function isObj(v: unknown): v is Json {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

function arr(v: unknown): unknown[] {
  return Array.isArray(v) ? v : [];
}

function num(v: unknown, fallback = 0): number {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'boolean') return v ? 1 : 0;
  if (typeof v === 'string' && v.trim() !== '' && Number.isFinite(Number(v))) return Number(v);
  return fallback;
}

function bool(v: unknown): boolean {
  return v === true || v === 1 || v === 'true';
}

function optNum(v: unknown): number | null {
  return typeof v === 'number' && Number.isFinite(v) ? v : null;
}

/**
 * Accepts the raw game save, plus the wrappers some mods write around it:
 * Archipelago's {UnityGameData: "<json string>"} and older {gameData: {...}}.
 */
export function unwrapSave(root: unknown): Json {
  if (isObj(root)) {
    if (Array.isArray(root.m_CardCollectedList)) return root;
    if (typeof root.UnityGameData === 'string') {
      try {
        return unwrapSave(JSON.parse(root.UnityGameData));
      } catch {
        throw new SaveFormatError('This looks like an Archipelago save, but its game data could not be read.');
      }
    }
    if (isObj(root.gameData)) return unwrapSave(root.gameData);
  }
  throw new SaveFormatError(
    'This file is not a TCG Card Shop Simulator save. Look for savedGames_Release0.json (autosave) or savedGames_Release1-3.json.',
  );
}

export function parseSaveText(text: string): ParsedSave {
  // Files written by the game can start with a UTF-8 BOM.
  const clean = text.replace(/^﻿/, '').trimStart();
  if (clean.startsWith('\u0000') || /^[\x00-\x08]/.test(clean)) {
    throw new SaveFormatError(
      'This looks like a binary .gd save. Upload the matching .json file from the same folder instead.',
    );
  }
  let root: unknown;
  try {
    root = JSON.parse(clean);
  } catch {
    throw new SaveFormatError('The file is not valid JSON. If the game was saving at the time, try again in a moment.');
  }
  return parseSave(root);
}

const LOCATION_SOURCES: readonly { key: LocationKey; field: string; nested: boolean }[] = [
  { key: 'displays', field: 'm_CardShelfSaveDataList', nested: true },
  { key: 'displays', field: 'm_CardItemCombiShelfSaveDataList', nested: true },
  { key: 'storage', field: 'm_CardStorageShelfSaveDataList', nested: true },
  { key: 'decks', field: 'm_DeckCompactCardDataList', nested: true },
  { key: 'grading', field: 'm_GradeCardInProgressList', nested: true },
  { key: 'boxes', field: 'm_PackageBoxCardSaveDataList', nested: true },
  { key: 'hand', field: 'm_HoldCardDataList', nested: false },
  { key: 'donation', field: 'm_BulkDonationSaveDataList', nested: true },
  { key: 'openers', field: 'm_AutoPackOpenerSaveDataList', nested: true },
];

function looksPhysical(v: unknown): v is Json {
  return isObj(v) && 'monsterType' in v;
}

function looksCompact(v: unknown): v is Json {
  return isObj(v) && 'cardSaveIndex' in v;
}

export function parseSave(root: unknown): ParsedSave {
  const save = unwrapSave(root);
  const n = CATALOG.cards.length;
  const counts = Object.fromEntries(
    (['binder', 'displays', 'storage', 'decks', 'grading', 'boxes', 'hand', 'donation', 'openers'] as LocationKey[]).map(
      (k) => [k, new Int32Array(n * COLS)],
    ),
  ) as Record<LocationKey, Int32Array>;
  const unknown: UnknownCard[] = [];
  const warnings: string[] = [];
  const setsWithData = new Set<SetKey>();
  let moddedGradeCount = 0;

  const add = (loc: LocationKey, card: CardDef, grade: number, copies: number) => {
    if (copies <= 0) return;
    let col: number;
    if (grade <= 0) col = 0;
    else if (grade <= 10 && Number.isInteger(grade)) col = grade;
    else {
      col = COL_OTHER;
      moddedGradeCount += copies;
    }
    counts[loc][card.i * COLS + col]! += copies;
    setsWithData.add(card.set);
  };

  // ---- Binder: ungraded album counts ----
  for (const set of CATALOG.sets) {
    const setCards = CATALOG.bySet.get(set.key)!;
    const perList = set.shown.length * set.perMonsterNoFoil * 2;
    set.lists.forEach((list, li) => {
      const field = 'm_CardCollectedList' + list.suffix;
      const values = save[field];
      if (!Array.isArray(values)) return;
      for (let idx = 0; idx < Math.min(values.length, perList); idx++) {
        const c = num(values[idx]);
        if (c > 0) add('binder', setCards[li * perList + idx]!, 0, c);
      }
    });
  }

  // ---- Binder: graded album (one row per graded copy, `amount` holds the grade) ----
  for (const row of arr(save.m_GradedCardInventoryList)) {
    if (!isObj(row)) continue;
    const card = cardFromCompact(num(row.expansionType, -1), num(row.cardSaveIndex, -1), bool(row.isDestiny));
    const grade = num(row.amount);
    if (!card) {
      unknown.push({ location: 'binder', reason: 'Unknown graded card', detail: compactDetail(row) });
      continue;
    }
    if (grade <= 0) {
      unknown.push({ location: 'binder', reason: 'Graded card without a grade', detail: compactDetail(row) });
      continue;
    }
    add('binder', card, grade, 1);
  }

  // ---- Everywhere else ----
  const takePhysical = (loc: LocationKey, c: Json) => {
    const monsterType = num(c.monsterType);
    if (monsterType <= 0) return; // empty display slot
    const card = cardFromPhysical(
      num(c.expansionType, -1),
      monsterType,
      num(c.borderType),
      bool(c.isFoil),
      bool(c.isDestiny),
    );
    if (!card) {
      unknown.push({ location: loc, reason: 'Unknown card', detail: physicalDetail(c) });
      return;
    }
    add(loc, card, num(c.cardGrade), 1);
  };
  const takeCompact = (loc: LocationKey, c: Json) => {
    const card = cardFromCompact(num(c.expansionType, -1), num(c.cardSaveIndex, -1), bool(c.isDestiny));
    if (!card) {
      unknown.push({ location: loc, reason: 'Unknown card', detail: compactDetail(c) });
      return;
    }
    // Compact rows hold a copy count, except graded rows (gradedCardIndex > 0) whose
    // `amount` is the grade of a single copy.
    if (num(c.gradedCardIndex) > 0) add(loc, card, num(c.amount), 1);
    else add(loc, card, 0, num(c.amount));
  };
  const takeContainer = (loc: LocationKey, container: unknown) => {
    if (!isObj(container)) return;
    for (const value of Object.values(container)) {
      if (!Array.isArray(value) || value.length === 0) continue;
      for (const item of value) {
        if (looksPhysical(item)) takePhysical(loc, item);
        else if (looksCompact(item)) takeCompact(loc, item);
      }
    }
  };

  for (const src of LOCATION_SOURCES) {
    for (const item of arr(save[src.field])) {
      if (src.nested) takeContainer(src.key, item);
      else if (looksPhysical(item)) takePhysical(src.key, item);
      else if (looksCompact(item)) takeCompact(src.key, item);
    }
  }
  // The submission currently being assembled on the grading website.
  takeContainer('grading', save.m_CurrentGradeCardSubmitSet);

  // ---- Prices ----
  const market: (MarketPrice | null)[] = new Array(n).fill(null);
  for (const set of CATALOG.sets) {
    const setCards = CATALOG.bySet.get(set.key)!;
    const perList = set.shown.length * set.perMonsterNoFoil * 2;
    set.lists.forEach((list, li) => {
      const values = save['m_GenCardMarketPriceList' + list.marketSuffix];
      if (!Array.isArray(values)) return;
      for (let idx = 0; idx < Math.min(values.length, perList); idx++) {
        const p = values[idx];
        if (!isObj(p)) continue;
        market[setCards[li * perList + idx]!.i] = {
          generatedMarketPrice: num(p.generatedMarketPrice),
          pricePercentChangeList: num(p.pricePercentChangeList),
          pastPricePercentChangeList: arr(p.pastPricePercentChangeList).map((x) => num(x)),
        };
      }
    });
  }
  const gradedMultipliers = arr(save.m_GenGradedCardPriceMultiplierList).map((x) => num(x));

  // ---- Ever collected ----
  const everCollected = new Uint8Array(n);
  for (const set of CATALOG.sets) {
    const setCards = CATALOG.bySet.get(set.key)!;
    const perList = set.shown.length * set.perMonsterNoFoil * 2;
    set.lists.forEach((list, li) => {
      const values = save['m_IsCardCollectedList' + list.suffix];
      if (!Array.isArray(values)) return;
      for (let idx = 0; idx < Math.min(values.length, perList); idx++) {
        if (bool(values[idx])) {
          const card = setCards[li * perList + idx]!;
          everCollected[card.i] = 1;
          setsWithData.add(card.set);
        }
      }
    });
  }

  const hasAscension = Array.isArray(save.m_CardCollectedListAscension);
  if (!hasAscension) {
    warnings.push('This save is from before the 1.0 update, so it has no Ascension cards.');
  }
  if (!Array.isArray(save.m_GradedCardInventoryList)) {
    warnings.push('This save has no graded card album (it may predate card grading in v0.60).');
  }
  if (moddedGradeCount > 0) {
    warnings.push(
      `${moddedGradeCount} graded card${moddedGradeCount === 1 ? ' has' : 's have'} a grade outside 1-10, which grading mods such as Grading Overhaul write. They are counted under "Other grade".`,
    );
  }

  const coinsD = optNum(save.m_CoinAmountDouble);
  return {
    meta: {
      playerName: typeof save.m_PlayerName === 'string' && save.m_PlayerName.trim() ? save.m_PlayerName.trim() : null,
      day: optNum(save.m_CurrentDay),
      shopLevel: optNum(save.m_ShopLevel),
      coins: coinsD !== null && coinsD !== 0 ? coinsD : optNum(save.m_CoinAmount) ?? coinsD,
      saveIndex: optNum(save.m_SaveIndex),
      hasAscension,
    },
    counts,
    market,
    gradedMultipliers,
    everCollected,
    setsWithData,
    unknown,
    moddedGradeCount,
    warnings,
  };
}

function physicalDetail(c: Json): string {
  const exp = num(c.expansionType, -1);
  const set = setForExpansion(exp);
  return `expansion ${set?.name ?? exp}, monster ${num(c.monsterType)}, border ${num(c.borderType)}${bool(c.isFoil) ? ', foil' : ''}`;
}

function compactDetail(c: Json): string {
  const exp = num(c.expansionType, -1);
  const set = setForExpansion(exp);
  return `expansion ${set?.name ?? exp}, index ${num(c.cardSaveIndex, -1)}${bool(c.isDestiny) ? ', destiny/black' : ''}`;
}
