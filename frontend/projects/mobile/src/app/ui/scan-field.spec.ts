import { TestBed } from '@angular/core/testing';
import { ScanField } from './scan-field';
import { ScannerService } from '../core/scanner.service';

type Internals = { submit(): void };

function create(keep = false) {
  TestBed.overrideComponent(ScanField, { set: { template: '', imports: [] } });
  TestBed.configureTestingModule({ providers: [{ provide: ScannerService, useValue: { cameraAvailable: false } }] });
  const fixture = TestBed.createComponent(ScanField);
  fixture.componentRef.setInput('keep', keep);
  const emitted: string[] = [];
  fixture.componentInstance.scanned.subscribe((c) => emitted.push(c));
  return { field: fixture.componentInstance, emitted };
}

describe('ScanField', () => {
  it('emits the trimmed code once and clears the field, ignoring a repeat within a second', () => {
    const { field, emitted } = create();
    field.value.set('  7610000000024 ');
    (field as unknown as Internals).submit();
    expect(emitted).toEqual(['7610000000024']);
    expect(field.value()).toBe('');
    field.value.set('7610000000024');
    (field as unknown as Internals).submit();
    expect(emitted).toHaveLength(1);
    field.value.set('BIN-A01');
    (field as unknown as Internals).submit();
    expect(emitted).toEqual(['7610000000024', 'BIN-A01']);
  });

  it('keeps the value when asked (a bin used for several picks) and ignores empty input', () => {
    const { field, emitted } = create(true);
    field.value.set('   ');
    (field as unknown as Internals).submit();
    expect(emitted).toEqual([]);
    field.value.set('A-01');
    (field as unknown as Internals).submit();
    expect(field.value()).toBe('A-01');
    expect(emitted).toEqual(['A-01']);
  });
});
