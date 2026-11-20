import { CurrencyPipe } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { Subscription } from 'rxjs';
import { dialogSize } from '../../core/constants/ui';
import { PriceMatrixResponse, PriceMatrixRow } from '../../interfaces/price-matrix';
import { SupplierOption } from '../../interfaces/supplier';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { SupplierService } from '../../services/supplier.service';
import { Pagination } from '../../shared/components/pagination/pagination';
import { errorMessage } from '../../shared/utils/error-message';
import { ProductDetail } from '../../interfaces/product';
import { ProductForm, ProductFormData } from '../products/product-form/product-form';

/** Matriz producto × proveedor con el precio mínimo y las existencias de cada cruce. */
@Component({
  selector: 'app-price-matrix',
  imports: [
    CurrencyPipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSortModule,
    Pagination,
  ],
  templateUrl: './price-matrix.html',
  styleUrl: './price-matrix.css',
})
export class PriceMatrix implements OnInit, OnDestroy {
  private productService = inject(ProductService);
  private supplierService = inject(SupplierService);
  private dialog = inject(MatDialog);

  isAdmin = inject(AuthService).isAdmin;

  // Filtros y página actual.
  search = signal('');
  supplierFilter = signal('');
  descending = signal(false);
  page = signal(1);

  matrix = signal<PriceMatrixResponse | null>(null);
  suppliers = signal<SupplierOption[]>([]);
  loading = signal(false);
  error = signal('');
  notice = signal('');

  // Petición en curso
  private request?: Subscription;

  ngOnInit() {
    this.load();
    this.loadSuppliers();
  }

  ngOnDestroy() {
    this.request?.unsubscribe();
  }

  load() {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    const filters = {
      page: this.page(),
      search: this.search().trim(),
      supplierId: this.supplierFilter(),
      descending: this.descending(),
    };
    
    this.request = this.productService.getPriceMatrix(filters).subscribe({
      next: matrix => {
        this.matrix.set(matrix);
        this.loading.set(false);
      },
      error: error => this.fail(error),
    });
  }

  private loadSuppliers() {
    this.supplierService.options().subscribe({
      next: suppliers => this.suppliers.set(suppliers),
      error: error => this.fail(error),
    });
  }

  // Precio más bajo de la fila. Con un solo proveedor no hay con qué comparar, así que no se resalta nada.
  lowestPrice(row: PriceMatrixRow): number | null {
    const prices = Object.values(row.cells).map(cell => cell.price);
    const hasComparison = prices.length >= 2;
    return hasComparison ? Math.min(...prices) : null;
  }

  applyFilters() {
    this.page.set(1);
    this.load();
  }

  // Clic en la cabecera "Producto": `matSortDisableClear` garantiza que siempre hay dirección.
  changeSort(sort: Sort) {
    this.descending.set(sort.direction === 'desc');
    this.applyFilters();
  }

  goToPage(page: number) {
    this.page.set(page);
    this.load();
  }

  // Abre el mismo formulario que el listado; si se guardó algo, se recarga la matriz.
  openForm(productId: number) {
    const dialogRef = this.dialog.open<ProductForm, ProductFormData, ProductDetail>(ProductForm, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data: { id: productId },
    });

    dialogRef.afterClosed().subscribe(saved => {
      if (!saved) return;
      this.notice.set('Producto guardado.');
      this.load();
    });
  }

  private fail(error: unknown) {
    this.loading.set(false);
    this.error.set(errorMessage(error));
  }
}
