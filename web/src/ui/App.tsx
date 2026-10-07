import { useCallback, useEffect, useMemo, useState } from 'preact/hooks';
import { CATALOG_VERSION } from '../core/catalog';
import { combineLocations, DEFAULT_FILTERS, visibleSets, type Filters } from '../core/collection';
import { parseSaveText, SaveFormatError } from '../core/save';
import { exampleSave, exampleSnapshots } from '../core/sample';
import { makeSnapshot, type Snapshot } from '../core/snapshot';
import {
  deleteSnapshot,
  forgetEverything,
  listSnapshots,
  loadLastSave,
  putSnapshot,
  saveLastSave,
  type LastSave,
} from '../core/storage';
import { FilterRow, LocationsMenu } from './Controls';
import { relativeTime, saveSlotLabel } from './format';
import { Missing } from './Missing';
import { ProgressGrid, Tiles, type CellTarget } from './Overview';
import { loadPrefs, savePrefs, type Prefs } from './prefs';
import { applyThemePref, loadThemePref, type ThemePref } from './theme';
import { Timeline } from './Timeline';
import { DropZone, FilePicker } from './Upload';
import { ValueView } from './ValueView';

type Tab = 'overview' | 'missing' | 'value' | 'timeline';

const TABS: { key: Tab; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'missing', label: 'Missing cards' },
  { key: 'value', label: 'Value' },
  { key: 'timeline', label: 'Timeline' },
];

