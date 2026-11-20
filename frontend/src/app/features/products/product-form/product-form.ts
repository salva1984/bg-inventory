import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import {
  maxPrice,
  maxStock,
  minPrice,
  notBlankPattern,
  pricePattern,
  wholeNumberPattern,
} from '../../../core/constants/validation';
import { Lot, ProductDetail, ProductInput } from '../../../interfaces/product';
import { SupplierOption } from '../../../interfaces/supplier';
import { AuthService } from '../../../services/auth.service';
import { ProductService } from '../../../services/product.service';
import { SupplierService } from '../../../services/supplier.service';
import { errorMessage } from '../../../shared/utils/error-message';

// Datos que recibe el diálogo: sin `id` se crea un producto nuevo.
export interface ProductFormData {
  id?: number;
  // Nombre con el que empieza un producto nuevo (por ejemplo, la descripción de una línea de factura).
  initialName?: string;
  // Al crear desde una factura no se piden lotes: el stock lo registra la confirmación de la factura.
  withoutLots?: boolean;
}

/**
 * Diálogo para crear, editar o consultar un producto con sus lotes.
 * Un usuario sin rol de administrador lo ve en solo lectura.
 * Se cierra con el producto guardado, o sin valor si se canceló.
 */
@Component({
  selector: 'app-product-form',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './product-form.html',
  styleUrl: './product-form.css',
})
export class ProductForm implements OnInit {
  private productService = inject(ProductService);
  private supplierService = inject(SupplierService);
  private dialogRef = inject<MatDialogRef<ProductForm, ProductDetail>>(MatDialogRef);
  private data = inject<ProductFormData>(MAT_DIALOG_DATA);
  private id = this.data.id;

  isAdmin = inject(AuthService).isAdmin;
  isEditing = this.id !== undefined;
  suppliers = signal<SupplierOption[]>([]);
  code = signal('');
  name = signal(this.data.initialName ?? '');
  withoutLots = this.data.withoutLots ?? false;
  description = signal('');
  // Los campos de cada lote se editan directamente sobre el objeto con ngModel;
  // el signal cambia solo cuando se agrega, quita o carga la lista completa.
  lots = signal<Lot[]>([]);
  busy = signal(false);
  error = signal('');

  // Reglas de validación expuestas al template; las comprueban los validadores de cada campo.
  readonly notBlankPattern = notBlankPattern;
  readonly pricePattern = pricePattern;
  readonly wholeNumberPattern = wholeNumberPattern;
  readonly minPrice = minPrice;
  readonly maxPrice = maxPrice;
  readonly maxStock = maxStock;

  get title() {
    if (!this.isEditing) return 'Nuevo producto';
    if (this.isAdmin) return 'Editar producto';
    return 'Detalle del producto';
  }

  // Se puede editar si es administrador y no hay una carga o un guardado en curso.
  get canEdit() {
    return this.isAdmin && !this.busy();
  }

  ngOnInit() {
    this.loadSuppliers();
    if (this.id !== undefined) this.loadProduct(this.id);
  }

  private loadSuppliers() {
    this.supplierService.options().subscribe({
      next: suppliers => this.suppliers.set(suppliers),
      error: error => this.fail(error),
    });
  }

  // Al editar, trae el producto con sus lotes para llenar el formulario.
  private loadProduct(id: number) {
    this.busy.set(true);
    this.productService.getById(id).subscribe({
      next: detail => {
        this.code.set(detail.code);
        this.name.set(detail.name);
        this.description.set(detail.description ?? '');
        this.lots.set(detail.lots);
        this.busy.set(false);
      },
      error: error => this.fail(error),
    });
  }

  addLot() {
    // Sin proveedor elegido (null) para que el campo obligatorio lo marque como pendiente.
    const newLot: Lot = { id: null, supplierId: null, lotNumber: '', price: minPrice, stock: 0 };
    this.lots.update(lots => [...lots, newLot]);
  }

  removeLot(index: number) {
    this.lots.update(lots => lots.filter((_, position) => position !== index));
  }

  save() {
    this.busy.set(true);
    this.error.set('');

    const input = this.toInput();
    const request = this.id === undefined
      ? this.productService.create(input)
      : this.productService.update(this.id, input);

    request.subscribe({
      next: saved => this.dialogRef.close(saved),
      error: error => this.fail(error),
    });
  }

  close() {
    this.dialogRef.close();
  }

  // Limpia el formulario para enviarlo: descripción vacía como null y números como números.
  private toInput(): ProductInput {
    return {
      code: this.code(),
      name: this.name(),
      description: this.description().trim() || null,
      lots: this.lots().map(lot => ({
        id: lot.id,
        supplierId: Number(lot.supplierId),
        lotNumber: lot.lotNumber.trim(),
        price: Number(lot.price),
        stock: Number(lot.stock),
      })),
    };
  }

  private fail(error: unknown) {
    this.busy.set(false);
    this.error.set(errorMessage(error));
  }
}
