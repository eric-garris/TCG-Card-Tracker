import { useRef, useState } from 'preact/hooks';

export const SAVE_FOLDER = '%USERPROFILE%\\AppData\\LocalLow\\OPNeonGames\\Card Shop Simulator';

interface PickerProps {
  onFiles: (files: File[]) => void;
  label?: string;
  primary?: boolean;
}

/** A button that opens the file picker (accepts several saves at once). */
export function FilePicker({ onFiles, label = 'Choose save file', primary }: PickerProps) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <>
      <button type="button" class={`btn ${primary ? 'primary' : ''}`} onClick={() => input.current?.click()}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
          <path d="M12 16V4M6 10l6-6 6 6M4 20h16" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
        {label}
      </button>
      <input
        ref={input}
        type="file"
        accept=".json,application/json"
        multiple
        class="visually-hidden"
        tabIndex={-1}
        aria-hidden="true"
        onChange={(e) => {
          const el = e.currentTarget;
          const files = el.files ? Array.from(el.files) : [];
          el.value = '';
          if (files.length) onFiles(files);
        }}
      />
    </>
  );
}

export function CopyPath() {
  const [copied, setCopied] = useState(false);
  return (
    <span class="pathbox">
      <code>{SAVE_FOLDER}</code>
      <button
        type="button"
        class="btn ghost small"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(SAVE_FOLDER);
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          } catch {
            // Clipboard blocked; the path is still selectable.
          }
        }}
      >
        {copied ? 'Copied' : 'Copy'}
      </button>
    </span>
  );
}

interface DropProps {
  onFiles: (files: File[]) => void;
  busy: boolean;
}

export function DropZone({ onFiles, busy }: DropProps) {
  const [over, setOver] = useState(false);
  return (
    <div
      class={`drop ${over ? 'over' : ''}`}
      onDragOver={(e) => {
        e.preventDefault();
        setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        e.preventDefault();
        setOver(false);
        const files = e.dataTransfer ? Array.from(e.dataTransfer.files) : [];
        if (files.length) onFiles(files);
      }}
    >
      <h2>{busy ? 'Reading your save…' : 'Drop your save file here'}</h2>
      <p class="muted">
        <code>savedGames_Release0.json</code> is the autosave; <code>1</code>–<code>3</code> are your manual slots. You can
        drop several at once, including the <code>…BackupFile</code> copies, to see progress over time.
      </p>
      <div class="row" style={{ justifyContent: 'center', marginTop: '12px' }}>
        <FilePicker onFiles={onFiles} primary />
      </div>
      <div style={{ marginTop: '14px' }}>
        <div class="small muted">Saves live in this folder on Windows (paste it into File Explorer's address bar):</div>
        <CopyPath />
      </div>
      <p class="small muted" style={{ marginTop: '14px', marginBottom: 0 }}>
        Your save is read in this browser and never uploaded anywhere.
      </p>
    </div>
  );
}
