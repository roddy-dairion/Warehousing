import { TestBed } from '@angular/core/testing';
import { AuthImage } from './auth-image';

describe('AuthImage', () => {
  beforeAll(() => {
    // jsdom has no object URLs.
    URL.createObjectURL ??= () => 'blob:test';
    URL.revokeObjectURL ??= () => undefined;
  });

  it('loads the image once and shows it (no reload loop)', async () => {
    let calls = 0;
    const load = () => {
      calls++;
      return Promise.resolve(new Blob(['x'], { type: 'image/png' }));
    };
    const fixture = TestBed.createComponent(AuthImage);
    fixture.componentRef.setInput('load', load);
    fixture.detectChanges();
    await fixture.whenStable();
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
    expect(calls).toBe(1);
    expect((fixture.nativeElement as HTMLElement).querySelector('img')).not.toBeNull();
  });

  it('shows a message when the image cannot be loaded', async () => {
    const fixture = TestBed.createComponent(AuthImage);
    fixture.componentRef.setInput('load', () => Promise.reject(new Error('404')));
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Image unavailable');
  });
});
