/**
 * Market value, reproducing the game's MarketPrice.GetMarketPrice in float32 arithmetic.
 *
 *   ungraded: RoundToInt(base * (1 + pct/100) * 100) / 100
 *   graded:   m = gradedMultipliers[(saveIndex*10 + grade-1) % count]
 *             RoundToInt(m * base * (1 + pct/100) * 100) / 100 + bonus
 *             bonus = m * 12 / 8 / 4 / 2 for grade 10 / 9 / 8 / 7, else 0
 *
 * Unity's Mathf.RoundToInt rounds half to even. Values are in the game's base currency (USD);
 * the game converts for display when another currency is selected.
 */

import type { CardDef } from './catalog';
import type { MarketPrice } from './save';

const f = Math.fround;

function roundHalfEven(x: number): number {
  const r = Math.round(x);
  // Math.round rounds .5 up; move exact halves to the even neighbour.
  if (Math.abs(x % 1) === 0.5) return 2 * Math.round(x / 2);
  return r;
}

function adjusted(base: number, pct: number, m: number | null): number {
  const factor = f(1 + f(pct / 100));
  const scaled = m === null ? f(base) : f(f(m) * f(base));
  return f(roundHalfEven(f(f(scaled * factor) * 100)) / 100);
}

export function gradeBonus(m: number, grade: number): number {
  if (grade >= 10) return f(m * 12);
  if (grade === 9) return f(m * 8);
  if (grade === 8) return f(m * 4);
  if (grade === 7) return f(m * 2);
  return 0;
}

export function multiplierFor(card: CardDef, grade: number, multipliers: readonly number[]): number | null {
  if (multipliers.length === 0) return null;
  const k = Math.abs(card.saveIndex * 10 + (grade - 1)) % multipliers.length;
  return multipliers[k] ?? null;
}

/** Current market price for one copy at `grade` (0 = ungraded), or null if unknown. */
export function marketPrice(
  card: CardDef,
  grade: number,
  price: MarketPrice | null,
  multipliers: readonly number[],
  pct: number = price?.pricePercentChangeList ?? 0,
): number | null {
  if (!price || !(price.generatedMarketPrice > 0)) return null;
  // The game keeps prices as float32; report them as plain cents.
  if (grade <= 0) return Math.round(adjusted(price.generatedMarketPrice, pct, null) * 100) / 100;
  if (grade > 10) return null;
  const m = multiplierFor(card, grade, multipliers);
  if (m === null) return null;
  const v = f(adjusted(price.generatedMarketPrice, pct, m) + gradeBonus(m, grade));
  return Math.round(v * 100) / 100;
}

export function formatMoney(v: number | null | undefined, opts: { compact?: boolean } = {}): string {
  if (v === null || v === undefined || !Number.isFinite(v)) return '—';
  if (opts.compact && Math.abs(v) >= 10_000) {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      notation: 'compact',
      maximumFractionDigits: 1,
    }).format(v);
  }
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(v);
}
