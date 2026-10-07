import { useMemo } from 'preact/hooks';
import { CATALOG, GRADE_NAMES, type CardDef, type SetDef } from '../core/catalog';
import { unitPrice, valueOf } from '../core/collection';
import { COLS, COL_OTHER, LOCATIONS, type LocationKey, type ParsedSave } from '../core/save';
import { formatMoney } from '../core/value';
import { int } from './format';

interface Props {
  save: ParsedSave;
  totals: Int32Array;
  sets: SetDef[];
  included: ReadonlySet<LocationKey>;
}

interface Holding {
  card: CardDef;
  grade: number;
  copies: number;
  unit: number;
}

export function ValueView({ save, totals, sets, included }: Props) {
  const cards = useMemo(() => sets.flatMap((s) => CATALOG.bySet.get(s.key) ?? []), [sets]);
  const total = useMemo(() => valueOf(save, totals, cards), [save, totals, cards]);
  const bySet = useMemo(
    () => sets.map((s) => ({ set: s, v: valueOf(save, totals, CATALOG.bySet.get(s.key) ?? []) })),
    [save, totals, sets],
  );
  const byLoc = useMemo(
    () =>
      LOCATIONS.filter((l) => included.has(l.key)).map((l) => ({ loc: l, v: valueOf(save, save.counts[l.key], cards) })),
    [save, cards, included],
  );
  const top = useMemo(() => {
    const out: Holding[] = [];
    for (const card of cards) {
      for (let g = 0; g < COLS; g++) {
        if (g === COL_OTHER) continue;
        const n = totals[card.i * COLS + g]!;
        if (!n) continue;
        const unit = unitPrice(save, card, g);
        if (unit !== null) out.push({ card, grade: g, copies: n, unit });
      }
    }
    out.sort((a, b) => b.unit * b.copies - a.unit * a.copies);
    return out.slice(0, 25);
  }, [save, totals, cards]);

  const sum = total.ungraded + total.graded;
  const noPrices = save.market.every((m) => m === null);

  return (
    <div class="stack">
      {noPrices && (
        <div class="notice warn">This save has no market prices yet (they are generated when you first play a day), so values show as $0.</div>
      )}
      <div class="tiles">
        <div class="card tile">
          <div class="label">Total market value</div>
          <div class="value">{formatMoney(sum)}</div>
          <div class="detail">for the places you are counting</div>
        </div>
        <div class="card tile">
          <div class="label">Ungraded cards</div>
          <div class="value">{formatMoney(total.ungraded)}</div>
          <div class="detail">{sum > 0 ? `${((total.ungraded / sum) * 100).toFixed(0)}% of total` : '—'}</div>
        </div>
        <div class="card tile">
          <div class="label">Graded cards</div>
          <div class="value">{formatMoney(total.graded)}</div>
          <div class="detail">{sum > 0 ? `${((total.graded / sum) * 100).toFixed(0)}% of total` : '—'}</div>
        </div>
        {total.unpriced > 0 && (
          <div class="card tile">
            <div class="label">Copies without a price</div>
            <div class="value">{int(total.unpriced)}</div>
            <div class="detail">modded grades or missing market data</div>
          </div>
        )}
      </div>

      <div class="row" style={{ alignItems: 'stretch' }}>
        <div class="card grid-wrap" style={{ flex: '1 1 320px', minWidth: '0' }}>
          <table class="list">
            <thead>
              <tr>
                <th>Set</th>
                <th class="num">Ungraded</th>
                <th class="num">Graded</th>
                <th class="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {bySet.map(({ set, v }) => (
                <tr>
                  <td>{set.name}</td>
                  <td class="num">{formatMoney(v.ungraded)}</td>
                  <td class="num">{formatMoney(v.graded)}</td>
                  <td class="num">
                    <strong>{formatMoney(v.ungraded + v.graded)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div class="card grid-wrap" style={{ flex: '1 1 320px', minWidth: '0' }}>
          <table class="list">
            <thead>
              <tr>
                <th>Where</th>
                <th class="num">Ungraded</th>
                <th class="num">Graded</th>
                <th class="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {byLoc.map(({ loc, v }) => (
                <tr>
                  <td title={loc.hint}>{loc.label}</td>
                  <td class="num">{formatMoney(v.ungraded)}</td>
                  <td class="num">{formatMoney(v.graded)}</td>
                  <td class="num">
                    <strong>{formatMoney(v.ungraded + v.graded)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <div class="pad" style={{ paddingBottom: 0 }}>
          <strong>Most valuable holdings</strong>
        </div>
        {top.length === 0 ? (
          <p class="pad muted">No priced cards yet.</p>
        ) : (
          <div class="grid-wrap">
          <table class="list">
            <thead>
              <tr>
                <th>Card</th>
                <th>Set</th>
                <th>Condition</th>
                <th class="num">Copies</th>
                <th class="num">Each</th>
                <th class="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {top.map((h) => (
                <tr>
                  <td>
                    {h.card.name} <span class="pill">{h.card.set === 'ghost' ? h.card.group : h.card.editionName}</span>{' '}
                    {h.card.foil && <span class="pill foil">Foil</span>}
                  </td>
                  <td>{CATALOG.setByKey.get(h.card.set)?.name}</td>
                  <td>{h.grade === 0 ? 'Ungraded' : `Grade ${h.grade} · ${GRADE_NAMES[h.grade]}`}</td>
                  <td class="num">{int(h.copies)}</td>
                  <td class="num">{formatMoney(h.unit)}</td>
                  <td class="num">
                    <strong>{formatMoney(h.unit * h.copies)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          </div>
        )}
      </div>
      <p class="small muted">
        Values use the market prices stored in your save for its current in-game day, computed the same way the game
        does (graded prices use each card's grade multiplier plus the grade 7–10 bonus). Amounts are in the game's base
        currency (US dollars).
      </p>
    </div>
  );
}
