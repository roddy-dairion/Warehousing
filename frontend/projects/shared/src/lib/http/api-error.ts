import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 ProblemDetails as returned by the API. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Machine-readable error code, e.g. `dispatch.invalid_transition`. */
  code?: string;
  traceId?: string;
  /** Validation errors keyed by field name (camelCase). */
  errors?: Record<string, string[]>;
}

export type ApiErrorKind = 'network' | 'unauthorized' | 'forbidden' | 'not-found' | 'conflict' | 'validation' | 'server' | 'other';

/** Normalised error thrown by every API call. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly kind: ApiErrorKind,
    readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? 'Request failed');
    this.name = 'ApiError';
  }

  /** Validation errors by field. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }

  /** A sentence suitable for a toast. */
  get userMessage(): string {
    switch (this.kind) {
      case 'network':
        return 'Cannot reach the server. Check your connection and try again. Nothing was saved.';
      case 'unauthorized':
        return 'Your session has expired. Please sign in again.';
      case 'forbidden':
        return 'You do not have permission to do this.';
      case 'not-found':
        return this.problem.detail ?? 'The requested item was not found.';
      case 'server':
        return 'The server had a problem processing the request. Please try again later.';
      default:
        return this.problem.detail ?? this.problem.title ?? 'The request could not be completed.';
    }
  }

  static from(error: unknown): ApiError {
    if (error instanceof ApiError) return error;
    if (error instanceof HttpErrorResponse) {
      const status = error.status;
      const body = error.error;
      const problem: ProblemDetails =
        body && typeof body === 'object' && !(body instanceof Blob) && !(body instanceof ArrayBuffer)
          ? (body as ProblemDetails)
          : { title: error.statusText || undefined, detail: typeof body === 'string' && body ? body : undefined };
      return new ApiError(status, ApiError.kindFor(status, problem), { ...problem, status });
    }
    return new ApiError(0, 'other', { title: error instanceof Error ? error.message : 'Unexpected error' });
  }

  private static kindFor(status: number, problem: ProblemDetails): ApiErrorKind {
    if (status === 0) return 'network';
    if (status === 401) return 'unauthorized';
    if (status === 403) return 'forbidden';
    if (status === 404) return 'not-found';
    if (status === 409) return 'conflict';
    if (status === 400 || status === 422) return problem.errors ? 'validation' : 'other';
    if (status >= 500) return 'server';
    return 'other';
  }
}
