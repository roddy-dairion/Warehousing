import { fromIsoDate, qty, toIsoDate } from './format';

describe('format helpers', () => {
  it('converts dates to and from yyyy-MM-dd without a time-zone shift', () => {
    expect(toIsoDate(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
    expect(toIsoDate(null)).toBeNull();
    const d = fromIsoDate('2026-12-31')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 11, 31]);
    expect(fromIsoDate('')).toBeNull();
  });

  it('formats quantities with up to three decimals', () => {
    expect(qty(12)).toBe('12');
    expect(qty(1.5)).toBe('1.5');
    expect(qty(0.1234)).toBe('0.123');
    expect(qty(null)).toBe('—');
  });
});
