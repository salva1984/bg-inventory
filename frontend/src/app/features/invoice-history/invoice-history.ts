import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { dialogSize } from '../../core/constants/ui';
import { InvoiceReceipt } from '../../interfaces/invoice';
import { Page } from '../../interfaces/pagination';
import { AuthService } from '../../services/auth.service';
import { InvoiceService } from '../../services/invoice.service';
import { Pagination } from '../../shared/components/pagination/pagination';
import { errorMessage } from '../../shared/utils/error-message';
import { InvoiceDetail, InvoiceDetailData } from './invoice-detail/invoice-detail';

/**
 * Historial de facturas confirmadas, la más reciente primero: cuándo y quién la registró, y desde
 * el detalle, a qué producto se asoció cada línea y el archivo original.
 */
@Component({
  selector: 'app-invoice-history',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    Pagination,
    RouterLink,
  ],
  templateUrl: './invoice-history.html',
})
export class InvoiceHistory implements OnInit, OnDestroy {
  private invoiceService = inject(InvoiceService);
  private dialog = inject(MatDialog);

  // La API solo responde a administradores; los demás ven un aviso en lugar de un error.
  isAdmin = inject(AuthService).isAdmin;

  // Filtro y página actual.
  search = signal('');
  page = signal(1);

  receipts = signal<Page<InvoiceReceipt> | null>(null);
  loading = signal(false);
  error = signal('');

  // Petición en curso: se cancela al pedir otra para que una respuesta lenta no pise a la nueva.
  private request?: Subscription;

  ngOnInit() {
    if (this.isAdmin) this.load();
  }

  ngOnDestroy() {
    this.request?.unsubscribe();
  }

  load() {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    this.request = this.invoiceService.list(this.page(), this.search().trim()).subscribe({
      next: receipts => {
        this.receipts.set(receipts);
        this.loading.set(false);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  // Al cambiar el filtro se vuelve a la primera página.
  applyFilters() {
    this.page.set(1);
    this.load();
  }

  goToPage(page: number) {
    this.page.set(page);
    this.load();
  }

  openDetail(receipt: InvoiceReceipt) {
    this.dialog.open<InvoiceDetail, InvoiceDetailData>(InvoiceDetail, {
      ...dialogSize,
      // Las líneas tienen varias columnas; el diálogo de edición quedaría angosto.
      width: '1000px',
      ariaLabelledBy: 'dialog-title',
      data: { receiptId: receipt.id },
    });
  }
}
