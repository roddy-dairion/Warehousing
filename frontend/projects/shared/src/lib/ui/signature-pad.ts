import { AfterViewInit, Component, ElementRef, input, OnDestroy, output, signal, viewChild } from '@angular/core';

/**
 * Signature capture with mouse, pen or finger (pointer events). Produces a PNG.
 * Works on desktop browsers, tablets and phones.
 */
@Component({
  selector: 'wh-signature-pad',
  template: `
    <div class="pad" [class.signed]="!empty()" [style.height]="height()">
      <canvas #canvas [attr.aria-label]="label()" role="img"></canvas>
      @if (empty()) { <span class="placeholder" aria-hidden="true">Sign here</span> }
    </div>
    <div class="pad-actions">
      <button type="button" class="clear" (click)="clear()" [disabled]="empty()">{{ clearLabel() }}</button>
    </div>
  `,
  styles: `
    .pad { position: relative; border: 1px dashed var(--wh-line, #c5ced6); border-radius: 6px; background: #fff; height: 11rem; touch-action: none; }
    .pad.signed { border-style: solid; }
    canvas { width: 100%; height: 100%; display: block; cursor: crosshair; }
    .placeholder { position: absolute; inset: 0; display: grid; place-items: center; color: var(--wh-muted, #6b7280); pointer-events: none; }
    .pad-actions { display: flex; justify-content: flex-end; }
    .clear { background: none; border: 0; padding: 0.5rem 0.25rem; font: inherit; font-size: 0.875rem; color: var(--wh-sig-accent, #0f5c7a); cursor: pointer; }
    .clear:disabled { opacity: 0.4; cursor: default; }
  `,
})
export class SignaturePad implements AfterViewInit, OnDestroy {
  readonly label = input('Signature');
  readonly clearLabel = input('Clear');
  readonly height = input('11rem');
  readonly changed = output<boolean>();

  protected readonly empty = signal(true);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private ctx: CanvasRenderingContext2D | null = null;
  private drawing = false;
  private last: { x: number; y: number } | null = null;
  private resizeObserver?: ResizeObserver;

  ngAfterViewInit(): void {
    const canvas = this.canvas().nativeElement;
    this.ctx = canvas.getContext('2d');
    this.resize();
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.resize());
      this.resizeObserver.observe(canvas);
    }
    canvas.addEventListener('pointerdown', this.down);
    canvas.addEventListener('pointermove', this.move);
    canvas.addEventListener('pointerup', this.up);
    canvas.addEventListener('pointerleave', this.up);
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    const canvas = this.canvas().nativeElement;
    canvas.removeEventListener('pointerdown', this.down);
    canvas.removeEventListener('pointermove', this.move);
    canvas.removeEventListener('pointerup', this.up);
    canvas.removeEventListener('pointerleave', this.up);
  }

  isEmpty(): boolean {
    return this.empty();
  }

  clear(): void {
    const canvas = this.canvas().nativeElement;
    this.ctx?.clearRect(0, 0, canvas.width, canvas.height);
    this.empty.set(true);
    this.changed.emit(false);
  }

  /** PNG of the signature (white background), or null when nothing was drawn. */
  toBlob(): Promise<Blob | null> {
    if (this.empty()) return Promise.resolve(null);
    const source = this.canvas().nativeElement;
    const out = document.createElement('canvas');
    out.width = source.width;
    out.height = source.height;
    const octx = out.getContext('2d')!;
    octx.fillStyle = '#ffffff';
    octx.fillRect(0, 0, out.width, out.height);
    octx.drawImage(source, 0, 0);
    return new Promise((resolve) => out.toBlob((b) => resolve(b), 'image/png'));
  }

  private resize(): void {
    const canvas = this.canvas().nativeElement;
    const ratio = window.devicePixelRatio || 1;
    // Layout size (not affected by the dialog's open animation transform, unlike getBoundingClientRect).
    const width = canvas.offsetWidth;
    const height = canvas.offsetHeight;
    if (width === 0 || height === 0) return;
    const w = Math.round(width * ratio);
    const h = Math.round(height * ratio);
    if (canvas.width === w && canvas.height === h) return;
    // Resizing a canvas wipes it: keep what was signed and redraw it at the new size.
    let previous: HTMLCanvasElement | null = null;
    if (!this.empty() && canvas.width > 0 && canvas.height > 0) {
      previous = document.createElement('canvas');
      previous.width = canvas.width;
      previous.height = canvas.height;
      previous.getContext('2d')?.drawImage(canvas, 0, 0);
    }
    canvas.width = w;
    canvas.height = h;
    if (this.ctx) {
      this.ctx.setTransform(1, 0, 0, 1, 0, 0);
      if (previous) this.ctx.drawImage(previous, 0, 0, w, h);
      this.ctx.scale(ratio, ratio);
      this.ctx.lineWidth = 2.2;
      this.ctx.lineCap = 'round';
      this.ctx.lineJoin = 'round';
      this.ctx.strokeStyle = '#111827';
    }
  }

  /** Pointer position in canvas CSS pixels (corrects for any transform scaling the element on screen). */
  private point(e: PointerEvent): { x: number; y: number } {
    const canvas = this.canvas().nativeElement;
    const rect = canvas.getBoundingClientRect();
    const sx = rect.width ? canvas.offsetWidth / rect.width : 1;
    const sy = rect.height ? canvas.offsetHeight / rect.height : 1;
    return { x: (e.clientX - rect.left) * sx, y: (e.clientY - rect.top) * sy };
  }

  private readonly down = (e: PointerEvent) => {
    e.preventDefault();
    this.resize();
    this.canvas().nativeElement.setPointerCapture?.(e.pointerId);
    this.drawing = true;
    this.last = this.point(e);
    // A dot for a single tap.
    this.ctx?.beginPath();
    this.ctx?.arc(this.last.x, this.last.y, 1, 0, Math.PI * 2);
    this.ctx?.stroke();
    this.markSigned();
  };

  private readonly move = (e: PointerEvent) => {
    if (!this.drawing || !this.ctx || !this.last) return;
    e.preventDefault();
    const p = this.point(e);
    this.ctx.beginPath();
    this.ctx.moveTo(this.last.x, this.last.y);
    this.ctx.lineTo(p.x, p.y);
    this.ctx.stroke();
    this.last = p;
  };

  private readonly up = () => {
    this.drawing = false;
    this.last = null;
  };

  private markSigned(): void {
    if (this.empty()) {
      this.empty.set(false);
      this.changed.emit(true);
    }
  }
}
