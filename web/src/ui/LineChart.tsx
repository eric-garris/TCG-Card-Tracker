import { useEffect, useRef, useState } from 'preact/hooks';

export interface Series {
  key: string;
  label: string;
  /** CSS color (use a --series-N token). */
  color: string;
  values: (number | null)[];
}

interface Props {
  title: string;
  xLabels: string[];
  series: Series[];
  yFormat: (v: number) => string;
  /** Fixed y maximum (e.g. 100 for percentages); otherwise derived from the data. */
  yMax?: number;
  height?: number;
}

function niceMax(v: number): number {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (m * p >= v) return m * p;
  return 10 * p;
}

/**
 * Multi-series line chart: 2px lines, ringed end markers, hairline grid, crosshair tooltip
 * (pointer and arrow keys), legend for 2+ series, direct end labels when they don't collide,
 * and a data table twin.
 */
export function LineChart({ title, xLabels, series, yFormat, yMax, height = 260 }: Props) {
  const wrap = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    const el = wrap.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width;
      if (w) setWidth(Math.max(280, Math.round(w)));
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const n = xLabels.length;
  const showDirect = series.length <= 4;
  const m = { top: 14, right: showDirect ? 112 : 16, bottom: 40, left: 56 };
  const w = width - m.left - m.right;
  const h = height - m.top - m.bottom;
  const dataMax = Math.max(0, ...series.flatMap((s) => s.values.filter((v): v is number => v !== null)));
  const top = yMax ?? niceMax(dataMax * 1.05);
  const x = (i: number) => m.left + (n <= 1 ? w / 2 : (i / (n - 1)) * w);
  const y = (v: number) => m.top + h - (Math.min(v, top) / top) * h;
  const ticks = Array.from({ length: 5 }, (_, i) => (top / 4) * i);
  const labelEvery = Math.max(1, Math.ceil(n / Math.max(1, Math.floor(w / 90))));

  // Direct labels only when the series' last points are far enough apart.
  const ends = series
    .map((s) => {
      for (let i = s.values.length - 1; i >= 0; i--) {
        const v = s.values[i];
        if (v !== null && v !== undefined) return { s, i, v, py: y(v) };
      }
      return null;
    })
    .filter((e): e is NonNullable<typeof e> => e !== null)
    .sort((a, b) => a.py - b.py);
  const labelsFit = showDirect && ends.every((e, k) => k === 0 || e.py - ends[k - 1]!.py >= 15);

  const pick = (clientX: number) => {
    const svg = wrap.current?.querySelector('svg');
    if (!svg || n === 0) return;
    const r = svg.getBoundingClientRect();
    const px = ((clientX - r.left) / r.width) * width;
    const i = n <= 1 ? 0 : Math.round(((px - m.left) / w) * (n - 1));
    setHover(Math.max(0, Math.min(n - 1, i)));
  };

  const tipLeft = hover === null ? 0 : x(hover) / width;

  return (
    <figure class="chart" style={{ margin: 0 }}>
      <figcaption class="row" style={{ marginBottom: '8px' }}>
        <strong>{title}</strong>
        <span class="spacer" />
        {series.length >= 2 && (
          <span class="legend" aria-hidden="true">
            {series.map((s) => (
              <span class="legend-item">
                <svg width="16" height="8" aria-hidden="true">
                  <line x1="0" y1="4" x2="16" y2="4" stroke={s.color} stroke-width="2" stroke-linecap="round" />
                </svg>
                {s.label}
              </span>
            ))}
          </span>
        )}
      </figcaption>
      <div ref={wrap} style={{ position: 'relative' }}>
        <svg
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-label={`${title}. Use the data table below for exact values.`}
          tabIndex={0}
          onPointerMove={(e) => pick(e.clientX)}
          onPointerLeave={() => setHover(null)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowRight') setHover((v) => Math.min(n - 1, (v ?? -1) + 1));
            else if (e.key === 'ArrowLeft') setHover((v) => Math.max(0, (v ?? n) - 1));
            else if (e.key === 'Escape') setHover(null);
          }}
          onBlur={() => setHover(null)}
        >
          {ticks.map((t) => (
            <g>
              <line x1={m.left} x2={m.left + w} y1={y(t)} y2={y(t)} stroke={t === 0 ? 'var(--axis)' : 'var(--hairline)'} stroke-width="1" />
              <text x={m.left - 8} y={y(t)} dy="0.32em" text-anchor="end" fill="var(--muted)" font-size="11" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {yFormat(t)}
              </text>
            </g>
          ))}
          {xLabels.map((l, i) =>
            i % labelEvery === 0 || i === n - 1 ? (
              <text x={x(i)} y={m.top + h + 18} text-anchor="middle" fill="var(--muted)" font-size="11">
                {l}
              </text>
            ) : null,
          )}
          {hover !== null && <line x1={x(hover)} x2={x(hover)} y1={m.top} y2={m.top + h} stroke="var(--axis)" stroke-width="1" />}
          {series.map((s) => {
            const pts = s.values.map((v, i) => (v === null ? null : ([x(i), y(v)] as const)));
            let d = '';
            let pen = false;
            for (const p of pts) {
              if (!p) {
                pen = false;
                continue;
              }
              d += `${pen ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`;
              pen = true;
            }
            return (
              <g>
                <path d={d} fill="none" stroke={s.color} stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
                {pts.map((p, i) =>
                  p && (n <= 12 || i === n - 1 || i === hover) ? (
                    <circle cx={p[0]} cy={p[1]} r="4" fill={s.color} stroke="var(--surface)" stroke-width="2" />
                  ) : null,
                )}
              </g>
            );
          })}
          {labelsFit &&
            ends.map((e) => (
              <text x={x(e.i) + 10} y={e.py} dy="0.32em" fill="var(--ink-2)" font-size="12">
                {series.length > 1 ? `${e.s.label} ${yFormat(e.v)}` : yFormat(e.v)}
              </text>
            ))}
        </svg>
        {hover !== null && (
          <div
            class="tip"
            role="status"
            style={{
              top: '8px',
              left: tipLeft > 0.6 ? undefined : `calc(${(tipLeft * 100).toFixed(2)}% + 12px)`,
              right: tipLeft > 0.6 ? `calc(${((1 - tipLeft) * 100).toFixed(2)}% + 12px)` : undefined,
            }}
          >
            <div class="muted" style={{ marginBottom: '4px' }}>
              {xLabels[hover]}
            </div>
            {series.map((s) => (
              <div class="trow">
                <span class="key" style={{ background: s.color }} />
                <strong>{s.values[hover] === null || s.values[hover] === undefined ? '—' : yFormat(s.values[hover]!)}</strong>
                <span class="muted">{s.label}</span>
              </div>
            ))}
          </div>
        )}
      </div>
      <details style={{ marginTop: '8px' }}>
        <summary class="small muted" style={{ cursor: 'pointer' }}>
          Data table
        </summary>
        <div style={{ overflowX: 'auto' }}>
          <table class="list">
            <thead>
              <tr>
                <th>Save</th>
                {series.map((s) => (
                  <th class="num">{s.label}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {xLabels.map((l, i) => (
                <tr>
                  <td>{l}</td>
                  {series.map((s) => (
                    <td class="num">{s.values[i] === null || s.values[i] === undefined ? '—' : yFormat(s.values[i]!)}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
