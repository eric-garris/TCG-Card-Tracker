/**
 * Compact, storable summaries of a save so progress can be compared over time without
 * keeping the (multi-megabyte) save files themselves.
 */

import { CATALOG, CATALOG_VERSION, type CardDef, type SetKey } from './catalog';
import { COLS, LOCATIONS, type LocationKey, type ParsedSave } from './save';
import { ALL_LOCATIONS, combineLocations, copies, valueByLocation, type Column, type ValueBreakdown } from './collection';

export interface Snapshot {
  id: string;
  catalogVersion: number;
  /** When the file was loaded into the tracker (ms since epoch). */
  addedAt: number;
  fileName: string;
  /** File's last-modified time (ms), i.e. roughly when the game saved it. */
  fileModified: number;
  playerName: string | null;
  day: number | null;
  shopLevel: number | null;
  coins: number | null;
  /** Sparse counts per location, flattened as [cell, count, cell, count, ...], cell = card.i*COLS+col. */
  counts: Partial<Record<LocationKey, number[]>>;
  value: Partial<Record<LocationKey, ValueBreakdown>>;
}

export function toSparse(a: Int32Array): number[] {
  const out: number[] = [];
  for (let i = 0; i < a.length; i++) {
    const v = a[i]!;
    if (v !== 0) out.push(i, v);
  }
  return out;
}

export function fromSparse(s: readonly number[] | undefined): Int32Array {
  const out = new Int32Array(CATALOG.cards.length * COLS);
  if (!s) return out;
  for (let i = 0; i + 1 < s.length; i += 2) {
    const cell = s[i]!;
    if (cell >= 0 && cell < out.length) out[cell] = s[i + 1]!;
  }
  return out;
}

/** FNV-1a over the content that matters, so re-uploading the same save is a no-op. */
function contentHash(parts: readonly (string | number | null)[]): string {
  let h = 0x811c9dc5;
  for (const part of parts) {
    const s = String(part);
    for (let i = 0; i < s.length; i++) {
      h ^= s.charCodeAt(i);
      h = Math.imul(h, 0x01000193) >>> 0;
    }
    h ^= 0x7c;
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, '0');
}

export function makeSnapshot(
  save: ParsedSave,
  file: { name: string; lastModified: number },
  addedAt: number,
): Snapshot {
  const counts: Partial<Record<LocationKey, number[]>> = {};
  for (const loc of ALL_LOCATIONS) {
    const sparse = toSparse(save.counts[loc]);
    if (sparse.length) counts[loc] = sparse;
  }
  const value = valueByLocation(save);
  const parts: (string | number | null)[] = [save.meta.playerName, save.meta.day, save.meta.coins];
  for (const loc of ALL_LOCATIONS) parts.push(loc, (counts[loc] ?? []).join(','));
  return {
    id: contentHash(parts),
    catalogVersion: CATALOG_VERSION,
    addedAt,
    fileName: file.name,
    fileModified: file.lastModified,
    playerName: save.meta.playerName,
    day: save.meta.day,
    shopLevel: save.meta.shopLevel,
    coins: save.meta.coins,
    counts,
    value,
  };
}

export function snapshotTotals(s: Snapshot, included: ReadonlySet<LocationKey>): Int32Array {
  const per: Partial<Record<LocationKey, Int32Array>> = {};
  for (const loc of ALL_LOCATIONS) if (included.has(loc)) per[loc] = fromSparse(s.counts[loc]);
  return combineLocations(per, included);
}

export function snapshotValue(s: Snapshot, included: ReadonlySet<LocationKey>): number {
  let v = 0;
  for (const loc of LOCATIONS) {
    if (!included.has(loc.key)) continue;
    const b = s.value[loc.key];
    if (b) v += b.ungraded + b.graded;
  }
  return Math.round(v * 100) / 100;
}

/** Order snapshots by in-game day, then by file time. */
export function sortSnapshots(list: readonly Snapshot[]): Snapshot[] {
  return [...list].sort(
    (a, b) => (a.day ?? -1) - (b.day ?? -1) || a.fileModified - b.fileModified || a.addedAt - b.addedAt,
  );
}

export function snapshotLabel(s: Snapshot): string {
  const day = s.day !== null ? `Day ${s.day}` : 'Unknown day';
  return `${day} · ${s.fileName}`;
}

export interface CardChange {
  card: CardDef;
  col: Column;
  from: number;
  to: number;
}

export interface SetChange {
  set: SetKey;
  col: Column;
  ownedFrom: number;
  ownedTo: number;
  copiesFrom: number;
  copiesTo: number;
}

export interface SnapshotDiff {
  sets: SetChange[];
  /** Cards whose copy count changed in a column; newly owned/lost first. */
  cards: CardChange[];
  valueFrom: number;
  valueTo: number;
}

export function diffSnapshots(
  from: Snapshot,
  to: Snapshot,
  included: ReadonlySet<LocationKey>,
  cols: readonly Column[],
  sets: readonly SetKey[],
): SnapshotDiff {
  const a = snapshotTotals(from, included);
  const b = snapshotTotals(to, included);
  const setChanges: SetChange[] = [];
  const cardChanges: CardChange[] = [];
  for (const set of sets) {
    const cards = CATALOG.bySet.get(set) ?? [];
    for (const col of cols) {
      let ownedFrom = 0;
      let ownedTo = 0;
      let copiesFrom = 0;
      let copiesTo = 0;
      for (const card of cards) {
        const x = copies(a, card, col);
        const y = copies(b, card, col);
        if (x > 0) ownedFrom++;
        if (y > 0) ownedTo++;
        copiesFrom += x;
        copiesTo += y;
        if (x !== y && typeof col === 'number') cardChanges.push({ card, col, from: x, to: y });
      }
      setChanges.push({ set, col, ownedFrom, ownedTo, copiesFrom, copiesTo });
    }
  }
  const rank = (c: CardChange) => (c.from === 0 ? 0 : c.to === 0 ? 1 : 2);
  cardChanges.sort((p, q) => rank(p) - rank(q) || p.card.i - q.card.i || (p.col as number) - (q.col as number));
  return {
    sets: setChanges,
    cards: cardChanges,
    valueFrom: snapshotValue(from, included),
    valueTo: snapshotValue(to, included),
  };
}
