import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AlertController, IonButton, IonInput, IonItem, IonLabel, IonList, IonNote, IonSearchbar, IonSelect, IonSelectOption, IonSpinner, IonToggle,
} from '@ionic/angular';
import { ApiError, CatalogApi, ProductListItemDto, ProductsApi, qty, UnitDto } from '@wh/shared';
import { Feedback } from '../../core/feedback';

type Mode = 'choose' | 'link' | 'create';

/**
 * Shown after scanning a code the system does not know, to users allowed to manage products (`products.manage`):
 * add the barcode to an existing product (e.g. a new packaging or a carton), or create the product with this barcode.
 * The server checks the permission and that the barcode is not used by another product.
 */
@Component({
  selector: 'wh-unknown-code',
  imports: [FormsModule, IonButton, IonInput, IonItem, IonLabel, IonList, IonNote, IonSearchbar, IonSelect, IonSelectOption, IonSpinner, IonToggle],
  template: `
    @switch (mode()) {
      @case ('choose') {
        <div class="wh-actions">
          <ion-button expand="block" (click)="mode.set('link')" data-testid="unknown-link">Add this barcode to a product</ion-button>
          <ion-button expand="block" fill="outline" (click)="startCreate()" data-testid="unknown-create">Create a new product</ion-button>
        </div>
      }
      @case ('link') {
        <div class="wh-section"><h2>Which product is it?</h2></div>
        <ion-searchbar placeholder="SKU or name" [debounce]="300" (ionInput)="search($any($event.target).value)" data-testid="unknown-search" />
        @if (searching()) { <div class="wh-empty"><ion-spinner /></div> }
        <ion-list lines="full">
          @for (p of results(); track p.id) {
            <ion-item button detail (click)="link(p)" [disabled]="busy()">
              <ion-label><h3>{{ p.sku }}</h3><p>{{ p.name }}</p></ion-label>
              @if (p.primaryBarcode) { <ion-note slot="end">{{ p.primaryBarcode }}</ion-note> }
            </ion-item>
          }
        </ion-list>
        <div class="wh-actions"><ion-button expand="block" fill="clear" (click)="mode.set('choose')">Back</ion-button></div>
      }
      @case ('create') {
        <div class="wh-section form">
          <h2>{{ code() ? 'New product with barcode ' + code() : 'New product' }}</h2>
          <ion-input label="SKU" labelPlacement="stacked" fill="outline" [(ngModel)]="sku" autocapitalize="characters" data-testid="new-sku" />
          <ion-input label="Name" labelPlacement="stacked" fill="outline" [(ngModel)]="name" autocapitalize="sentences" data-testid="new-name" />
          <ion-select label="Counted in" labelPlacement="stacked" fill="outline" interface="action-sheet" [(ngModel)]="baseUnitId" data-testid="new-unit">
            @for (u of units(); track u.id) { <ion-select-option [value]="u.id">{{ u.code }} · {{ u.name }}</ion-select-option> }
          </ion-select>
          <ion-toggle [(ngModel)]="lotTracked" (ionChange)="!lotTracked && (hasExpiry = false)">Tracked by lot</ion-toggle>
          <ion-toggle [(ngModel)]="hasExpiry" [disabled]="!lotTracked">Has expiry dates</ion-toggle>
          <p class="wh-muted hint">Pack sizes, category, minimum stock and an image can be added later in the web app.</p>
        </div>
        <div class="wh-actions">
          <ion-button expand="block" (click)="create()" [disabled]="busy() || !sku.trim() || !name.trim() || !baseUnitId" data-testid="new-save">Create product</ion-button>
          @if (!createOnly()) { <ion-button expand="block" fill="clear" (click)="mode.set('choose')">Back</ion-button> }
        </div>
      }
    }
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: 12px; }
    .hint { font-size: 0.85rem; margin: 0; }
  `,
})
export class UnknownCode implements OnInit {
  private readonly products = inject(ProductsApi);
  private readonly catalog = inject(CatalogApi);
  private readonly feedback = inject(Feedback);
  private readonly alerts = inject(AlertController);

