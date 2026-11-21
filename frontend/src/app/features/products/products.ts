import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { Subscription } from 'rxjs';
import { dialogSize } from '../../core/constants/ui';
import { Page } from '../../interfaces/pagination';
import { Product, ProductDetail } from '../../interfaces/product';
import { SupplierOption } from '../../interfaces/supplier';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { SupplierService } from '../../services/supplier.service';
import { Pagination } from '../../shared/components/pagination/pagination';
import { errorMessage } from '../../shared/utils/error-message';
import { ProductForm, ProductFormData } from './product-form/product-form';
import { StockWithdrawal, StockWithdrawalData } from './stock-withdrawal/stock-withdrawal';

/** Listado de productos con búsqueda, filtro por proveedor, orden y paginación en el servidor. */
@Component({
  selector: 'app-products',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSortModule,
    Pagination,
  ],
  templateUrl: './products.html',
})
export class Products implements OnInit, OnDestroy {
  private productService = inject(ProductService);
  private supplierService = inject(SupplierService);
  private dialog = inject(MatDialog);

  isAdmin = inject(AuthService).isAdmin;

  // Filtros y página actual.
  search = signal('');
  supplierFilter = signal('');
  sortBy = signal('Name');
  descending = signal(false);
  page = signal(1);

  products = signal<Page<Product> | null>(null);
  suppliers = signal<SupplierOption[]>([]);
  loading = signal(false);
  busy = signal(false);
  error = signal('');
  notice = signal('');

  // Petición en curso: se cancela al pedir otra para que una respuesta lenta no pise a la nueva.
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
      sortBy: this.sortBy(),
      descending: this.descending(),
    };
    this.request = this.productService.list(filters).subscribe({
      next: products => {
        this.products.set(products);
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

  // Al cambiar un filtro se vuelve a la primera página.
  applyFilters() {
    this.page.set(1);
    this.load();
  }

  // Clic en una cabecera ordenable: `matSortDisableClear` garantiza que siempre hay dirección.
  changeSort(sort: Sort) {
    this.sortBy.set(sort.active);
    this.descending.set(sort.direction === 'desc');
    this.applyFilters();
  }

  goToPage(page: number) {
    this.page.set(page);
    this.load();
  }

  // El formulario guarda por su cuenta y, si hubo cambios, devuelve el producto guardado al cerrarse.
  openForm(id?: number) {
    const dialogRef = this.dialog.open<ProductForm, ProductFormData, ProductDetail>(ProductForm, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data: { id },
    });

    dialogRef.afterClosed().subscribe(saved => {
      if (!saved) return;
      this.notice.set('Producto guardado.');
      this.load();
    });
  }

  // Salida rápida de inventario: el diálogo descuenta por su cuenta y devuelve el producto actualizado.
  openWithdrawal(product: Product) {
    const dialogRef = this.dialog.open<StockWithdrawal, StockWithdrawalData, ProductDetail>(StockWithdrawal, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data: { productId: product.id },
    });

    dialogRef.afterClosed().subscribe(updated => {
      if (!updated) return;
      this.notice.set(`Salida registrada para «${updated.name}».`);
      this.load();
    });
  }

  remove(product: Product) {
    const confirmed = confirm(`¿Eliminar «${product.name}»? También se eliminarán sus registros de inventario.`);
    if (!confirmed) return;

    this.busy.set(true);
    this.error.set('');
    this.productService.delete(product.id).subscribe({
      next: () => this.afterRemove(),
      error: error => this.fail(error),
    });
  }

  private afterRemove() {
    this.busy.set(false);
    this.notice.set('Registro eliminado.');

    // Si se borró lo último de la página, se retrocede una.
    const removedLastOfPage = this.products()?.items.length === 1 && this.page() > 1;
    if (removedLastOfPage) this.page.update(page => page - 1);

    this.load();
  }

  private fail(error: unknown) {
    this.busy.set(false);
    this.loading.set(false);
    this.error.set(errorMessage(error));
  }
}
