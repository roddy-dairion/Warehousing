import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage } from './feedback';

describe('errorMessage', () => {
  it('tells the user a retry is safe for idempotent stock actions after a network failure', () => {
    const offline = new HttpErrorResponse({ status: 0 });
    expect(errorMessage(offline, { retrySafe: true })).toContain('will not be counted twice');
    expect(errorMessage(offline)).toContain('Check Wi-Fi');
    expect(errorMessage(offline)).not.toContain('counted twice');
  });

  it("shows the server's explanation for business rule errors", () => {
    const err = new HttpErrorResponse({ status: 422, error: { detail: 'SOAP-100 lot L9 in bin A-03 is not allocated to this dispatch.', code: 'pick.not_allocated' } });
    expect(errorMessage(err)).toBe('SOAP-100 lot L9 in bin A-03 is not allocated to this dispatch.');
  });
});
