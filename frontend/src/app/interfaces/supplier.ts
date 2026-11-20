export interface Supplier {
  id: number;
  name: string;
  contactEmail: string | null;
  phone: string | null;
  productCount: number;
}

// Cuerpo para crear o editar un proveedor.
export interface SupplierInput {
  name: string;
  contactEmail: string | null;
  phone: string | null;
}

// Par id + nombre, usado para llenar listas desplegables.
export interface SupplierOption {
  id: number;
  name: string;
}
