import { Component, inject, input, OnDestroy, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonButton, IonIcon, IonInput, IonTextarea } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { cameraOutline, closeCircle } from 'ionicons/icons';
import { DispatchDto, DispatchesApi, SignaturePad } from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { shrinkPhoto } from '../../core/images';

interface Photo {
  file: File;
  url: string;
}

/** Proof of delivery on the phone: receiver's name, photos of the goods, signature drawn on the screen. */
@Component({
  selector: 'wh-deliver-panel',
  imports: [FormsModule, IonButton, IonIcon, IonInput, IonTextarea, SignaturePad],
  template: `
    <div class="wh-section fields">
      <h2>Proof of delivery</h2>
      <ion-input label="Received by (name)" labelPlacement="stacked" fill="outline" [(ngModel)]="receiver" autocapitalize="words" data-testid="pod-receiver" />

      <div>
        <div class="caption">Photos of the delivered goods ({{ photos().length }})</div>
        <div class="photos">
          @for (p of photos(); track p.url; let i = $index) {
            <div class="thumb">
              <img [src]="p.url" alt="Delivery photo {{ i + 1 }}" />
              <button type="button" (click)="remove(i)" aria-label="Remove photo"><ion-icon name="close-circle" /></button>
            </div>
          }
          <button type="button" class="add" (click)="fileInput.click()" [disabled]="photos().length >= 10 || busy()">
            <ion-icon name="camera-outline" aria-hidden="true" /><span>Take photo</span>
          </button>
        </div>
        <input #fileInput type="file" accept="image/*" capture="environment" multiple hidden (change)="onFiles($event)" data-testid="pod-photos" />
      </div>

      <div>
        <div class="caption">Receiver's signature</div>
        <wh-signature-pad #pad label="Receiver's signature" height="14rem" (changed)="signed.set($event)" />
      </div>

      <ion-textarea label="Notes (optional)" labelPlacement="stacked" fill="outline" [(ngModel)]="notes" [autoGrow]="true" />
    </div>
    <div class="wh-actions">
      <ion-button expand="block" size="large" (click)="save()" [disabled]="busy() || !receiver.trim() || photos().length === 0 || !signed()" data-testid="pod-save">
        Save delivery
      </ion-button>
      @if (!receiver.trim() || photos().length === 0 || !signed()) {
        <p class="wh-muted hint">Name, at least one photo and the signature are required.</p>
      }
    </div>
  `,
  styles: `
    .fields { display: flex; flex-direction: column; gap: 14px; }
    .caption { font-size: 0.85rem; color: var(--ion-color-medium-shade); margin-bottom: 6px; }
    .photos { display: flex; flex-wrap: wrap; gap: 8px; }
    .thumb { position: relative; width: 84px; height: 84px; }
    .thumb img { width: 100%; height: 100%; object-fit: cover; border-radius: 6px; }
    .thumb button { position: absolute; top: -8px; right: -8px; background: none; border: 0; padding: 0; font-size: 1.5rem; color: var(--ion-color-danger); }
    .add { width: 84px; height: 84px; border: 2px dashed var(--ion-color-medium); border-radius: 6px; background: none; color: var(--ion-color-primary);
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; font: inherit; font-size: 0.75rem; }
    .add ion-icon { font-size: 1.6rem; }
    .hint { text-align: center; margin: 0; font-size: 0.85rem; }
  `,
})
export class DeliverPanel implements OnDestroy {
  private readonly api = inject(DispatchesApi);
  private readonly feedback = inject(Feedback);
  private readonly pad = viewChild.required<SignaturePad>('pad');

  readonly dispatch = input.required<DispatchDto>();
  readonly changed = output<void>();

  protected readonly busy = signal(false);
  protected readonly photos = signal<Photo[]>([]);
  protected readonly signed = signal(false);
  protected receiver = '';
  protected notes = '';

  constructor() {
    addIcons({ cameraOutline, closeCircle });
  }

  ngOnDestroy(): void {
    this.photos().forEach((p) => URL.revokeObjectURL(p.url));
  }

  protected async onFiles(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    for (const f of files) {
      if (this.photos().length >= 10) break;
      const file = await shrinkPhoto(f);
      this.photos.update((p) => [...p, { file, url: URL.createObjectURL(file) }]);
    }
  }

  protected remove(i: number): void {
    const p = this.photos()[i];
    if (p) URL.revokeObjectURL(p.url);
    this.photos.update((list) => list.filter((_, x) => x !== i));
  }

  protected async save(): Promise<void> {
    const signature = await this.pad().toBlob();
    const d = this.dispatch();
    if (!signature || !this.receiver.trim() || this.photos().length === 0) return;
    this.busy.set(true);
    try {
      await this.api.deliver(d.id, {
        receiverName: this.receiver.trim(), deliveredAt: new Date().toISOString(), notes: this.notes.trim() || null, signature,
        photos: this.photos().map((p) => p.file),
      });
      await this.feedback.success('Delivery recorded. The signed delivery note is emailed to the customer.');
      this.changed.emit();
    } catch (e) {
      // A retry after a lost connection may find the delivery already recorded by the first attempt.
      const now = await this.api.getOne(d.id).catch(() => null);
      if (now?.status === 'delivered') {
        await this.feedback.success('Delivery recorded.');
        this.changed.emit();
      } else {
        await this.feedback.error(e);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
