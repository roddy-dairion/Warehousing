import { Permissions } from '@wh/shared';
import { visibleNavigation } from './navigation';

describe('visibleNavigation', () => {
  it('shows only items the user has a permission for and drops empty groups', () => {
    const granted = new Set<string>([Permissions.InventoryRead]);
    const nav = visibleNavigation((p) => granted.has(p));
    expect(nav.flatMap((g) => g.items.map((i) => i.label))).toEqual(['Dashboard', 'Stock']);
    expect(nav.map((g) => g.label)).toEqual(['Operations']);
  });

  it('shows every group to a user holding all permissions', () => {
    const nav = visibleNavigation(() => true);
    expect(nav.map((g) => g.label)).toEqual(['Operations', 'Master data', 'Administration']);
  });
});
