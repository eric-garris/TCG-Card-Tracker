import { useMemo, useState } from 'preact/hooks';
import { CATALOG, cardLabel, type SetDef, type SetKey } from '../core/catalog';
import { aggregate, columnLabel, percent, type Column } from '../core/collection';
import { COL_OTHER, type LocationKey } from '../core/save';
import {
  diffSnapshots,
  snapshotLabel,
  snapshotTotals,
  snapshotValue,
  sortSnapshots,
  type Snapshot,
} from '../core/snapshot';
import { formatMoney } from '../core/value';
import { int, relativeTime, saveSlotLabel, signed } from './format';
import { LineChart, type Series } from './LineChart';

/** Fixed color per set, so a set keeps its color whatever is filtered. */
const SET_COLORS: Record<SetKey, string> = {
  tetramon: 'var(--series-1)',
  destiny: 'var(--series-2)',
  ascension: 'var(--series-3)',
  ghost: 'var(--series-4)',
  megabot: 'var(--series-5)',
  fantasyrpg: 'var(--series-6)',
  catjob: 'var(--series-7)',
};

interface Props {
  snapshots: Snapshot[];
  sets: SetDef[];
  included: ReadonlySet<LocationKey>;
  currentPlayer: string | null;
  currentId: string | null;
  onDelete: (id: string) => void;
  onForgetAll: () => void;
}

const METRICS: Column[] = ['any', 0, 'graded', 10, 9, 8];
const DIFF_COLS: Column[] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, COL_OTHER, 'graded', 'any'];

