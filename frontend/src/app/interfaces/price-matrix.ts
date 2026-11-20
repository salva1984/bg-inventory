import { Page } from './pagination';
import { SupplierOption } from './supplier';

// Cruce de un producto con un proveedor.
export interface PriceMatrixCell {
  price: number;
  stock: number;
  lots: number;
}

export interface PriceMatrixRow {
  productId: number;
  code: string;
  name: string;
  // Clave: id del proveedor.
  cells: Record<string, PriceMatrixCell>;
}

export interface PriceMatrixResponse {
  suppliers: SupplierOption[];
  products: Page<PriceMatrixRow>;
}

export interface PriceMatrixFilters {
  page: number;
  search: string;
  supplierId: string;
  // La matriz solo se ordena por nombre de producto.
  descending: boolean;
}
