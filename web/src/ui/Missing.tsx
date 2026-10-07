import { useEffect, useMemo, useState } from 'preact/hooks';
import { CATALOG, TIERS, type CardDef, type SetDef, type SetKey } from '../core/catalog';
import { cardMatches, columnLabel, copies, unitPrice, type Column, type Filters } from '../core/collection';
import { COLS, COL_OTHER, type ParsedSave } from '../core/save';
import { formatMoney } from '../core/value';
import { FoilSeg, TierChips } from './Controls';
import { int } from './format';
import type { CellTarget } from './Overview';

type Show = 'missing' | 'owned' | 'all';
type Sort = 'number' | 'name' | 'price-desc' | 'price-asc';

interface Props {
  save: ParsedSave;
  totals: Int32Array;
  sets: SetDef[];
  preset: CellTarget | null;
}

const PAGE = 300;

/** Grade whose price the list shows: the column's grade, ungraded for roll-ups, none for modded grades. */
function colPriceGrade(col: Column): number | null {
  if (col === COL_OTHER) return null;
  return typeof col === 'number' && col >= 0 && col <= 10 ? col : 0;
}

function ownedAs(totals: Int32Array, card: CardDef): string {
  const parts: string[] = [];
  for (let c = 0; c < COLS; c++) {
    const n = totals[card.i * COLS + c]!;
    if (!n) continue;
    const name = c === 0 ? 'Ungraded' : c === COL_OTHER ? 'Other grade' : `G${c}`;
    parts.push(`${name} ×${n}`);
  }
  return parts.join(' · ');
}

