import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { AUTH_ROUTES } from '../core/tokens';
import { AuthService } from './auth.service';
import { Permission } from './permissions';

/** Allows the route only for a signed-in user; tries to restore the session first. */
export const authGuard: CanMatchFn = async (_route, segments) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const routes = inject(AUTH_ROUTES);
  if (auth.isAuthenticated() || (await auth.restoreSession())) {
    return true;
  }
  const returnUrl = '/' + segments.map((s) => s.path).join('/');
  return router.createUrlTree([routes.login], { queryParams: returnUrl !== '/' ? { returnUrl } : {} });
};

/** Allows the route only when the user holds at least one of the permissions. UI convenience only. */
export function permissionGuard(...permissions: Permission[]): CanMatchFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const routes = inject(AUTH_ROUTES);
    return auth.hasAnyPermission(...permissions) ? true : router.createUrlTree([routes.forbidden]);
  };
}

/** For the login page: a signed-in user is sent home. */
export const guestGuard: CanMatchFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() || (await auth.restoreSession()) ? router.createUrlTree(['/']) : true;
};
