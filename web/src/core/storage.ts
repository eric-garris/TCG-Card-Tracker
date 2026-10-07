/**
 * Local persistence in the browser's IndexedDB: snapshot history plus the last loaded save,
 * so the page can reopen where it left off. Everything stays on this device. When storage is
 * unavailable (private windows, blocked site data) it falls back to memory for the session.
 */

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
}

let memorySnapshots: Snapshot[] = [];
let memoryLast: LastSave | null = null;
let dbPromise: Promise<IDBDatabase | null> | null = null;

function openDb(): Promise<IDBDatabase | null> {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve) => {
    try {
      if (typeof indexedDB === 'undefined') return resolve(null);
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(SNAPSHOTS)) db.createObjectStore(SNAPSHOTS, { keyPath: 'id' });
        if (!db.objectStoreNames.contains(STATE)) db.createObjectStore(STATE);
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
      req.onblocked = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
  return dbPromise;
}

function run<T>(db: IDBDatabase, store: string, mode: IDBTransactionMode, op: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    const t = db.transaction(store, mode);
    const req = op(t.objectStore(store));
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

export async function storageAvailable(): Promise<boolean> {
  return (await openDb()) !== null;
}

export async function listSnapshots(): Promise<Snapshot[]> {
  const db = await openDb();
  if (!db) return [...memorySnapshots];
  try {
    return await run<Snapshot[]>(db, SNAPSHOTS, 'readonly', (s) => s.getAll() as IDBRequest<Snapshot[]>);
  } catch {
    return [...memorySnapshots];
  }
}

export async function putSnapshot(snap: Snapshot): Promise<void> {
  memorySnapshots = [...memorySnapshots.filter((m) => m.id !== snap.id), snap];
  const db = await openDb();
  if (!db) return;
  try {
    await run(db, SNAPSHOTS, 'readwrite', (s) => s.put(snap));
  } catch {
    // Quota or blocked storage: the in-memory copy still works for this session.
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
  if (!db) return memoryLast;
  try {
    return ((await run(db, STATE, 'readonly', (s) => s.get('lastSave'))) as LastSave | undefined) ?? null;
  } catch {
    return memoryLast;
  }
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
