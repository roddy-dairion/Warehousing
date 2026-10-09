import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { API_BASE_URL, AuthResponse, whHttpInterceptors } from '@wh/shared';
import { LoginPage } from './login';

const API = '/api/v1';
const settle = () => new Promise((r) => setTimeout(r, 0));
const okResponse: AuthResponse = {
  accessToken: 't',
  accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
  user: { id: '1', email: 'op@test.local', displayName: 'Op', roles: [], permissions: [], allWarehouses: false, warehouses: [] },
};

function setup(returnUrl?: string) {
  TestBed.configureTestingModule({
    imports: [LoginPage],
    providers: [
      provideRouter([]),
      provideHttpClient(withInterceptors(whHttpInterceptors)),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: API },
    ],
  });
  const fixture = TestBed.createComponent(LoginPage);
  if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const type = (sel: string, v: string) => {
    const input = el.querySelector<HTMLInputElement>(sel)!;
    input.value = v;
    input.dispatchEvent(new Event('input'));
  };
  const submit = () => el.querySelector('form')!.dispatchEvent(new Event('submit'));
  return { fixture, el, type, submit, http: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
}

describe('LoginPage', () => {
  it('does not call the API when the form is invalid', async () => {
    const { el, fixture, http, submit } = setup();
    submit();
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectNone(`${API}/auth/login`);
    expect(el.textContent).toContain('Enter your work email address.');
  });

  it('shows a clear message when credentials are wrong', async () => {
    const { el, fixture, type, http, submit } = setup();
    type('#email', 'op@test.local');
    type('#password', 'bad');
    submit();
    http.expectOne(`${API}/auth/login`).flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    fixture.detectChanges();
    expect(el.textContent).toContain('Email or password is incorrect.');
  });

  it('navigates to a safe return URL after signing in', async () => {
    const { type, http, router, submit } = setup('/inventory');
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    type('#email', 'op@test.local');
    type('#password', 'secret');
    submit();
    http.expectOne(`${API}/auth/login`).flush(okResponse);
    await settle();
    expect(nav).toHaveBeenCalledWith('/inventory');
  });

  it('ignores an external return URL (open-redirect protection)', async () => {
    const { type, http, router, submit } = setup('//evil.example.com');
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    type('#email', 'op@test.local');
    type('#password', 'secret');
    submit();
    http.expectOne(`${API}/auth/login`).flush(okResponse);
    await settle();
    expect(nav).toHaveBeenCalledWith('/');
  });
});
