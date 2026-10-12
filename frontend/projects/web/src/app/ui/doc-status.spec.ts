import { TestBed } from '@angular/core/testing';
import { DocStatus } from './doc-status';

function render(kind: 'receipt' | 'adjustment' | 'dispatch', status: string): string {
  const fixture = TestBed.createComponent(DocStatus);
  fixture.componentRef.setInput('kind', kind);
  fixture.componentRef.setInput('status', status);
  fixture.detectChanges();
  return (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '';
}

describe('DocStatus', () => {
  it('shows readable labels for each document kind', () => {
    expect(render('dispatch', 'readyForDispatch')).toBe('Ready to ship');
    expect(render('dispatch', 'submitted')).toBe('Awaiting approval');
    expect(render('adjustment', 'pendingApproval')).toBe('Awaiting approval');
    expect(render('receipt', 'posted')).toBe('Posted');
  });
});