export function App() {
  const [theme, setTheme] = useState<ThemePref>(loadThemePref);
  const [prefs, setPrefsState] = useState<Prefs>(loadPrefs);
  const [last, setLast] = useState<LastSave | null>(null);
  const [restoring, setRestoring] = useState(true);
  const [snapshots, setSnapshots] = useState<Snapshot[]>([]);
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [tab, setTab] = useState<Tab>('overview');
  const [filters, setFilters] = useState<Filters>(DEFAULT_FILTERS);
  const [preset, setPreset] = useState<CellTarget | null>(null);
  const [openRows, setOpenRows] = useState<Set<string>>(() => new Set());
  // Tabs stay mounted once visited, so drill-downs and list settings survive switching tabs.
  const [visited, setVisited] = useState<Set<Tab>>(() => new Set<Tab>(['overview']));
  const [dragOver, setDragOver] = useState(false);
  const [storageWarning, setStorageWarning] = useState(false);

  const showTab = (t: Tab) => {
    setTab(t);
    setVisited((v) => (v.has(t) ? v : new Set([...v, t])));
  };

  useEffect(() => applyThemePref(theme), [theme]);

  useEffect(() => {
    let alive = true;
    void Promise.all([loadLastSave(), listSnapshots()]).then(([l, s]) => {
      if (!alive) return;
      setLast(l);
      setSnapshots(s);
      setRestoring(false);
    });
    return () => {
      alive = false;
    };
  }, []);

  const setPrefs = (p: Prefs) => {
    setPrefsState(p);
    savePrefs(p);
  };

  const handleFiles = useCallback(async (files: File[]) => {
    setBusy(true);
    const errs: string[] = [];
    const loaded: { last: LastSave; snap: Snapshot }[] = [];
    let stored = true;
    for (const file of files) {
      try {
        const save = parseSaveText(await file.text());
        const snap = makeSnapshot(save, { name: file.name, lastModified: file.lastModified }, Date.now());
        if (!(await putSnapshot(snap))) stored = false;
        loaded.push({
          snap,
          last: { save, fileName: file.name, fileModified: file.lastModified, snapshotId: snap.id, catalogVersion: CATALOG_VERSION },
        });
      } catch (e) {
        errs.push(`${file.name}: ${e instanceof SaveFormatError ? e.message : 'could not be read.'}`);
        if (!(e instanceof SaveFormatError)) console.error(e);
      }
    }
    if (loaded.length) {
      // Show the most recently written save; the rest go to the timeline.
      loaded.sort((a, b) => b.last.fileModified - a.last.fileModified);
      const newest = loaded[0]!.last;
      setLast(newest);
      void saveLastSave(newest);
      setSnapshots(await listSnapshots());
      setStorageWarning(!stored);
      if (loaded.length > 1 && tab === 'overview') showTab('timeline');
    }
    setErrors(errs);
    setBusy(false);
  }, [tab]);

  // Accept a drop anywhere on the page once a save is showing.
  useEffect(() => {
    if (!last) return;
    const over = (e: DragEvent) => {
      if (e.dataTransfer?.types.includes('Files')) {
        e.preventDefault();
        setDragOver(true);
      }
    };
    const leave = (e: DragEvent) => {
      if (!e.relatedTarget) setDragOver(false);
    };
    const drop = (e: DragEvent) => {
      if (!e.dataTransfer?.files.length) return;
      e.preventDefault();
      setDragOver(false);
      void handleFiles(Array.from(e.dataTransfer.files));
    };
    window.addEventListener('dragover', over);
    window.addEventListener('dragleave', leave);
    window.addEventListener('drop', drop);
    return () => {
      window.removeEventListener('dragover', over);
      window.removeEventListener('dragleave', leave);
      window.removeEventListener('drop', drop);
    };
  }, [last, handleFiles]);

  const included = useMemo(() => new Set(prefs.locations), [prefs.locations]);
  // Until a save is loaded, show a clearly labelled example collection instead of an empty page.
  const example = useMemo(
    () => (!restoring && !last ? { save: exampleSave(), snapshots: exampleSnapshots() } : null),
    [restoring, last],
  );
  const save = last?.save ?? example?.save ?? null;
  const shownSnapshots = example ? example.snapshots : snapshots;
  const totals = useMemo(() => (save ? combineLocations(save.counts, included) : null), [save, included]);
  const sets = useMemo(() => visibleSets(save), [save]);

  const themeCycle: ThemePref[] = ['system', 'light', 'dark'];

  return (
    <div class="shell">
      <header class="topbar">
        <div class="brand">
          <svg width="28" height="28" viewBox="0 0 32 32" aria-hidden="true">
            <rect x="7" y="3" width="18" height="26" rx="3" fill="var(--accent)" />
            <rect x="10" y="6" width="12" height="9" rx="1.5" fill="var(--accent-wash)" />
            <rect x="10" y="18" width="12" height="2" rx="1" fill="var(--accent-wash)" />
            <rect x="10" y="22" width="8" height="2" rx="1" fill="var(--accent-wash)" />
          </svg>
          <div>
            <h1>TCG Card Tracker</h1>
            <div class="sub">Set progress for TCG Card Shop Simulator, ungraded and graded 1–10</div>
          </div>
        </div>
        {last && <FilePicker onFiles={handleFiles} label="Load save" />}
        <button
          type="button"
          class="btn ghost"
          title="Theme"
          aria-label={`Theme: ${theme}. Click to change.`}
          onClick={() => setTheme(themeCycle[(themeCycle.indexOf(theme) + 1) % themeCycle.length]!)}
        >
          <ThemeIcon pref={theme} />
          {theme === 'dark' ? 'Dark' : theme === 'light' ? 'Light' : 'Auto'}
        </button>
      </header>

      {dragOver && (
        <div class="notice" style={{ marginBottom: '12px', textAlign: 'center' }}>
          Drop to load the save
        </div>
      )}

      {storageWarning && (
        <div class="notice warn" style={{ marginBottom: '16px' }}>
          This browser would not store your timeline (storage is full or blocked), so it will reset when you close the
          page.
        </div>
      )}

      {errors.length > 0 && (
        <div class="notice error stack" style={{ marginBottom: '16px' }} role="alert">
          {errors.map((e) => (
            <div>{e}</div>
          ))}
        </div>
      )}

      {restoring || !save || !totals ? null : (
        <div class="stack">
          {example && (
            <>
              <DropZone onFiles={handleFiles} busy={busy} />
              <div class="notice example row">
                <span class="pill">Example</span>
                <span>
                  Below is a made-up example collection so you can see how the tracker works. Load your save above to
                  replace it with your own cards.
                </span>
                <span class="spacer" />
                <LocationsMenu value={prefs.locations} onChange={(locations) => setPrefs({ ...prefs, locations })} />
              </div>
            </>
          )}
          {last && (
          <div class="card pad row" style={{ gap: '16px' }}>
            <div>
              <div style={{ fontWeight: 600 }}>{save.meta.playerName ?? 'Your shop'}</div>
              <div class="small muted">
                {[
                  save.meta.day !== null ? `Day ${save.meta.day}` : null,
                  save.meta.shopLevel !== null ? `Shop level ${save.meta.shopLevel + 1}` : null,
                  saveSlotLabel(last!.fileName),
                  `saved ${relativeTime(last!.fileModified, Date.now())}`,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </div>
            </div>
            <span class="spacer" />
            <LocationsMenu value={prefs.locations} onChange={(locations) => setPrefs({ ...prefs, locations })} />
          </div>
          )}

          {save.warnings.length > 0 && (
            <div class="notice warn stack">
              {save.warnings.map((w) => (
                <div>{w}</div>
              ))}
            </div>
          )}
          {save.unknown.length > 0 && (
            <details class="notice warn">
              <summary>
                {save.unknown.length} card{save.unknown.length === 1 ? '' : 's'} in this save could not be identified and
                {save.unknown.length === 1 ? ' is' : ' are'} not counted (often cards added by content mods).
              </summary>
              <ul class="small">
                {save.unknown.slice(0, 50).map((u) => (
                  <li>
                    {u.reason} ({u.location}): {u.detail}
                  </li>
                ))}
              </ul>
            </details>
          )}

          <nav class="tabs" role="tablist">
            {TABS.map((t) => (
              <button
                type="button"
                role="tab"
                id={`tab-${t.key}`}
                aria-controls={`panel-${t.key}`}
                aria-selected={tab === t.key}
                onClick={() => showTab(t.key)}
              >
                {t.label}
                {t.key === 'timeline' && shownSnapshots.length > 0 ? <span class="muted"> ({shownSnapshots.length})</span> : null}
              </button>
            ))}
          </nav>

          {visited.has('overview') && (
            <div class="stack" role="tabpanel" id="panel-overview" aria-labelledby="tab-overview" hidden={tab !== 'overview'}>
              <Tiles save={save} totals={totals} sets={sets} />
              <FilterRow filters={filters} onChange={setFilters}>
                <span class="spacer" />
                <label class="check small">
                  <input type="checkbox" checked={prefs.showCopies} onChange={(e) => setPrefs({ ...prefs, showCopies: e.currentTarget.checked })} />
                  Show copies
                </label>
              </FilterRow>
              <ProgressGrid
                save={save}
                totals={totals}
                sets={sets}
                filters={filters}
                showCopies={prefs.showCopies}
                open={openRows}
                onToggle={(id) =>
                  setOpenRows((prev) => {
                    const next = new Set(prev);
                    if (next.has(id)) next.delete(id);
                    else next.add(id);
                    return next;
                  })
                }
                onCell={(t) => {
                  // The list opens with the same rarity/foil/search filters the clicked cell counted.
                  setPreset({ ...t, filters: { ...filters, foil: t.foil ?? filters.foil } });
                  showTab('missing');
                }}
              />
            </div>
          )}
          {visited.has('missing') && (
            <div role="tabpanel" id="panel-missing" aria-labelledby="tab-missing" hidden={tab !== 'missing'}>
              <Missing save={save} totals={totals} sets={sets} preset={preset} />
            </div>
          )}
          {visited.has('value') && (
            <div role="tabpanel" id="panel-value" aria-labelledby="tab-value" hidden={tab !== 'value'}>
              <ValueView save={save} totals={totals} sets={sets} included={included} />
            </div>
          )}
          {visited.has('timeline') && (
            <div role="tabpanel" id="panel-timeline" aria-labelledby="tab-timeline" hidden={tab !== 'timeline'}>
            <Timeline
              snapshots={shownSnapshots}
              readOnly={!!example}
              sets={sets}
              included={included}
              currentPlayer={save.meta.playerName}
              currentId={last ? last.snapshotId : (example?.snapshots[example.snapshots.length - 1]?.id ?? null)}
              onDelete={async (id) => {
                await deleteSnapshot(id);
                setSnapshots(await listSnapshots());
              }}
              onForgetAll={async () => {
                await forgetEverything();
                setSnapshots([]);
                setLast(null);
                setTab('overview');
                setVisited(new Set<Tab>(['overview']));
                setPreset(null);
                setOpenRows(new Set());
                setFilters(DEFAULT_FILTERS);
                setErrors([]);
                setStorageWarning(false);
              }}
            />
            </div>
          )}
        </div>
      )}

      <footer class="foot">
        Fan-made and unofficial; not affiliated with OPNeon Games. Your save is processed entirely in your browser.
        Works with game version 1.0+ saves (older saves load without Ascension).
      </footer>
    </div>
  );
}

function ThemeIcon({ pref }: { pref: ThemePref }) {
  const common = { width: 16, height: 16, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': 2, 'aria-hidden': true } as const;
  if (pref === 'dark') {
    return (
      <svg {...common}>
        <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z" stroke-linejoin="round" />
      </svg>
    );
  }
  if (pref === 'light') {
    return (
      <svg {...common}>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" stroke-linecap="round" />
      </svg>
    );
  }
  return (
    <svg {...common}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor" />
    </svg>
  );
}
