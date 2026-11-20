import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { InvoiceReceiptDetail, InvoiceReceiptLine } from '../../../interfaces/invoice';
import { InvoiceService } from '../../../services/invoice.service';
import { errorMessage } from '../../../shared/utils/error-message';

// Datos que recibe el diálogo: la factura del historial que se muestra.
export interface InvoiceDetailData {
  receiptId: number;
}

/**
 * Detalle de una factura confirmada: cada línea con la descripción tal como vino en la factura y
 * el producto al que se asoció (marcado si se creó durante la revisión), y la descarga del archivo.
 */
@Component({
  selector: 'app-invoice-detail',
  imports: [CurrencyPipe, DatePipe, MatButtonModule, MatProgressBarModule],
  templateUrl: './invoice-detail.html',
  styleUrl: './invoice-detail.css',
})
export class InvoiceDetail implements OnInit {
  private invoiceService = inject(InvoiceService);
  private dialogRef = inject(MatDialogRef<InvoiceDetail>);
  private receiptId = inject<InvoiceDetailData>(MAT_DIALOG_DATA).receiptId;

  receipt = signal<InvoiceReceiptDetail | null>(null);
  downloading = signal(false);
  error = signal('');

  ngOnInit() {
    this.invoiceService.get(this.receiptId).subscribe({
      next: receipt => this.receipt.set(receipt),
      error: error => this.error.set(errorMessage(error)),
    });
  }

  lineTotal(line: InvoiceReceiptLine) {
    return line.quantity * line.unitPrice;
  }

  download() {
    const receipt = this.receipt();
    if (!receipt) return;

    this.downloading.set(true);
    this.error.set('');

    this.invoiceService.file(receipt.id).subscribe({
      next: blob => {
        this.downloading.set(false);
        saveFile(blob, receipt.fileName);
      },
      error: error => {
        this.downloading.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  close() {
    this.dialogRef.close();
  }
}

// Guarda el archivo con su nombre original: un enlace temporal a la copia local que se libera al usarse.
function saveFile(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}
