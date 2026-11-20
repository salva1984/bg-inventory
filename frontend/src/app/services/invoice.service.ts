import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { pageSize } from '../core/constants/ui';
import {
  ConfirmedInvoice,
  ConfirmInvoiceRequest,
  InvoiceAnalysis,
  InvoiceReceipt,
  InvoiceReceiptDetail,
} from '../interfaces/invoice';
import { Page } from '../interfaces/pagination';

/**
 * Facturas de proveedores: análisis (no guarda nada), confirmación (registra el stock y la factura)
 * y el historial de facturas confirmadas.
 */
@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private http = inject(HttpClient);
  private url = `${environment.apiUrl}/invoices`;

  // El archivo viaja como multipart; el navegador arma el Content-Type con su boundary.
  analyze(supplierId: number, file: File) {
    const form = new FormData();
    form.append('supplierId', supplierId.toString());
    form.append('file', file);

    return this.http.post<InvoiceAnalysis>(`${this.url}/analyze`, form);
  }

  confirm(request: ConfirmInvoiceRequest) {
    return this.http.post<ConfirmedInvoice>(`${this.url}/confirm`, request);
  }

  // Historial, la más reciente primero. La búsqueda mira el número de factura y el proveedor.
  list(page: number, search: string) {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);

    if (search) params = params.set('search', search);

    return this.http.get<Page<InvoiceReceipt>>(this.url, { params });
  }

  get(id: number) {
    return this.http.get<InvoiceReceiptDetail>(`${this.url}/${id}`);
  }

  // Como blob: la descarga necesita el token, así que no sirve un enlace directo a la API.
  file(id: number) {
    return this.http.get(`${this.url}/${id}/file`, { responseType: 'blob' });
  }
}
