/** Date-only values travel as `yyyy-MM-dd`. These helpers convert to/from local Date objects (no time-zone shift). */
export function toIsoDate(d: Date | null | undefined): string | null {
  if (!d) return null;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function fromIsoDate(s: string | null | undefined): Date | null {
  if (!s) return null;
  const [y, m, d] = s.split('-').map(Number);
  return y && m && d ? new Date(y, m - 1, d) : null;
}

/** Saves a downloaded file. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Opens a downloaded file (e.g. a PDF) in a new tab. */
export function openBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Quantity with up to 3 decimals, no grouping (matches the API's precision). */
export function qty(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return Number(value.toFixed(3)).toString();
}

/** A fresh id for one scan / one user action (idempotency). */
export function newId(): string {
  return globalThis.crypto.randomUUID();
}
