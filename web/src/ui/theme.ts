export type ThemePref = 'system' | 'light' | 'dark';

const KEY = 'tcgct.theme';

export function loadThemePref(): ThemePref {
  try {
    const v = localStorage.getItem(KEY);
    if (v === 'light' || v === 'dark' || v === 'system') return v;
  } catch {
    // Storage can be unavailable (private mode, blocked site data); fall back to system.
  }
  return 'system';
}

export function applyThemePref(pref: ThemePref): void {
  const root = document.documentElement;
  if (pref === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', pref);
  try {
    localStorage.setItem(KEY, pref);
  } catch {
    // Ignore: the choice just won't persist.
  }
}
