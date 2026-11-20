import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { pageSize } from '../core/constants/ui';
import { Page } from '../interfaces/pagination';
import { Supplier, SupplierInput, SupplierOption } from '../interfaces/supplier';

/** Proveedores. */
@Injectable({ providedIn: 'root' })
export class SupplierService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/suppliers`;

  list(page: number, search: string) {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);

    if (search) params = params.set('search', search);

    return this.http.get<Page<Supplier>>(this.url, { params });
  }

  // Todos los proveedores (id y nombre) para filtros y selects.
  options() {
    return this.http.get<SupplierOption[]>(`${this.url}/lookup`);
  }

  create(supplier: SupplierInput) {
    return this.http.post(this.url, supplier);
  }

  update(id: number, supplier: SupplierInput) {
    return this.http.put(`${this.url}/${id}`, supplier);
  }

  delete(id: number) {
    return this.http.delete(`${this.url}/${id}`);
  }
}
