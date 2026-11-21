// Reglas de validación que comparten los formularios. Reflejan los límites de la API
// (atributos de los contratos en BgInventario.Core) para avisar antes de enviar.
// Angular envuelve cada patrón con ^ y $, así que deben cubrir el valor completo.

// Al menos un carácter visible: un campo obligatorio no se llena solo con espacios.
export const notBlankPattern = '.*\\S.*';

// Hasta 2 decimales, como la columna decimal(18,2) donde se guarda el precio.
export const pricePattern = '\\d+(\\.\\d{1,2})?';

// Entero sin signo ni decimales, para las existencias.
export const wholeNumberPattern = '\\d+';

// Límites del precio de un lote (InventoryLotLimits en la API).
export const minPrice = 0.01;
export const maxPrice = 999_999_999;

// Las existencias son un int de 32 bits en la API.
export const maxStock = 2_147_483_647;

// Largo máximo del nombre de un producto (ProductLimits.NameMaxLength en la API).
export const productNameMaxLength = 150;

// Tamaño máximo de una factura (InvoiceReceiptLimits.MaxFileBytes en la API).
export const maxInvoiceFileMegabytes = 10;
export const maxInvoiceFileBytes = maxInvoiceFileMegabytes * 1024 * 1024;

// El número de factura se guarda como número de lote (InventoryLotLimits.LotNumberMaxLength).
export const invoiceNumberMaxLength = 50;

// Límites de la conversación con el asistente (AssistantLimits en la API).
export const assistantMaxMessages = 20;
export const assistantMessageMaxLength = 4000;
