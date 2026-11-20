import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription } from 'rxjs';
import { dialogSize } from '../../core/constants/ui';
import { Page } from '../../interfaces/pagination';
import { Supplier } from '../../interfaces/supplier';
import { AuthService } from '../../services/auth.service';
import { SupplierService } from '../../services/supplier.service';
import { Pagination } from '../../shared/components/pagination/pagination';
import { errorMessage } from '../../shared/utils/error-message';
import { SupplierForm, SupplierFormData } from './supplier-form/supplier-form';

/** Listado de proveedores con búsqueda y paginación en el servidor. */
@Component({
  selector: 'app-suppliers',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    Pagination,
  ],
  templateUrl: './suppliers.html',
})
export class Suppliers implements OnInit, OnDestroy {
  private supplierService = inject(SupplierService);
  private dialog = inject(MatDialog);

  isAdmin = inject(AuthService).isAdmin;

  // Filtro y página actual.
  search = signal('');
  page = signal(1);

  suppliers = signal<Page<Supplier> | null>(null);
  loading = signal(false);
  busy = signal(false);
  error = signal('');
  notice = signal('');

  // Petición en curso: se cancela al pedir otra para que una respuesta lenta no pise a la nueva.
  private request?: Subscription;

  ngOnInit() {
    this.load();
  }

  ngOnDestroy() {
    this.request?.unsubscribe();
  }

  load() {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    this.request = this.supplierService.list(this.page(), this.search().trim()).subscribe({
      next: suppliers => {
        this.suppliers.set(suppliers);
        this.loading.set(false);
      },
      error: error => this.fail(error),
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

  // El formulario guarda por su cuenta y devuelve `true` al cerrarse si hubo cambios.
  openForm(supplier?: Supplier) {
    const dialogRef = this.dialog.open<SupplierForm, SupplierFormData, boolean>(SupplierForm, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data: { supplier },
    });

    dialogRef.afterClosed().subscribe(saved => {
      if (!saved) return;
      this.notice.set('Proveedor guardado.');
      this.load();
    });
  }

  remove(supplier: Supplier) {
    const confirmed = confirm(`¿Eliminar «${supplier.name}»?`);
    if (!confirmed) return;

    this.busy.set(true);
    this.error.set('');
    this.supplierService.delete(supplier.id).subscribe({
      next: () => this.afterRemove(),
      error: error => this.fail(error),
    });
  }

  private afterRemove() {
    this.busy.set(false);
    this.notice.set('Registro eliminado.');

    // Si se borró lo último de la página, se retrocede una.
    const removedLastOfPage = this.suppliers()?.items.length === 1 && this.page() > 1;
    if (removedLastOfPage) this.page.update(page => page - 1);

    this.load();
  }

  private fail(error: unknown) {
    this.busy.set(false);
    this.loading.set(false);
    this.error.set(errorMessage(error));
  }
}