export function Missing({ save, totals, sets, preset }: Props) {
  const [setKey, setSetKey] = useState<SetKey | 'all'>(sets[0]?.key ?? 'all');
  const [group, setGroup] = useState<string>('all');
  const [col, setCol] = useState<Column>(0);
  const [show, setShow] = useState<Show>('missing');
  const [sort, setSort] = useState<Sort>('number');
  const [filters, setFilters] = useState<Filters>({ tiers: new Set(TIERS), foil: null, search: '' });
  const [limit, setLimit] = useState(PAGE);

  useEffect(() => {
    if (!preset) return;
    setSetKey(preset.set);
    setGroup(preset.group ?? 'all');
    setCol(preset.col);
    setShow('missing');
    setFilters(preset.filters ?? { tiers: new Set(TIERS), foil: preset.foil ?? null, search: '' });
    setLimit(PAGE);
  }, [preset]);

  const groups = setKey === 'all' ? [] : (CATALOG.groups.get(setKey) ?? []);
  const priceGrade = colPriceGrade(col);

  const rows = useMemo(() => {
    const pool = setKey === 'all' ? sets.flatMap((s) => CATALOG.bySet.get(s.key) ?? []) : (CATALOG.bySet.get(setKey) ?? []);
    const list = pool.filter((c) => {
      if (group !== 'all' && c.group !== group) return false;
      if (!cardMatches(c, filters)) return false;
      const n = copies(totals, c, col);
      return show === 'all' || (show === 'missing' ? n === 0 : n > 0);
    });
    const withPrice = list.map((card) => ({ card, price: priceGrade === null ? null : unitPrice(save, card, priceGrade) }));
    if (sort === 'name') withPrice.sort((a, b) => a.card.name.localeCompare(b.card.name) || a.card.i - b.card.i);
    else if (sort === 'price-desc') withPrice.sort((a, b) => (b.price ?? -1) - (a.price ?? -1) || a.card.i - b.card.i);
    else if (sort === 'price-asc') withPrice.sort((a, b) => (a.price ?? Infinity) - (b.price ?? Infinity) || a.card.i - b.card.i);
    return withPrice;
  }, [save, totals, sets, setKey, group, col, show, sort, filters, priceGrade]);

  const listValue = rows.reduce((s, r) => s + (r.price ?? 0), 0);
  const pool = rows.length;

  const [copied, setCopied] = useState<'idle' | 'done' | 'failed'>('idle');

  const buildCsv = () => {
    const header = ['Set', 'Number', 'Card', 'Edition', 'Foil', 'Rarity', `Copies (${columnLabel(col)})`, 'Owned as', `Market price (${priceGrade === null ? 'n/a' : priceGrade ? 'Grade ' + priceGrade : 'ungraded'})`];
    const lines = [header, ...rows.map(({ card, price }) => [
      CATALOG.setByKey.get(card.set)?.name ?? card.set,
      String(card.num),
      card.name,
      card.set === 'ghost' ? card.group : card.editionName,
      card.foil ? 'Foil' : 'Non-foil',
      card.tier,
      String(copies(totals, card, col)),
      ownedAs(totals, card),
      price === null ? '' : price.toFixed(2),
    ])];
    return lines.map((l) => l.map((v) => (/[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)).join(',')).join('\n');
  };

  const exportCsv = () => {
    const url = URL.createObjectURL(new Blob([buildCsv()], { type: 'text/csv' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `tcg-${show}-${setKey}-${String(col)}.csv`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  const colOptions: Column[] = [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
    ...(save.moddedGradeCount > 0 || col === COL_OTHER ? [COL_OTHER] : []),
    'graded', 'any',
  ];

  return (
    <div class="stack">
      <div class="card pad stack">
        <div class="row">
          <label class="field">
            Set
            <select
              value={setKey}
              onChange={(e) => {
                setSetKey(e.currentTarget.value as SetKey | 'all');
                setGroup('all');
                setLimit(PAGE);
              }}
            >
              <option value="all">All sets</option>
              {sets.map((s) => (
                <option value={s.key}>{s.name}</option>
              ))}
            </select>
          </label>
          <label class="field">
            {setKey === 'ghost' ? 'Variant' : 'Edition'}
            <select value={group} disabled={setKey === 'all'} onChange={(e) => setGroup(e.currentTarget.value)}>
              <option value="all">All</option>
              {groups.map((g) => (
                <option value={g}>{g}</option>
              ))}
            </select>
          </label>
          <label class="field">
            Condition
            <select
              value={String(col)}
              onChange={(e) => {
                const v = e.currentTarget.value;
                setCol(v === 'graded' || v === 'any' ? v : Number(v));
              }}
            >
              {colOptions.map((c) => (
                <option value={String(c)}>{columnLabel(c)}</option>
              ))}
            </select>
          </label>
          <label class="field">
            Show
            <select value={show} onChange={(e) => setShow(e.currentTarget.value as Show)}>
              <option value="missing">Missing cards</option>
              <option value="owned">Owned cards</option>
              <option value="all">All cards</option>
            </select>
          </label>
          <label class="field">
            Sort
            <select value={sort} onChange={(e) => setSort(e.currentTarget.value as Sort)}>
              <option value="number">Card number</option>
              <option value="name">Name</option>
              <option value="price-desc">Price, high to low</option>
              <option value="price-asc">Price, low to high</option>
            </select>
          </label>
        </div>
        <div class="row">
          <TierChips value={filters.tiers} onChange={(tiers) => setFilters({ ...filters, tiers })} />
          <FoilSeg value={filters.foil} onChange={(foil) => setFilters({ ...filters, foil })} />
          <input
            type="search"
            placeholder="Search card name or #"
            aria-label="Search cards"
            value={filters.search}
            onInput={(e) => setFilters({ ...filters, search: e.currentTarget.value })}
            style={{ border: '1px solid var(--ring)', borderRadius: '8px', padding: '6px 10px', background: 'var(--surface)', minWidth: '0', flex: '1 1 160px', maxWidth: '260px' }}
          />
        </div>
      </div>

      <div class="card">
        <div class="row pad" style={{ paddingBottom: '8px' }}>
          <div>
            <strong class="num">{int(pool)}</strong>{' '}
            {show === 'missing' ? 'missing' : show === 'owned' ? 'owned' : ''} {pool === 1 ? 'card' : 'cards'}
            <span class="muted"> · {columnLabel(col)}</span>
            {show === 'missing' && pool > 0 && priceGrade !== null && (
              <span class="muted small">
                {' '}
                · market value of one of each: {formatMoney(listValue)}
              </span>
            )}
          </div>
          <span class="spacer" />
          <button
            type="button"
            class="btn"
            disabled={pool === 0}
            onClick={async () => {
              try {
                await navigator.clipboard.writeText(buildCsv());
                setCopied('done');
              } catch {
                setCopied('failed');
              }
              setTimeout(() => setCopied('idle'), 2000);
            }}
          >
            {copied === 'done' ? 'Copied' : copied === 'failed' ? 'Copy blocked' : 'Copy CSV'}
          </button>
          <button type="button" class="btn" onClick={exportCsv} disabled={pool === 0}>
            Download CSV
          </button>
        </div>
        {pool === 0 ? (
          <p class="pad muted" style={{ marginTop: 0 }}>
            {show === 'missing' ? 'Nothing missing here. Complete!' : 'No cards match.'}
          </p>
        ) : (
          <div class="scroll">
            <table class="list">
              <thead>
                <tr>
                  <th class="num">#</th>
                  <th>Card</th>
                  {setKey === 'all' && <th>Set</th>}
                  <th>Rarity</th>
                  <th>Owned as</th>
                  <th class="num">Price{priceGrade ? ` (G${priceGrade})` : ''}</th>
                </tr>
              </thead>
              <tbody>
                {rows.slice(0, limit).map(({ card, price }) => (
                  <tr key={card.i}>
                    <td class="num muted">{String(card.num).padStart(3, '0')}</td>
                    <td>
                      {card.name}{' '}
                      <span class="pill">{card.set === 'ghost' ? card.group : card.editionName}</span>{' '}
                      {card.foil && <span class="pill foil">Foil</span>}
                    </td>
                    {setKey === 'all' && <td>{CATALOG.setByKey.get(card.set)?.name}</td>}
                    <td class="muted">{card.tier}</td>
                    <td class="small">{ownedAs(totals, card) || <span class="muted">—</span>}</td>
                    <td class="num">{formatMoney(price)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rows.length > limit && (
              <div class="pad" style={{ textAlign: 'center' }}>
                <button type="button" class="btn" onClick={() => setLimit(rows.length)}>
                  Show all {int(rows.length)}
                </button>
              </div>
            )}
          </div>
        )}
      </div>
      <p class="small muted">
        Prices are today's market prices from your save, in the game's base currency.
      </p>
    </div>
  );
}
