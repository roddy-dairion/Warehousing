import { TestBed } from '@angular/core/testing';
import { QtyStepper } from './qty-stepper';

type Internals = { step(d: number): void; set(v: string | number | null): void };

describe('QtyStepper', () => {
  it('steps within min and max and parses typed values', () => {
    TestBed.overrideComponent(QtyStepper, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(QtyStepper);
    fixture.componentRef.setInput('max', 3);
    const s = fixture.componentInstance as QtyStepper & Internals;
    s.value.set(2.5);
    s.step(1);
    expect(s.value()).toBe(3);
    s.step(1);
    expect(s.value()).toBe(3);
    s.value.set(0.4);
    s.step(-1);
    expect(s.value()).toBe(0);
    s.set('1.25');
    expect(s.value()).toBe(1.25);
    s.set('');
    expect(s.value()).toBeNull();
  });
});
