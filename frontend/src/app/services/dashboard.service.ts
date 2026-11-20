import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { DashboardSummary } from '../interfaces/dashboard';

/** Resumen del inventario para la página de inicio. */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/dashboard`;

  summary() {
    return this.http.get<DashboardSummary>(this.url);
  }
}
