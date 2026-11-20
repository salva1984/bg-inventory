// Fila del listado de productos; los totales y precios resumen todos sus lotes.
export interface Product {
  id: number;
  code: string;
  name: string;
  description: string | null;
  supplierCount: number;
  totalStock: number;
  minPrice: number | null;
  maxPrice: number | null;
  updatedAt: string;
}

// Un lote es el registro de inventario de un producto con un proveedor.
export interface Lot {
  id: number | null;
  supplierId: number;
  supplierName?: string;
  lotNumber: string;
  price: number;
  stock: number;
}

export interface ProductDetail {
  id: number;
  code: string;
  name: string;
  description: string | null;
  lots: Lot[];
}

// Cuerpo para crear o editar un producto con sus lotes.
export interface ProductInput {
  code: string;
  name: string;
  description: string | null;
  lots: Lot[];
}

// Filtros del listado de productos.
export interface ProductFilters {
  page: number;
  search: string;
  supplierId: string;
  sortBy: string;
  descending: boolean;
}
