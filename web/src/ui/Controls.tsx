import { useEffect, useRef, useState } from 'preact/hooks';
import { TIERS, type Tier } from '../core/catalog';
import type { Filters } from '../core/collection';
import { LOCATIONS, type LocationKey } from '../core/save';

export function TierChips({ value, onChange }: { value: ReadonlySet<Tier>; onChange: (v: Set<Tier>) => void }) {
  return (
    <div class="seg" role="group" aria-label="Rarity">
      {TIERS.map((t) => (
        <button
          type="button"
          aria-pressed={value.has(t)}
          onClick={() => {
            const next = new Set(value);
            if (next.has(t)) next.delete(t);
            else next.add(t);
            // Never filter everything out: an empty selection means "all".
            onChange(next.size === 0 ? new Set(TIERS) : next);
          }}
        >
          {t}
        </button>
      ))}
    </div>
  );
}

export function FoilSeg({ value, onChange }: { value: boolean | null; onChange: (v: boolean | null) => void }) {
  const opts: [boolean | null, string][] = [
    [null, 'All'],
    [false, 'Non-foil'],
    [true, 'Foil'],
  ];
  return (
    <div class="seg" role="group" aria-label="Foil">
      {opts.map(([v, label]) => (
        <button type="button" aria-pressed={value === v} onClick={() => onChange(v)}>
          {label}
        </button>
      ))}
    </div>
  );
}

export function FilterRow({
  filters,
  onChange,
  children,
}: {
  filters: Filters;
  onChange: (f: Filters) => void;
  children?: preact.ComponentChildren;
}) {
  return (
    <div class="row">
      <TierChips value={filters.tiers} onChange={(tiers) => onChange({ ...filters, tiers })} />
      <FoilSeg value={filters.foil} onChange={(foil) => onChange({ ...filters, foil })} />
      <input
        type="search"
        placeholder="Search card name or #"
        aria-label="Search cards"
        value={filters.search}
        onInput={(e) => onChange({ ...filters, search: e.currentTarget.value })}
        style={{ border: '1px solid var(--ring)', borderRadius: '8px', padding: '6px 10px', background: 'var(--surface)', minWidth: '0', flex: '1 1 160px', maxWidth: '260px' }}
      />
      {children}
    </div>
  );
}

/** "Count cards in" popover: which save locations contribute to every number on the page. */
export function LocationsMenu({
  value,
  onChange,
}: {
  value: readonly LocationKey[];
  onChange: (v: LocationKey[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setOpen(false);
    };
    const esc = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      setOpen(false);
      button.current?.focus();
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', esc);
    };
  }, [open]);
  const set = new Set(value);
  const all = value.length === LOCATIONS.length;
  return (
    <div
      ref={root}
      style={{ position: 'relative', marginLeft: 'auto' }}
      onFocusOut={(e) => {
        // Close when keyboard focus leaves the menu, so it never covers what is focused next.
        if (open && !root.current?.contains(e.relatedTarget as Node | null)) setOpen(false);
      }}
    >
      <button ref={button} type="button" class="btn" aria-expanded={open} onClick={() => setOpen(!open)}>
        Counting: {all ? 'everywhere' : value.length === 1 && set.has('binder') ? 'binders only' : `${value.length} of ${LOCATIONS.length} places`}
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true">
          <path d="M6 9l6 6 6-6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </button>
      {open && (
        <div class="card pad" style={{ position: 'absolute', right: 0, top: 'calc(100% + 6px)', zIndex: 20, width: 'min(340px, calc(100vw - 48px))' }}>
          <div class="small muted" style={{ marginBottom: '8px' }}>
            Count cards wherever they are in your shop. Cards leave the binder when you display, store or deck them.
            At least one place is always counted.
          </div>
          {LOCATIONS.map((l) => (
            <label class="check" style={{ display: 'flex', padding: '4px 0' }} title={l.hint}>
              <input
                type="checkbox"
                checked={set.has(l.key)}
                // At least one place is always counted, otherwise every number reads zero.
                disabled={set.size === 1 && set.has(l.key)}
                onChange={(e) => {
                  const next = new Set(set);
                  if (e.currentTarget.checked) next.add(l.key);
                  else next.delete(l.key);
                  onChange(LOCATIONS.map((x) => x.key).filter((k) => next.has(k)));
                }}
              />
              <span>
                {l.label}
                <span class="muted small"> · {l.hint}</span>
              </span>
            </label>
          ))}
          <div class="row" style={{ marginTop: '8px' }}>
            <button type="button" class="btn small" onClick={() => onChange(LOCATIONS.map((l) => l.key))}>
              Everywhere
            </button>
            <button type="button" class="btn small" onClick={() => onChange(['binder'])}>
              Binders only
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
