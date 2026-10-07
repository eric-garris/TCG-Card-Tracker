/**
 * Local persistence in the browser's IndexedDB: snapshot history plus the last loaded save,
 * so the page can reopen where it left off. Everything stays on this device. When storage is
 * unavailable (private windows, blocked site data) it falls back to memory for the session.
 */

import { CATALOG_VERSION } from './catalog';
import type { ParsedSave } from './save';
import type { Snapshot } from './snapshot';

const DB_NAME = 'tcg-card-tracker';
const SNAPSHOTS = 'snapshots';
const STATE = 'state';

export interface LastSave {
  save: ParsedSave;
  fileName: string;
  fileModified: number;
  snapshotId: string;
  /** Count arrays are indexed by catalog position; a different catalog makes them meaningless. */
  catalogVersion: number;
}

let memorySnapshots: Snapshot[] = [];
let memoryLast: LastSave | null = null;
let dbPromise: Promise<IDBDatabase | null> | null = null;

function openDb(): Promise<IDBDatabase | null> {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve) => {
    // Some browsers (e.g. Safari 14) can leave the open request hanging; don't wait forever.
    const timer = setTimeout(() => resolve(null), 3000);
    const done = (db: IDBDatabase | null) => {
      clearTimeout(timer);
      resolve(db);
    };
    try {
      if (typeof indexedDB === 'undefined') return done(null);
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(SNAPSHOTS)) db.createObjectStore(SNAPSHOTS, { keyPath: 'id' });
        if (!db.objectStoreNames.contains(STATE)) db.createObjectStore(STATE);
      };
      req.onsuccess = () => done(req.result);
      req.onerror = () => done(null);
      req.onblocked = () => done(null);
    } catch {
      done(null);
    }
  });
  return dbPromise;
}

/** Resolves when the transaction commits, so quota errors raised at commit time are seen. */
function run<T>(db: IDBDatabase, store: string, mode: IDBTransactionMode, op: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    const t = db.transaction(store, mode);
    const req = op(t.objectStore(store));
    t.oncomplete = () => resolve(req.result);
    t.onabort = () => reject(t.error ?? req.error);
    t.onerror = () => reject(t.error ?? req.error);
  });
}

export async function storageAvailable(): Promise<boolean> {
  return (await openDb()) !== null;
}

/** Snapshots made with the current catalog. Ones from an older catalog can't be compared and are skipped. */
export async function listSnapshots(): Promise<Snapshot[]> {
  const db = await openDb();
  let stored: Snapshot[] = [];
  if (db) {
    try {
      stored = await run<Snapshot[]>(db, SNAPSHOTS, 'readonly', (s) => s.getAll() as IDBRequest<Snapshot[]>);
    } catch {
      stored = [];
    }
  }
  // Include snapshots that only made it into memory (storage full or blocked).
  const ids = new Set(stored.map((s) => s.id));
  const all = [...stored, ...memorySnapshots.filter((m) => !ids.has(m.id))];
  return all.filter((s) => s.catalogVersion === CATALOG_VERSION);
}

/** Returns false when the snapshot could only be kept in memory for this session. */
export async function putSnapshot(snap: Snapshot): Promise<boolean> {
  memorySnapshots = [...memorySnapshots.filter((m) => m.id !== snap.id), snap];
  const db = await openDb();
  if (!db) return false;
  try {
    await run(db, SNAPSHOTS, 'readwrite', (s) => s.put(snap));
    return true;
  } catch {
    return false;
  }
}

export async function deleteSnapshot(id: string): Promise<void> {
  memorySnapshots = memorySnapshots.filter((m) => m.id !== id);
  const db = await openDb();
  if (!db) return;
  try {
    await run(db, SNAPSHOTS, 'readwrite', (s) => s.delete(id));
  } catch {
    // Ignore.
  }
}

export async function loadLastSave(): Promise<LastSave | null> {
  const db = await openDb();
  let last: LastSave | null = memoryLast;
  if (db) {
    try {
      last = ((await run(db, STATE, 'readonly', (s) => s.get('lastSave'))) as LastSave | undefined) ?? null;
    } catch {
      last = memoryLast;
    }
  }
  return last && last.catalogVersion === CATALOG_VERSION ? last : null;
}

export async function saveLastSave(last: LastSave): Promise<void> {
  memoryLast = last;
  const db = await openDb();
  if (!db) return;
  try {
    await run(db, STATE, 'readwrite', (s) => s.put(last, 'lastSave'));
  } catch {
    // Ignore.
  }
}

/** Remove everything this tracker stored on this device. */
export async function forgetEverything(): Promise<void> {
  memorySnapshots = [];
  memoryLast = null;
  const db = await openDb();
  if (!db) return;
  try {
    await run(db, SNAPSHOTS, 'readwrite', (s) => s.clear());
    await run(db, STATE, 'readwrite', (s) => s.clear());
  } catch {
    // Ignore.
  }
}
