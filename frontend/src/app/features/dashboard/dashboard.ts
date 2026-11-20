import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { DashboardSummary } from '../../interfaces/dashboard';
import { DashboardService } from '../../services/dashboard.service';
import { errorMessage } from '../../shared/utils/error-message';

/**
 * Página de inicio: cifras del inventario, valor por proveedor, productos con menos existencias
 * y productos cuyo precio más varía entre proveedores o lotes.
 */
@Component({
  selector: 'app-dashboard',
  imports: [CurrencyPipe, DecimalPipe, MatProgressBarModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard implements OnInit {
  private dashboardService = inject(DashboardService);

  summary = signal<DashboardSummary | null>(null);
  loading = signal(false);
  error = signal('');

  // Las barras se miden contra el proveedor de mayor valor, que ocupa todo el ancho.
  private highestSupplierValue = computed(() => {
    const values = this.summary()?.valueBySupplier.map(supplier => supplier.inventoryValue) ?? [];
    return Math.max(0, ...values);
  });

  ngOnInit() {
    this.loading.set(true);
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
}
