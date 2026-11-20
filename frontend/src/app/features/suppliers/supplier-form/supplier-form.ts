import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { notBlankPattern } from '../../../core/constants/validation';
import { Supplier, SupplierInput } from '../../../interfaces/supplier';
import { SupplierService } from '../../../services/supplier.service';
import { errorMessage } from '../../../shared/utils/error-message';

// Datos que recibe el diálogo: sin proveedor se crea uno nuevo.
export interface SupplierFormData {
  supplier?: Supplier;
}

/** Diálogo para crear o editar un proveedor. Se cierra con `true` cuando guardó cambios. */
@Component({
  selector: 'app-supplier-form',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './supplier-form.html',
})
export class SupplierForm {
  private supplierService = inject(SupplierService);
  private dialogRef = inject<MatDialogRef<SupplierForm, boolean>>(MatDialogRef);
  private existing = inject<SupplierFormData>(MAT_DIALOG_DATA).supplier;

  isEditing = this.existing !== undefined;
  name = signal(this.existing?.name ?? '');
  contactEmail = signal(this.existing?.contactEmail ?? '');
  phone = signal(this.existing?.phone ?? '');
  busy = signal(false);
  error = signal('');

  // Patrón expuesto al template para rechazar un nombre de solo espacios.
  readonly notBlankPattern = notBlankPattern;

  save() {
    this.busy.set(true);
    this.error.set('');

    // Los campos opcionales vacíos se envían como null.
    const input: SupplierInput = {
      name: this.name(),
      contactEmail: this.contactEmail().trim() || null,
      phone: this.phone().trim() || null,
    };
    const request = this.existing === undefined
      ? this.supplierService.create(input)
      : this.supplierService.update(this.existing.id, input);

    request.subscribe({
      next: () => this.dialogRef.close(true),
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }

  close() {
    this.dialogRef.close(false);
  }
}
