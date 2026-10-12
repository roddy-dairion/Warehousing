import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActionSheetController, AlertController } from '@ionic/angular';
import { AuthService, BarcodeLookupDto, DispatchDto, DispatchesApi, DispatchLineDto, InventoryApi, PickListDto, PickTaskDto, ProductsApi } from '@wh/shared';
import { Feedback } from '../../core/feedback';
import { PickPanel } from './pick-panel';

const line = (id: string, productId: string, sku: string, reserved: number, picked = 0): DispatchLineDto => ({
  id, lineNo: 1, productId, sku, productName: sku, isLotTracked: false, baseUnitCode: 'EA', productUnitId: null, unitCode: 'EA', enteredQuantity: reserved,
  quantity: reserved, quantityReserved: reserved, quantityPicked: picked, quantityShipped: 0, outstanding: reserved, notes: null,
});

const task = (reservationId: string, lineId: string, productId: string, sku: string, bin: string, remaining: number, lotNo: string | null = null): PickTaskDto => ({
  reservationId, lineId, lineNo: 1, productId, sku, productName: sku, baseUnitCode: 'EA', locationId: bin, locationCode: bin, locationBarcode: `BIN-${bin}`,
  lotId: null, lotNo, expiryDate: null, quantity: remaining, picked: 0, remaining,
});

const lookup = (productId: string, sku: string, factorToBase = 1, unitCode = 'EA'): BarcodeLookupDto => ({
  productId, sku, name: sku, isActive: true, barcode: 'x', unitCode, factorToBase, baseUnitCode: 'EA', isLotTracked: false, hasExpiry: false,
});

type Internals = {
  onBin(code: string): void;
  onProduct(code: string): Promise<void>;
  pick(): Promise<void>;
  scanned(): { scanId: string; code: string } | null;
  quantity: { (): number | null; set(v: number | null): void };
  current(): PickTaskDto | null;
  tooMuch(): boolean;
  pickList(): PickListDto | null;
};

async function setup() {
  const lines = [line('l1', 'soap', 'SOAP-100', 24), line('l2', 'oil', 'OIL-1L', 8)];
  const list: PickListDto = {
    dispatchId: 'd1', number: 'DSP-1', status: 'approved', assignedToName: null, lines,
    tasks: [task('r1', 'l1', 'soap', 'SOAP-100', 'A-01', 24, 'L1'), task('r2', 'l2', 'oil', 'OIL-1L', 'A-02', 8)],
  };
  const api = { pickList: vi.fn().mockResolvedValue(list), pick: vi.fn() };
  const products = { lookupBarcode: vi.fn() };
  const feedback = { warn: vi.fn().mockResolvedValue(undefined), success: vi.fn().mockResolvedValue(undefined), error: vi.fn().mockResolvedValue(undefined) };

  TestBed.overrideComponent(PickPanel, { set: { template: '', imports: [] } });
  TestBed.configureTestingModule({
    providers: [
      { provide: DispatchesApi, useValue: api },
      { provide: ProductsApi, useValue: products },
      { provide: InventoryApi, useValue: {} },
      { provide: Feedback, useValue: feedback },
      { provide: AlertController, useValue: {} },
      { provide: ActionSheetController, useValue: {} },
      { provide: AuthService, useValue: { user: signal(null) } },
    ],
  });
  const fixture = TestBed.createComponent(PickPanel);
  fixture.componentRef.setInput('dispatch', { id: 'd1', warehouseId: 'w1', lines, actions: {} } as unknown as DispatchDto);
  fixture.detectChanges();
  await fixture.whenStable();
  const panel = fixture.componentInstance as unknown as Internals;
  expect(panel.pickList()).not.toBeNull();
  return { panel, api, products, feedback };
}

describe('PickPanel', () => {
  it('needs the bin first, then proposes whole cartons for what is left', async () => {
    const { panel, products, feedback } = await setup();
    await panel.onProduct('761-CTN');
    expect(feedback.warn).toHaveBeenCalledWith('Scan the bin label first.');

    products.lookupBarcode.mockResolvedValue(lookup('soap', 'SOAP-100', 12, 'CTN'));
    panel.onBin('BIN-A-01'); // bin label barcode
    await panel.onProduct('761-CTN');
    expect(panel.current()?.reservationId).toBe('r1');
    expect(panel.quantity()).toBe(2); // 24 EA left = 2 cartons
    panel.quantity.set(3);
    expect(panel.tooMuch()).toBe(true);
  });

  it('warns about products that are not on the dispatch or are allocated to another bin', async () => {
    const { panel, products, feedback } = await setup();
    panel.onBin('A-01');
    products.lookupBarcode.mockResolvedValue(lookup('tea', 'TEA-1'));
    await panel.onProduct('tea');
    expect(feedback.warn).toHaveBeenLastCalledWith('TEA-1 is not on this dispatch. Put it back.');
    expect(panel.scanned()).toBeNull();

    products.lookupBarcode.mockResolvedValue(lookup('oil', 'OIL-1L'));
    await panel.onProduct('oil');
    expect(feedback.warn).toHaveBeenLastCalledWith(expect.stringContaining('allocated to bin A-02, not A-01'));
    expect(panel.current()?.reservationId).toBe('r2');
    expect(panel.quantity()).toBe(8);
  });

  it('keeps the scan id after a network failure so the retry is not counted twice', async () => {
    const { panel, api, products, feedback } = await setup();
    products.lookupBarcode.mockResolvedValue(lookup('oil', 'OIL-1L'));
    panel.onBin('A-02');
    await panel.onProduct('oil');
    const first = panel.scanned()!.scanId;

    api.pick.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await panel.pick();
    expect(feedback.error).toHaveBeenLastCalledWith(expect.anything(), { retrySafe: true });
    expect(panel.scanned()!.scanId).toBe(first);

    api.pick.mockRejectedValueOnce(new HttpErrorResponse({ status: 422, error: { detail: 'Over-pick' } }));
    await panel.pick();
    expect(panel.scanned()!.scanId).not.toBe(first);

    api.pick.mockResolvedValueOnce({ pickId: 'p1', replayed: false, sku: 'OIL-1L', quantity: 8, locationCode: 'A-02', lotNo: null });
    await panel.pick();
    expect(api.pick).toHaveBeenLastCalledWith('d1', expect.objectContaining({ location: 'A-02', barcode: 'oil', quantity: 8, lotNo: null }));
    expect(panel.scanned()).toBeNull();
  });
});
