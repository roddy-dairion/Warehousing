import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Select } from 'primeng/select';
import { Button } from 'primeng/button';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { AuthService, WarehouseContextService } from '@wh/shared';
import { visibleNavigation } from './navigation';

@Component({
  selector: 'wh-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, Select, Button, Menu],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly warehouses = inject(WarehouseContextService);

  protected readonly user = this.auth.user;
  protected readonly navOpen = signal(false);
  protected readonly nav = computed(() => {
    this.auth.user(); // re-evaluate when the user (and permissions) change
    return visibleNavigation((p) => this.auth.hasPermission(p));
  });
  protected readonly initials = computed(() =>
    (this.user()?.displayName ?? '?')
      .split(/\s+/)
      .map((p) => p[0] ?? '')
      .join('')
      .slice(0, 2)
      .toUpperCase(),
  );
  protected readonly userMenu: MenuItem[] = [
    { label: 'Sign out', icon: 'pi pi-sign-out', command: () => void this.signOut() },
  ];

  protected onWarehouseChange(id: string): void {
    this.warehouses.select(id);
  }

  private async signOut(): Promise<void> {
    await this.auth.logout();
    this.warehouses.clear();
    await this.router.navigateByUrl('/login');
  }
}
