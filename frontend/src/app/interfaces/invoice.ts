import { SupplierOption } from './supplier';

// Recepción de mercadería desde facturas (POST /api/invoices/analyze y /confirm) y el historial
// de facturas confirmadas (GET /api/invoices). Los nombres y valores reflejan los contratos de
// BgInventario.Core/Features/Invoices.

// Qué tan seguro está el sistema del producto de una línea.
// Matched: reconocido por un código o una equivalencia ya confirmada.
// Suggested: hay un candidato parecido. NeedsReview: solo candidatos débiles.
// Manual: la descripción no alcanza; el usuario identifica el producto.
export type LineMatchStatus = 'Matched' | 'Suggested' | 'NeedsReview' | 'Manual';

// Por qué una línea quedó como Matched.
export type MatchedBy = 'SupplierItemCode' | 'ProductCode' | 'Description';

// Producto posible para una línea; score va de 0 a 1 (1 = equivalencia conocida).
export interface ProductCandidate {
  productId: number;
  code: string;
  name: string;
  score: number;
}

export interface ExtractedInvoiceLine {
  description: string;
  supplierItemCode: string | null;
  quantity: number;
  unitPrice: number;
}

export interface AnalyzedInvoiceLine {
  lineNumber: number;
  extracted: ExtractedInvoiceLine;
  match: {
    status: LineMatchStatus;
    matchedBy: MatchedBy | null;
    // El mejor primero; en Matched, el primero es el producto reconocido.
    candidates: ProductCandidate[];
  };
}

// Si el emisor impreso en la factura es el proveedor elegido. La API nunca cambia la elección:
// Matches: es el elegido. NotRead: no se pudo leer el emisor.
// OtherSupplier: es otro proveedor registrado (suggestedSupplier). NotRegistered: no está registrado.
export type IssuerCheck = 'Matches' | 'NotRead' | 'OtherSupplier' | 'NotRegistered';

// Resultado del análisis: lo que dice la factura y la propuesta por línea. Nada está guardado aún.
export interface InvoiceAnalysis {
  // El proveedor elegido; las líneas se concilian con sus equivalencias.
  supplierId: number;
  invoiceNumber: string | null;
  issueDate: string | null;
  // El emisor tal como aparece en la factura.
  supplierName: string | null;
  issuerCheck: IssuerCheck;
  suggestedSupplier: SupplierOption | null;
  subtotal: number | null;
  total: number | null;
  lines: AnalyzedInvoiceLine[];
  warnings: string[];
}

// Cuerpo de la confirmación: las líneas ya revisadas, cada una con su producto real, y el archivo
// analizado, que queda guardado en el historial junto a lo registrado.
export interface ConfirmInvoiceRequest {
  supplierId: number;
  invoiceNumber: string;
  issueDate: string | null;
  // El total con impuestos que leyó el análisis; solo se muestra en el historial.
  invoiceTotal: number | null;
  file: InvoiceFileUpload;
  lines: ConfirmInvoiceLine[];
  // El usuario aceptó volver a registrar una factura ya registrada: su stock se suma al anterior.
  registerAgain: boolean;
}

// El archivo viaja en base64 dentro del JSON; su tipo lo detecta la API por el contenido.
export interface InvoiceFileUpload {
  fileName: string;
  content: string;
}

export interface ConfirmInvoiceLine {
  productId: number;
  // La descripción tal como vino en la factura: con ella se reconocerá la próxima.
  originalDescription: string;
  supplierItemCode: string | null;
  quantity: number;
  unitPrice: number;
  // El producto se creó durante esta revisión; solo es una etiqueta del historial.
  productCreated: boolean;
}

export interface ConfirmedInvoice {
  // Registro de la factura en el historial.
  receiptId: number;
  lotNumber: string;
  productsReceived: number;
  unitsReceived: number;
  aliasesSaved: number;
}

// ---------- Historial de facturas confirmadas ----------

// Fila del historial. registeredAt es la fecha y hora de la confirmación (UTC).
export interface InvoiceReceipt {
  id: number;
  invoiceNumber: string;
  supplierName: string;
  issueDate: string | null;
  registeredAt: string;
  registeredBy: string;
  lineCount: number;
  // Suma de las líneas registradas (cantidad × precio), sin impuestos.
  subtotal: number;
  // Total con impuestos impreso en la factura, según el análisis; null si no se encontró.
  invoiceTotal: number | null;
  fileName: string;
}

// Una línea registrada: la descripción tal como vino en la factura y el producto al que se asoció.
// El código y el nombre del producto son los del momento de la confirmación.
export interface InvoiceReceiptLine {
  lineNumber: number;
  description: string;
  supplierItemCode: string | null;
  productId: number;
  productCode: string;
  productName: string;
  productCreated: boolean;
  quantity: number;
  unitPrice: number;
}

export interface InvoiceReceiptDetail extends Omit<InvoiceReceipt, 'lineCount'> {
  lines: InvoiceReceiptLine[];
}
