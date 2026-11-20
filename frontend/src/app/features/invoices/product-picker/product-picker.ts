import { PercentPipe } from '@angular/common';
import { Component, DestroyRef, inject, input, model, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { debounceTime, distinctUntilChanged, map, of, Subject, switchMap } from 'rxjs';
import { dialogSize } from '../../../core/constants/ui';
import { productNameMaxLength } from '../../../core/constants/validation';
import { ProductCandidate } from '../../../interfaces/invoice';
import { ProductDetail } from '../../../interfaces/product';
import { ProductService } from '../../../services/product.service';
import { ProductForm, ProductFormData } from '../../products/product-form/product-form';

// Producto elegible: un candidato del análisis (con puntuación) o uno buscado a mano (sin ella).
// `created` marca el producto que se creó desde esta línea, para que el historial lo muestre.
export type ProductOption = Omit<ProductCandidate, 'score'> & { score: number | null; created?: boolean };

// Espera tras la última tecla antes de buscar, para no consultar la API en cada letra.
const searchDelayMs = 300;

// Con menos letras la búsqueda trae casi todo el catálogo; se siguen mostrando los candidatos.
const minSearchLength = 2;

/**
 * Campo para elegir el producto de una línea de factura. Sin escribir nada ofrece los candidatos
 * del análisis; al escribir busca por nombre o código en todo el catálogo, para las líneas que
 * el sistema no pudo identificar. Si el producto no existe, se crea desde aquí y queda elegido.
 */
@Component({
  selector: 'app-product-picker',
  imports: [FormsModule, MatAutocompleteModule, MatFormFieldModule, MatInputModule, PercentPipe],
  templateUrl: './product-picker.html',
  styleUrl: './product-picker.css',
})
export class ProductPicker implements OnInit {
  private productService = inject(ProductService);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);

  candidates = input<ProductCandidate[]>([]);
  disabled = input(false);
  // Nombre propuesto para un producto nuevo: la descripción de la línea, que el usuario corrige.
  newProductName = input('');
  // Producto elegido; el padre lo lee y lo escribe con [(product)].
  product = model<ProductOption | null>(null);

  // Lo que muestra el campo: el texto que se escribe o el producto elegido.
  text: string | ProductOption | null = null;
  options = signal<ProductOption[]>([]);
  private queries = new Subject<string>();

  ngOnInit() {
    this.text = this.product();
    this.options.set(this.candidates());

    // Cada búsqueda nueva cancela la anterior (switchMap): una respuesta lenta no pisa a la última.
    this.queries.pipe(
      debounceTime(searchDelayMs),
      distinctUntilChanged(),
      switchMap(query => this.search(query)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(options => this.options.set(options));
  }

  // Escribir borra la elección anterior: el producto vuelve a ser obligatorio hasta elegir otro.
  onTextChange(value: string | ProductOption | null) {
    this.text = value;
    if (typeof value === 'string') {
      this.product.set(null);
      this.queries.next(value.trim());
    }
  }

  choose(option: ProductOption) {
    this.text = option;
    this.product.set(option);
  }

  /**
   * Abre el formulario de producto con el nombre de la línea y sin lotes: el stock lo registra
   * la factura al confirmarse. Al guardar, el producto nuevo queda elegido para esta línea.
   */
  createProduct() {
    const data: ProductFormData = {
      initialName: this.newProductName().slice(0, productNameMaxLength),
      withoutLots: true,
    };
    const dialogRef = this.dialog.open<ProductForm, ProductFormData, ProductDetail>(ProductForm, {
      ...dialogSize,
      ariaLabelledBy: 'dialog-title',
      data,
    });

    dialogRef.afterClosed().subscribe(created => {
      if (!created) return;
      this.choose({ productId: created.id, code: created.code, name: created.name, score: null, created: true });
    });
  }

  label(option: ProductOption | string | null) {
    if (!option) return '';
    if (typeof option === 'string') return option;
    return `${option.code} · ${option.name}`;
  }

  private search(query: string) {
    const isTooShort = query.length < minSearchLength;
    if (isTooShort) return of(this.candidates());

    const filters = { page: 1, search: query, supplierId: '', sortBy: 'Name', descending: false };
    return this.productService.list(filters).pipe(
      map(page => page.items.map(item => ({ productId: item.id, code: item.code, name: item.name, score: null }))),
    );
  }
}
