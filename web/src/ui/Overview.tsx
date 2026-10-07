import { useMemo, useState } from 'preact/hooks';
import { CATALOG, type SetDef } from '../core/catalog';
import {
  aggregate,
  buildTree,
  columnLabel,
  formatPercent,
  GRADE_COLUMNS,
  percent,
  valueOf,
  type Column,
  type Filters,
  type TreeRow,
} from '../core/collection';
import { COL_OTHER, type ParsedSave } from '../core/save';
import { formatMoney } from '../core/value';
import { int } from './format';

export interface CellTarget {
  set: TreeRow['set'];
  group?: string;
  foil?: boolean;
  col: Column;
}

interface Props {
  save: ParsedSave;
  totals: Int32Array;
  sets: SetDef[];
  filters: Filters;
  showCopies: boolean;
  onCell: (t: CellTarget) => void;
}

function heatClass(owned: number, total: number): string {
  if (total === 0 || owned === 0) return 'empty';
  if (owned === total) return 'h5 full';
  const p = percent(owned, total);
  if (p < 20) return 'h1';
  if (p < 40) return 'h2';
  if (p < 60) return 'h3';
  if (p < 80) return 'h4';
  return 'h5';
}

export function Tiles({ save, totals, sets }: { save: ParsedSave; totals: Int32Array; sets: SetDef[] }) {
  const cards = useMemo(() => sets.flatMap((s) => CATALOG.bySet.get(s.key) ?? []), [sets]);
  const any = aggregate(cards, totals, 'any');
  const ungraded = aggregate(cards, totals, 0);
  const graded = aggregate(cards, totals, 'graded');
  const gem = aggregate(cards, totals, 10);
  const value = useMemo(() => valueOf(save, totals, cards), [save, totals, cards]);
  return (
    <div class="tiles">
      <div class="card tile">
        <div class="label">Cards collected (any form)</div>
        <div class="value">{formatPercent(any.owned, any.total)}</div>
        <div class="detail num">
          {int(any.owned)} of {int(any.total)} cards
        </div>
      </div>
      <div class="card tile">
        <div class="label">Ungraded</div>
        <div class="value">{formatPercent(ungraded.owned, ungraded.total)}</div>
        <div class="detail">
          {int(ungraded.owned)} cards · {int(ungraded.copies)} copies
        </div>
      </div>
      <div class="card tile">
        <div class="label">Graded</div>
        <div class="value">{int(graded.copies)}</div>
        <div class="detail">
          copies of {int(graded.owned)} different cards
        </div>
      </div>
      <div class="card tile">
        <div class="label">Gem Mint (grade 10)</div>
        <div class="value">{int(gem.copies)}</div>
        <div class="detail">{int(gem.owned)} different cards</div>
      </div>
      <div class="card tile">
        <div class="label">Collection value</div>
        <div class="value">{formatMoney(value.ungraded + value.graded, { compact: true })}</div>
        <div class="detail">
          {formatMoney(value.ungraded, { compact: true })} ungraded · {formatMoney(value.graded, { compact: true })} graded
        </div>
      </div>
    </div>
  );
}

export function ProgressGrid({ save, totals, sets, filters, showCopies, onCell }: Props) {
  const [open, setOpen] = useState<Set<string>>(new Set());
  const tree = useMemo(() => buildTree(sets, filters), [sets, filters]);
  const hasOther = save.moddedGradeCount > 0;
  const cols: Column[] = [...GRADE_COLUMNS, ...(hasOther ? [COL_OTHER] : []), 'graded', 'any'];

  const toggle = (id: string) => {
    const next = new Set(open);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setOpen(next);
  };

  const rows: TreeRow[] = [];
  const walk = (r: TreeRow) => {
    rows.push(r);
    if (open.has(r.id)) r.children.forEach(walk);
  };
  tree.forEach(walk);

  return (
    <div class="card">
      <div class="grid-wrap">
        <table class="grid">
          <thead>
            <tr>
              <th class="setcol" scope="col">
                Set
              </th>
              {cols.map((c) => (
                <th scope="col" title={typeof c === 'number' && c >= 1 && c <= 10 ? `Grade ${c}` : undefined}>
                  {c === 0 ? 'Ungraded' : columnLabel(c, true)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const expandable = r.children.length > 0;
              const expanded = open.has(r.id);
              return (
                <tr class={`depth-${r.depth}`} key={r.id}>
                  <th class="setcol" scope="row">
                    <button
                      type="button"
                      class="setname"
                      disabled={!expandable}
                      aria-expanded={expandable ? expanded : undefined}
                      onClick={() => expandable && toggle(r.id)}
                    >
                      <svg class="chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true" style={{ visibility: expandable ? 'visible' : 'hidden' }}>
                        <path d="M9 6l6 6-6 6" stroke-linecap="round" stroke-linejoin="round" />
                      </svg>
                      <span>
                        <span class="title">{r.label}</span>
                        {r.depth === 0 && <span class="meta"> · {int(r.cards.length)} cards</span>}
                      </span>
                    </button>
                  </th>
                  {cols.map((c) => {
                    const a = aggregate(r.cards, totals, c);
                    const where = [r.label, r.group && r.depth === 2 ? r.group : null].filter(Boolean).join(' ');
                    const label = `${setName(sets, r.set)}${r.depth > 0 ? ' · ' + where : ''} · ${columnLabel(c)}: ${int(a.owned)} of ${int(a.total)} cards (${formatPercent(a.owned, a.total)}), ${int(a.copies)} ${a.copies === 1 ? 'copy' : 'copies'}. Click to list missing cards.`;
                    return (
                      <td class="cell">
                        <button
                          type="button"
                          class={`cellbtn ${heatClass(a.owned, a.total)}`}
                          title={label}
                          aria-label={label}
                          onClick={() => onCell({ set: r.set, group: r.group, foil: r.foil, col: c })}
                        >
                          <div class="frac">
                            {int(a.owned)}
                            <span class="copies">/{int(a.total)}</span>
                          </div>
                          {showCopies && <div class="copies">{a.copies ? `${int(a.copies)}×` : ' '}</div>}
                        </button>
                      </td>
                    );
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div class="row pad" style={{ justifyContent: 'space-between', paddingTop: '10px', paddingBottom: '10px' }}>
        <div class="legend" aria-hidden="true">
          <span>Completion</span>
          <span class="sw" style={{ background: 'var(--surface)' }} /> 0%
          <span class="sw" style={{ background: 'var(--heat-1)' }} />
          <span class="sw" style={{ background: 'var(--heat-2)' }} />
          <span class="sw" style={{ background: 'var(--heat-3)' }} />
          <span class="sw" style={{ background: 'var(--heat-4)' }} />
          <span class="sw" style={{ background: 'var(--heat-5)' }} /> 100% ✓
        </div>
        <div class="small muted">
          Each cell: different cards owned / cards in the set{showCopies ? ', and total copies' : ''}. Click a cell for the missing cards.
        </div>
      </div>
    </div>
  );
}

function setName(sets: SetDef[], key: SetDef['key']): string {
  return sets.find((s) => s.key === key)?.name ?? key;
}
