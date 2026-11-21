import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { wholeNumberPattern } from '../../../core/constants/validation';
import { Lot, ProductDetail } from '../../../interfaces/product';
import { ProductService } from '../../../services/product.service';
import { errorMessage } from '../../../shared/utils/error-message';

// Producto del que se sacan unidades.
export interface StockWithdrawalData {
  productId: number;
}

/**
 * Diálogo de salida rápida de inventario: elige un lote y la cantidad a sacar.
 * Se cierra con el producto actualizado, o sin valor si se canceló.
 */
@Component({
  selector: 'app-stock-withdrawal',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './stock-withdrawal.html',
})
export class StockWithdrawal implements OnInit {
  private productService = inject(ProductService);
  private dialogRef = inject<MatDialogRef<StockWithdrawal, ProductDetail>>(MatDialogRef);
  private data = inject<StockWithdrawalData>(MAT_DIALOG_DATA);

  product = signal<ProductDetail | null>(null);
  // Solo se puede sacar de lotes que tienen existencias.
  lotsWithStock = computed(() => this.product()?.lots.filter(lot => lot.stock > 0) ?? []);
  lotId = signal<number | null>(null);
  quantity = signal(1);
  busy = signal(false);
  error = signal('');

  // La cantidad no puede superar lo que hay en el lote elegido.
  selectedLot = computed(() => this.lotsWithStock().find(lot => lot.id === this.lotId()) ?? null);
  maxQuantity = computed(() => this.selectedLot()?.stock ?? 0);

  readonly wholeNumberPattern = wholeNumberPattern;

  ngOnInit() {
    this.busy.set(true);
    this.productService.getById(this.data.productId).subscribe({
      next: product => this.showProduct(product),
      error: error => this.fail(error),
    });
  }

  private showProduct(product: ProductDetail) {
    this.product.set(product);
    this.busy.set(false);

    // Con un único lote no hay nada que elegir: se deja seleccionado.
    const lots = this.lotsWithStock();
    if (lots.length === 1) this.lotId.set(lots[0].id);
  }

  // Texto de cada opción: el usuario reconoce el lote por proveedor, número y lo que queda.
  describeLot(lot: Lot) {
    return `${lot.supplierName} · ${lot.lotNumber} · ${lot.stock} disponibles`;
  }

  withdraw() {
    const lotId = this.lotId();
    if (lotId === null) return;

    this.busy.set(true);
    this.error.set('');
    this.productService.withdrawStock(this.data.productId, lotId, Number(this.quantity())).subscribe({
      next: updated => this.dialogRef.close(updated),
      error: error => this.fail(error),
    });
  }

  close() {
    this.dialogRef.close();
  }

  private fail(error: unknown) {
    this.busy.set(false);
    this.error.set(errorMessage(error));
  }
}
