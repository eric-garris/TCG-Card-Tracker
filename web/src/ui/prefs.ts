import { ALL_LOCATIONS } from '../core/collection';
import type { LocationKey } from '../core/save';

export interface Prefs {
  locations: LocationKey[];
  showCopies: boolean;
}

const KEY = 'tcgct.prefs';

export const DEFAULT_PREFS: Prefs = { locations: [...ALL_LOCATIONS], showCopies: true };

export function loadPrefs(): Prefs {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return DEFAULT_PREFS;
    const p = JSON.parse(raw) as Partial<Prefs>;
    return {
      locations: Array.isArray(p.locations)
        ? p.locations.filter((l): l is LocationKey => (ALL_LOCATIONS as readonly string[]).includes(l))
        : DEFAULT_PREFS.locations,
      showCopies: typeof p.showCopies === 'boolean' ? p.showCopies : DEFAULT_PREFS.showCopies,
    };
  } catch {
    return DEFAULT_PREFS;
  }
}

export function savePrefs(p: Prefs): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(p));
  } catch {
    // Not persisted; fine.
  }
}
