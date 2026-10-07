const intFmt = new Intl.NumberFormat('en-US');

export function int(n: number): string {
  return intFmt.format(n);
}

export function compactInt(n: number): string {
  if (Math.abs(n) < 10_000) return intFmt.format(n);
  return new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(n);
}

export function signed(n: number, fmt: (x: number) => string = int): string {
  if (n === 0) return '±0';
  return (n > 0 ? '+' : '−') + fmt(Math.abs(n));
}

export function relativeTime(ms: number, now: number): string {
  const s = Math.round((now - ms) / 1000);
  if (s < 60) return 'just now';
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h} h ago`;
  const d = Math.round(h / 24);
  if (d < 30) return `${d} day${d === 1 ? '' : 's'} ago`;
  return new Date(ms).toLocaleDateString();
}

export function saveSlotLabel(fileName: string): string {
  const m = /savedGames_Release(BackupFile)?(\d+)\.json$/i.exec(fileName);
  if (!m) return fileName;
  const slot = Number(m[2]);
  const base = slot === 0 ? 'Autosave' : `Save slot ${slot}`;
  return m[1] ? `${base} (backup)` : base;
}