  /** The scanned code nobody knows yet. */
  readonly code = input.required<string>();
  /** Open directly on the "create product" form (the "Add a product" screen; the barcode is optional there). */
  readonly createOnly = input(false);
  /** The barcode now belongs to a product (or the product was created): the page shows it. */
  readonly done = output<{ code: string; productId: string }>();

  protected readonly mode = signal<Mode>('choose');
  protected readonly busy = signal(false);
  protected readonly searching = signal(false);
  protected readonly results = signal<ProductListItemDto[]>([]);
  protected readonly units = signal<UnitDto[]>([]);
  protected sku = '';
  protected name = '';
  protected baseUnitId: string | null = null;
  protected lotTracked = false;
  protected hasExpiry = false;

  protected async search(term: string | null): Promise<void> {
    const t = (term ?? '').trim();
    if (t.length < 2) {
      this.results.set([]);
      return;
    }
    this.searching.set(true);
    try {
      this.results.set((await this.products.list({ search: t, pageSize: 20, isActive: true })).items);
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.searching.set(false);
    }
  }

  protected async link(p: ProductListItemDto): Promise<void> {
    this.busy.set(true);
    try {
      const product = await this.products.getOne(p.id);
      // A barcode can stand for one base unit or for a pack (e.g. a carton of 12).
      let unitId: string | null = null;
      if (product.units.length > 0) {
        const alert = await this.alerts.create({
          header: `What does ${this.code()} stand for?`,
          inputs: [
            { type: 'radio', label: `1 ${product.baseUnitCode}`, value: '', checked: true },
            ...product.units.map((u) => ({ type: 'radio' as const, label: `1 ${u.unitCode} (${qty(u.factorToBase)} ${product.baseUnitCode})`, value: u.unitId })),
          ],
          buttons: [{ text: 'Cancel', role: 'cancel' }, { text: 'Add barcode', role: 'confirm' }],
        });
        await alert.present();
        const result = await alert.onDidDismiss<{ values: string }>();
        if (result.role !== 'confirm') return;
        unitId = result.data?.values || null;
      } else {
        const ok = await this.feedback.confirm('Add barcode', `Add barcode ${this.code()} to ${product.sku} ${product.name}?`, 'Add barcode');
        if (!ok) return;
      }
      await this.products.addBarcode(product.id, this.code(), unitId, product.version);
      await this.feedback.success(`Barcode added to ${product.sku}.`);
      this.done.emit({ code: this.code(), productId: product.id });
    } catch (e) {
      await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  ngOnInit(): void {
    if (this.createOnly()) void this.startCreate();
  }

  protected async startCreate(): Promise<void> {
    this.mode.set('create');
    if (this.units().length > 0) return;
    try {
      const units = (await this.catalog.units()).filter((u) => u.isActive);
      this.units.set(units);
      this.baseUnitId ??= units.find((u) => u.code === 'EA')?.id ?? units[0]?.id ?? null;
    } catch (e) {
      await this.feedback.error(e);
    }
  }

  protected async create(): Promise<void> {
    if (!this.baseUnitId) return;
    this.busy.set(true);
    try {
      const p = await this.products.create({
        sku: this.sku.trim().toUpperCase(), name: this.name.trim(), baseUnitId: this.baseUnitId, isLotTracked: this.lotTracked,
        hasExpiry: this.lotTracked && this.hasExpiry, barcodes: this.code() ? [{ value: this.code() }] : [],
      });
      await this.feedback.success(`${p.sku} created.`);
      this.done.emit({ code: this.code(), productId: p.id });
    } catch (e) {
      const err = ApiError.from(e);
      const fields = Object.values(err.fieldErrors).flat();
      if (fields.length > 0) await this.feedback.warn(fields[0]!);
      else await this.feedback.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
