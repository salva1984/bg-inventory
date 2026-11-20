import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { dialogSize } from '../../core/constants/ui';
import { DashboardSummary } from '../../interfaces/dashboard';
import { AuthService } from '../../services/auth.service';
import { DashboardService } from '../../services/dashboard.service';
import { errorMessage } from '../../shared/utils/error-message';
import { ProductDetail } from '../../interfaces/product';
import { ProductForm, ProductFormData } from '../products/product-form/product-form';

/**
 * Página de inicio: cifras del inventario, valor por proveedor, productos con menos existencias
 * y productos cuyo precio más varía entre proveedores o lotes.
 */
@Component({
  selector: 'app-dashboard',
  imports: [CurrencyPipe, DecimalPipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard implements OnInit {
  private dashboardService = inject(DashboardService);
  private dialog = inject(MatDialog);

  isAdmin = inject(AuthService).isAdmin;

  summary = signal<DashboardSummary | null>(null);
  loading = signal(false);
  error = signal('');

  // Las barras se miden contra el proveedor de mayor valor, que ocupa todo el ancho.
  private highestSupplierValue = computed(() => {
    const values = this.summary()?.valueBySupplier.map(supplier => supplier.inventoryValue) ?? [];
    return Math.max(0, ...values);
  });

  ngOnInit() {
    this.load();
  }

  private load() {
    this.loading.set(true);
    this.error.set('');
    this.dashboardService.summary().subscribe({
      next: summary => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  // Ancho de la barra de un proveedor, en porcentaje del de mayor valor.
  barWidth(inventoryValue: number) {
    const highest = this.highestSupplierValue();
    if (highest === 0) return 0;
    return (inventoryValue / highest) * 100;
  }

  // Abre el formulario del producto, que incluye sus lotes; si se guardó algo, se recalcula el resumen.
  openForm(productId: number) {
    const dialogRef = this.dialog.open<ProductForm, ProductFormData, ProductDetail>(ProductForm, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data: { id: productId },
    });

    dialogRef.afterClosed().subscribe(saved => {
      if (saved) this.load();
    });
  }
}
