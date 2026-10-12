import { HttpErrorResponse } from '@angular/common/http';
import { ApiError } from '@wh/shared';

/** Requests with responseType 'blob' return their error body as a Blob; this reads it into an ApiError. */
export async function readBlobError(error: unknown): Promise<ApiError> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    try {
      const body = JSON.parse(await error.error.text());
      return ApiError.from(new HttpErrorResponse({ error: body, status: error.status, statusText: error.statusText, url: error.url ?? undefined }));
    } catch {
      /* not JSON */
    }
  }
  return ApiError.from(error);
}