export function Timeline({ snapshots, sets, included, currentPlayer, currentId, onDelete, onForgetAll }: Props) {
  const players = useMemo(() => [...new Set(snapshots.map((s) => s.playerName ?? ''))], [snapshots]);
  const [chosen, setPlayer] = useState<string | null>(null);
  // The shop being charted: the user's pick if it still has saves, else the shop on screen, else any.
  const current = currentPlayer ?? '';
  const player =
    chosen !== null && players.includes(chosen) ? chosen : players.includes(current) ? current : (players[0] ?? '');
  const [metric, setMetric] = useState<Column>('any');
  const list = useMemo(
    () => sortSnapshots(snapshots.filter((s) => (s.playerName ?? '') === player)),
    [snapshots, player],
  );
  const [fromId, setFromId] = useState<string | null>(null);
  const [toId, setToId] = useState<string | null>(null);
  const [cardLimit, setCardLimit] = useState(100);
  const now = Date.now();

  const totalsList = useMemo(() => list.map((s) => snapshotTotals(s, included)), [list, included]);
  const xLabels = list.map((s) => (s.day !== null ? `Day ${s.day}` : saveSlotLabel(s.fileName)));

  const pctSeries: Series[] = useMemo(
    () =>
      sets.map((set) => {
        const cards = CATALOG.bySet.get(set.key) ?? [];
        return {
          key: set.key,
          label: set.name,
          color: SET_COLORS[set.key],
          values: totalsList.map((t) => {
            const a = aggregate(cards, t, metric);
            const r = Math.round(percent(a.owned, a.total) * 10) / 10;
            return a.owned < a.total ? Math.min(99.9, r) : r;
          }),
        };
      }),
    [sets, totalsList, metric],
  );
  const valueSeries: Series[] = useMemo(
    () => [{ key: 'value', label: 'Collection value', color: 'var(--series-1)', values: list.map((s) => snapshotValue(s, included)) }],
    [list, included],
  );

  const to = list.find((s) => s.id === toId) ?? list[list.length - 1] ?? null;
  const from = list.find((s) => s.id === fromId) ?? list[list.length - 2] ?? null;
  const diff = useMemo(
    () => (from && to && from.id !== to.id ? diffSnapshots(from, to, included, DIFF_COLS, sets.map((s) => s.key)) : null),
    [from, to, included, sets],
  );

  // The modded-grade column only appears when either save has such cards.
  const diffCols = diff
    ? DIFF_COLS.filter((c) => c !== COL_OTHER || diff.sets.some((x) => x.col === COL_OTHER && (x.ownedFrom > 0 || x.ownedTo > 0)))
    : DIFF_COLS;

  if (snapshots.length === 0) {
    return <p class="muted">Load a save to start your timeline.</p>;
  }

  return (
    <div class="stack">
      <div class="card pad small">
        Every save you load is remembered <strong>in this browser only</strong> as a small summary (no save file is
        stored), so you can watch your collection grow. Load a save after each play session, or drop several at once,
        including the <code>savedGames_ReleaseBackupFile</code> copies, which hold the previous save.
      </div>

      <div class="row">
        {players.length > 1 && (
          <label class="field">
            Shop
            <select value={player} onChange={(e) => setPlayer(e.currentTarget.value)}>
              {players.map((p) => (
                <option value={p}>{p || 'Unnamed shop'}</option>
              ))}
            </select>
          </label>
        )}
        <label class="field">
          Completion measure
          <select
            value={String(metric)}
            onChange={(e) => {
              const v = e.currentTarget.value;
              setMetric(v === 'graded' || v === 'any' ? v : Number(v));
            }}
          >
            {METRICS.map((c) => (
              <option value={String(c)}>{columnLabel(c)}</option>
            ))}
          </select>
        </label>
      </div>

      {list.length < 2 ? (
        <div class="notice">
          {list.length === 0
            ? 'No saves stored for this shop yet.'
            : 'Only one save loaded for this shop so far. Load another (newer or older) save to see change over time.'}
        </div>
      ) : (
        <>
          <div class="card pad">
            <LineChart
              title={`Cards collected (${columnLabel(metric).toLowerCase()}), % of each set`}
              xLabels={xLabels}
              series={pctSeries}
              yFormat={(v) => `${Number.isInteger(v) ? v : v.toFixed(1)}%`}
              yMax={100}
            />
          </div>
          <div class="card pad">
            <LineChart title="Collection value" xLabels={xLabels} series={valueSeries} yFormat={(v) => formatMoney(v, { compact: true })} />
          </div>
        </>
      )}

      {list.length >= 2 && from && to && (
        <div class="card">
          <div class="row pad">
            <strong>Compare saves</strong>
            <span class="spacer" />
            <label class="field">
              From
              <select value={from.id} onChange={(e) => setFromId(e.currentTarget.value)}>
                {list.map((s) => (
                  <option value={s.id}>{snapshotLabel(s)}</option>
                ))}
              </select>
            </label>
            <label class="field">
              To
              <select value={to.id} onChange={(e) => setToId(e.currentTarget.value)}>
                {list.map((s) => (
                  <option value={s.id}>{snapshotLabel(s)}</option>
                ))}
              </select>
            </label>
          </div>
          {!diff ? (
            <p class="pad muted" style={{ marginTop: 0 }}>
              Pick two different saves.
            </p>
          ) : (
            <>
              <div class="pad" style={{ paddingTop: 0 }}>
                Value {formatMoney(diff.valueFrom)} → <strong>{formatMoney(diff.valueTo)}</strong>{' '}
                <span class={diff.valueTo >= diff.valueFrom ? 'delta-up' : 'delta-down'}>
                  ({signed(Math.round((diff.valueTo - diff.valueFrom) * 100) / 100, (x) => formatMoney(x))})
                </span>
              </div>
              <div class="grid-wrap">
                <table class="list" style={{ minWidth: '760px' }}>
                  <thead>
                    <tr>
                      <th>Set</th>
                      {diffCols.map((c) => (
                        <th class="num">{c === 0 ? 'Ungraded' : columnLabel(c, true)}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {sets.map((set) => (
                      <tr>
                        <td>{set.name}</td>
                        {diffCols.map((c) => {
                          const ch = diff.sets.find((x) => x.set === set.key && x.col === c)!;
                          const d = ch.ownedTo - ch.ownedFrom;
                          return (
                            <td class="num" title={`${columnLabel(c)}: ${int(ch.ownedFrom)} → ${int(ch.ownedTo)} different cards; ${int(ch.copiesFrom)} → ${int(ch.copiesTo)} copies`}>
                              {int(ch.ownedTo)}
                              {d !== 0 && (
                                <div class={`small ${d > 0 ? 'delta-up' : 'delta-down'}`}>{signed(d)}</div>
                              )}
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div class="pad">
                <strong>Card changes</strong> <span class="muted small">({int(diff.cards.length)})</span>
                {diff.cards.length === 0 ? (
                  <p class="muted">No card changed between these saves.</p>
                ) : (
                  <div class="scroll" style={{ marginTop: '8px' }}>
                    <table class="list">
                      <thead>
                        <tr>
                          <th>Card</th>
                          <th>Set</th>
                          <th>Condition</th>
                          <th class="num">Copies</th>
                          <th>Change</th>
                        </tr>
                      </thead>
                      <tbody>
                        {diff.cards.slice(0, cardLimit).map((c) => (
                          <tr>
                            <td>{cardLabel(c.card)}</td>
                            <td>{CATALOG.setByKey.get(c.card.set)?.name}</td>
                            <td>{columnLabel(c.col)}</td>
                            <td class="num">
                              {int(c.from)} → {int(c.to)}
                            </td>
                            <td class={c.to > c.from ? 'delta-up' : 'delta-down'}>
                              {c.from === 0 ? 'New' : c.to === 0 ? 'Gone' : signed(c.to - c.from)}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {diff.cards.length > cardLimit && (
                      <div class="pad" style={{ textAlign: 'center' }}>
                        <button type="button" class="btn" onClick={() => setCardLimit(diff.cards.length)}>
                          Show all {int(diff.cards.length)}
                        </button>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      )}

      <div class="card">
        <div class="row pad" style={{ paddingBottom: '8px' }}>
          <strong>Saved snapshots</strong>
          <span class="muted small">({int(snapshots.length)})</span>
          <span class="spacer" />
          <button
            type="button"
            class="btn"
            onClick={() => {
              if (confirm('Forget every saved snapshot and the last loaded save on this device?')) onForgetAll();
            }}
          >
            Forget all
          </button>
        </div>
        <div class="grid-wrap">
          <table class="list">
            <thead>
              <tr>
                <th>In-game day</th>
                <th>File</th>
                <th>Shop</th>
                <th class="num">Value</th>
                <th>Loaded</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {sortSnapshots(snapshots)
                .reverse()
                .map((s) => (
                  <tr>
                    <td>
                      {s.day ?? '—'} {s.id === currentId && <span class="pill foil">showing</span>}
                    </td>
                    <td title={s.fileName}>{saveSlotLabel(s.fileName)}</td>
                    <td>{s.playerName ?? '—'}</td>
                    <td class="num">{formatMoney(snapshotValue(s, included))}</td>
                    <td class="muted">{relativeTime(s.addedAt, now)}</td>
                    <td>
                      <button type="button" class="btn ghost small" onClick={() => onDelete(s.id)} aria-label={`Delete snapshot ${snapshotLabel(s)}`}>
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
