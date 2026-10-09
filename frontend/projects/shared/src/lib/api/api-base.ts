import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/tokens';

/** Small promise-based wrapper; errors arrive as ApiError via the interceptors. */
export abstract class ApiBase {
  protected readonly http = inject(HttpClient);
  protected readonly base = inject(API_BASE_URL);

  protected get<T>(path: string, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.get<T>(`${this.base}${path}`, { params }));
  }

  protected post<T>(path: string, body: unknown = {}, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.post<T>(`${this.base}${path}`, body, { params }));
  }

  protected put<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(this.http.put<T>(`${this.base}${path}`, body));
  }

  protected delete<T>(path: string, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.delete<T>(`${this.base}${path}`, { params }));
  }

  protected version(version: number): HttpParams {
    return new HttpParams().set('version', String(version));
  }
}
