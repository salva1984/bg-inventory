// Resumen del negocio que devuelve GET /api/dashboard. Todo se calcula en la base de datos.
export interface DashboardSummary {
  totals: DashboardTotals;
  valueBySupplier: SupplierInventoryValue[];
  lowestStock: LowStockProduct[];
  largestPriceSpread: PriceSpreadProduct[];
}

// Cifras principales. El valor del inventario es la suma de precio × existencias de cada lote.
export interface DashboardTotals {
  productCount: number;
  supplierCount: number;
  outOfStockProductCount: number;
  totalUnits: number;
  inventoryValue: number;
}

export interface SupplierInventoryValue {
  supplierId: number;
  supplierName: string;
  inventoryValue: number;
  units: number;
}

export interface LowStockProduct {
  id: number;
  code: string;
  name: string;
  totalStock: number;
}

// Mismo producto con distinto precio según proveedor o lote. El porcentaje es sobre el precio más bajo.
export interface PriceSpreadProduct {
  id: number;
  code: string;
  name: string;
  minPrice: number;
  maxPrice: number;
  spreadPercent: number;
}
