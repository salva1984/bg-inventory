# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Operadores de bodega y compras de una empresa que mantiene inventario de productos tecnológicos comprados a varios proveedores. Usan la herramienta a diario en escritorio para revisar existencias, comparar precios entre proveedores y lotes, y registrar la mercadería que llega con cada factura. Dos roles: `admin` (lectura y escritura) y `viewer` (solo lectura).

## Product Purpose

Gestionar el inventario con precio y stock por proveedor y por número de lote, y dar ingreso a la mercadería a partir de las facturas de los proveedores. El éxito es que el operador encuentre un producto, vea su precio más barato y su stock, y registre una factura sin errores y en pocos pasos.

## Positioning

La factura del proveedor (PDF o foto) se lee con un LLM y se concilia línea por línea contra el catálogo antes de sumar stock; la matriz de precios muestra todos los proveedores de un producto lado a lado y marca el más barato.

## Operating Context

- Pantallas: resumen (KPIs, valor por proveedor, menor stock, mayor diferencia de precio), productos, matriz de precios, recepción de facturas, historial de facturas, proveedores, login.
- Trabajo denso en tablas: códigos de producto (p. ej. `APT-00095`), cifras en dólares, existencias, fechas, lotes.
- La recepción de facturas tiene pasos: subir archivo, revisar emisor y advertencias de la lectura, conciliar cada línea con un producto, registrar.
- Proyecto nacido como challenge técnico de Banco Guayaquil, con una sesión de cambios en vivo durante la revisión.

## Capabilities and Constraints

- Frontend Angular 22 con Angular Material: se conserva; sus componentes se reestilizan, no se reemplazan (fácil de defender y modificar en vivo).
- Se conservan el layout y la navegación: barra lateral con marca, enlaces y cuenta; la estructura de cada pantalla y sus flujos.
- Interfaz en español (Ecuador), moneda en dólares.
- Responsive: la barra lateral pasa arriba en pantallas pequeñas.

## Brand Commitments

- Nombre visible: "Inventario".
- Preferencia explícita del usuario (2026-11-21): interfaces utilitarias, semi-brutalistas. La versión anterior se sentía genérica y con poco contraste en tipografía y color.
- Ajuste del usuario (2026-11-21): el semi-brutalismo resultó demasiado agresivo para una herramienta profesional. Se conservan la densidad y el espaciado, pero con esquinas suaves (6–8px), bordes claros, menos negrita y color más contenido.
- Dirección elegida (2026-11-21): el estándar de la categoría de herramientas empresariales, con la densidad de la web japonesa (módulos con filete y barra de título) y acento azul corporativo. Referentes de acabado: SAP Fiori y Odoo.

## Evidence on Hand

- Datos de ejemplo sembrados por la API (`Seed__SampleData`, ~200 productos, 4 proveedores).
- Facturas de ejemplo en `docs/facturas-prueba`.
- No hay clientes, testimonios ni métricas reales; no inventarlos.

## Product Principles

1. Las cifras y los códigos mandan: todo se ordena para leer y comparar números rápido.
2. El estado se dice con texto, no solo con color (p. ej. "Más barato", "Sin stock").
3. Lo pendiente de revisar se distingue de lo que es un error.
4. Lo que existe es fácil de modificar en vivo: pocas piezas, nombres claros.
