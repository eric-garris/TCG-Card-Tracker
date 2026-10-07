/**
 * Turns per-location counts into the numbers the UI shows: unique cards owned and total
 * copies per column (Ungraded, Grade 1..10, any graded, any form), per set / edition / foil.
 */

import { CATALOG, TIERS, type CardDef, type SetDef, type SetKey, type Tier } from './catalog';
import { COLS, COL_OTHER, LOCATIONS, type LocationKey, type ParsedSave } from './save';
import { marketPrice } from './value';

/** 0 = ungraded, 1..10 = grade, 11 = other (modded) grade, plus two roll-ups. */
export type Column = number | 'graded' | 'any';

export const GRADE_COLUMNS: readonly Column[] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

export function columnLabel(col: Column, short = false): string {
  if (col === 'any') return short ? 'Any' : 'Any form';
  if (col === 'graded') return short ? 'Graded' : 'Any grade';
  if (col === 0) return 'Ungraded';
  if (col === COL_OTHER) return short ? 'Other' : 'Other grade';
  return short ? String(col) : `Grade ${col}`;
}

export const ALL_LOCATIONS: readonly LocationKey[] = LOCATIONS.map((l) => l.key);

/** Sum the selected locations into one counts array. */
export function combineLocations(
  counts: Partial<Record<LocationKey, Int32Array>>,
  included: ReadonlySet<LocationKey>,
): Int32Array {
  const out = new Int32Array(CATALOG.cards.length * COLS);
  for (const loc of ALL_LOCATIONS) {
    if (!included.has(loc)) continue;
    const src = counts[loc];
    if (!src) continue;
    for (let i = 0; i < out.length; i++) out[i]! += src[i]!;
  }
  return out;
}

export function copies(totals: Int32Array, card: CardDef, col: Column): number {
  const base = card.i * COLS;
  if (typeof col === 'number') return totals[base + col]!;
  let sum = 0;
  for (let c = col === 'any' ? 0 : 1; c < COLS; c++) sum += totals[base + c]!;
  return sum;
}

export interface Agg {
  total: number;
  owned: number;
  copies: number;
}

export function aggregate(cards: readonly CardDef[], totals: Int32Array, col: Column): Agg {
  let owned = 0;
  let n = 0;
  for (const card of cards) {
    const c = copies(totals, card, col);
    if (c > 0) owned++;
    n += c;
  }
  return { total: cards.length, owned, copies: n };
}

export interface Filters {
  tiers: ReadonlySet<Tier>;
  /** null = both */
  foil: boolean | null;
  search: string;
}

export const DEFAULT_FILTERS: Filters = { tiers: new Set(TIERS), foil: null, search: '' };

export function cardMatches(card: CardDef, f: Filters): boolean {
  if (!f.tiers.has(card.tier)) return false;
  if (f.foil !== null && card.foil !== f.foil) return false;
  if (f.search) {
    const q = f.search.trim().toLowerCase();
    const n = q.replace(/^#/, '');
    // Numbers match the binder number, with or without leading zeros ("7", "#007").
    const numberMatch = /^\d+$/.test(n) && Number(n) === card.num;
    if (q && !numberMatch && !card.name.toLowerCase().includes(q)) return false;
  }
  return true;
}

export interface TreeRow {
  id: string;
  label: string;
  depth: 0 | 1 | 2;
  set: SetKey;
  /** Drill-down group (edition or Ghost half); undefined at set level. */
  group?: string;
  foil?: boolean;
  cards: CardDef[];
  children: TreeRow[];
}

/** Sets shown to the user: every released set, plus mod-only sets that have data. */
export function visibleSets(save: ParsedSave | null): SetDef[] {
  return CATALOG.sets.filter((s) => {
    if (s.hiddenUnlessOwned) return !!save?.setsWithData.has(s.key);
    return true;
  });
}

export function buildTree(sets: readonly SetDef[], filters: Filters): TreeRow[] {
  const rows: TreeRow[] = [];
  for (const set of sets) {
    const setCards = CATALOG.bySet.get(set.key)!.filter((c) => cardMatches(c, filters));
    const groupRows: TreeRow[] = [];
    for (const g of CATALOG.groups.get(set.key)!) {
      const gCards = setCards.filter((c) => c.group === g);
      if (gCards.length === 0) continue;
      const foilRows: TreeRow[] = [];
      for (const foil of [false, true]) {
        const fCards = gCards.filter((c) => c.foil === foil);
        if (fCards.length === 0) continue;
        foilRows.push({
          id: `${set.key}/${g}/${foil ? 'foil' : 'nonfoil'}`,
          label: foil ? 'Foil' : 'Non-foil',
          depth: 2,
          set: set.key,
          group: g,
          foil,
          cards: fCards,
          children: [],
        });
      }
      groupRows.push({ id: `${set.key}/${g}`, label: g, depth: 1, set: set.key, group: g, cards: gCards, children: foilRows });
    }
    rows.push({ id: set.key, label: set.name, depth: 0, set: set.key, cards: setCards, children: groupRows });
  }
  return rows;
}

// ---------- Value ----------

export interface ValueBreakdown {
  ungraded: number;
  graded: number;
  /** Copies with no price data (missing market entry or modded grade). */
  unpriced: number;
}

export function unitPrice(save: ParsedSave, card: CardDef, grade: number): number | null {
  return marketPrice(card, grade, save.market[card.i] ?? null, save.gradedMultipliers);
}

export function valueOf(save: ParsedSave, totals: Int32Array, cards: readonly CardDef[]): ValueBreakdown {
  const out: ValueBreakdown = { ungraded: 0, graded: 0, unpriced: 0 };
  for (const card of cards) {
    const base = card.i * COLS;
    for (let col = 0; col < COLS; col++) {
      const n = totals[base + col]!;
      if (n === 0) continue;
      const p = col === COL_OTHER ? null : unitPrice(save, card, col);
      if (p === null) {
        out.unpriced += n;
        continue;
      }
      if (col === 0) out.ungraded += p * n;
      else out.graded += p * n;
    }
  }
  out.ungraded = Math.round(out.ungraded * 100) / 100;
  out.graded = Math.round(out.graded * 100) / 100;
  return out;
}

/** Value per location for the whole catalog (used for snapshots and the value tab). */
export function valueByLocation(save: ParsedSave): Record<LocationKey, ValueBreakdown> {
  const out = {} as Record<LocationKey, ValueBreakdown>;
  for (const loc of ALL_LOCATIONS) out[loc] = valueOf(save, save.counts[loc], CATALOG.cards);
  return out;
}

export function percent(owned: number, total: number): number {
  return total === 0 ? 0 : (owned / total) * 100;
}

/**
 * Percent for display. Never rounds an incomplete set up to 100% (or a non-empty one down
 * to 0%): completionists care exactly about that last card.
 */
export function displayPercent(owned: number, total: number): number {
  if (total === 0) return 0;
  const p = percent(owned, total);
  const digits = p >= 10 ? 0 : 1;
  const f = 10 ** digits;
  let r = Math.round(p * f) / f;
  if (owned < total && r >= 100) r = Math.floor(p * 10) / 10;
  if (owned > 0 && r <= 0) r = Math.ceil(p * 10) / 10;
  return r;
}

export function formatPercent(owned: number, total: number): string {
  if (total === 0) return '—';
  const p = percent(owned, total);
  if (p > 0 && p < 0.1) return '<0.1%';
  if (owned < total && p >= 99.95) return '>99.9%';
  const r = displayPercent(owned, total);
  return `${Number.isInteger(r) ? r : r.toFixed(1)}%`;
}
