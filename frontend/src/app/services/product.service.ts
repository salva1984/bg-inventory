import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { pageSize } from '../core/constants/ui';
import { Page } from '../interfaces/pagination';
import { PriceMatrixFilters, PriceMatrixResponse } from '../interfaces/price-matrix';
import { Product, ProductDetail, ProductFilters, ProductInput } from '../interfaces/product';

/** Productos, sus lotes y la matriz de precios. */
@Injectable({ providedIn: 'root' })
export class ProductService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/products`;

  // Filtrar y ordenar ocurre en el servidor; los filtros vacíos no se envían.
  list(filters: ProductFilters) {
    let params = new HttpParams()
      .set('page', filters.page)
      .set('pageSize', pageSize)
      .set('sortBy', filters.sortBy)
      .set('descending', filters.descending);

    if (filters.search) params = params.set('search', filters.search);
    if (filters.supplierId) params = params.set('supplierId', filters.supplierId);

    return this.http.get<Page<Product>>(this.url, { params });
  }

  getById(id: number) {
    return this.http.get<ProductDetail>(`${this.url}/${id}`);
  }

  // Crear y editar devuelven el producto guardado, con su id y sus lotes.
  create(product: ProductInput) {
    return this.http.post<ProductDetail>(this.url, product);
  }

  // La API reemplaza la colección de lotes, por eso cada lote existente debe llevar su id.
  update(id: number, product: ProductInput) {
    return this.http.put<ProductDetail>(`${this.url}/${id}`, product);
  }

  delete(id: number) {
    return this.http.delete(`${this.url}/${id}`);
  }

  // Salida rápida: resta unidades de un lote y devuelve el producto con el stock nuevo.
  withdrawStock(productId: number, lotId: number, quantity: number) {
    const url = `${this.url}/${productId}/lots/${lotId}/withdraw`;
    return this.http.post<ProductDetail>(url, { quantity });
  }

  getPriceMatrix(filters: PriceMatrixFilters) {
    let params = new HttpParams()
      .set('page', filters.page)
      .set('pageSize', pageSize)
      .set('descending', filters.descending);

    if (filters.search) params = params.set('search', filters.search);
    if (filters.supplierId) params = params.set('supplierId', filters.supplierId);

    return this.http.get<PriceMatrixResponse>(`${this.url}/price-matrix`, { params });
  }
}
