---
version: 1
slug: "frontend-src-app"
primary_target: "frontend/src/app"
related_targets: []
---

# Surface: aplicación de inventario (todas las pantallas)

Mode: Operate. Audiencia: operadores de bodega y compras, uso diario en escritorio. Tarea: buscar productos, comparar precios por proveedor, revisar stock y registrar facturas. Restricciones: Angular Material se conserva (reestilizado), layout y navegación se conservan, sin cambios de HTML/TS ni de textos.

## Direction contract

THESIS: Una herramienta empresarial al nivel de SAP Fiori/Odoo con la densidad de la web japonesa: cada área de trabajo es un módulo con filete y barra de título, y cabe más por pantalla. Sobria y profesional, no agresiva: esquinas suaves, bordes claros y pesos contenidos. Rechaza el admin SaaS de grises lavados y tarjetas flotantes, y también el semi-brutalismo de esquinas rectas y contornos duros.

OWN-WORLD: Fondo gris frío #eef1f4, módulos blancos con filete #dbe0e6, esquinas de 8px (6px en campos y botones, 4px en etiquetas) y una sombra muy leve, barra de título de módulo con fondo #f6f8fa y regla superior azul apagada (#9fb3d1) de 2px. Contorno de campos #b4bdc9. Etiquetas de estado con fondo suave y sin contorno. Tinta #101828, texto secundario #344054. Azul corporativo #0b5cd5 solo para acción, selección y enlaces. Rojo #b42318 solo para errores y sin stock (las acciones destructivas van en gris y se tiñen de rojo al apuntarlas), ámbar para avisos con borde suave, verde para confirmado. Inter 400 texto, 500 nombres, 600 títulos y cifras; cifras tabulares. Login con panel de marca en azul marino #0f2a52.

STORY: El operador entra, ve el estado del inventario en módulos compactos, encuentra el producto, identifica el precio más barato y registra la factura sin dudar qué es acción, qué es dato y qué es aviso.

FIRST VIEWPORT: Barra lateral blanca de 248px con marca, enlaces compactos (activo en tinte azul suave sin borde, texto azul en 600) y cuenta al pie. Contenido: título de página 24px/600, filtros de 36px, y la tabla o los módulos con encabezados en gris oscuro sobre gris claro. La acción principal es un botón azul sólido arriba a la derecha.

FORM: Estándar de la categoría (canon) con steer «parecido a la web japonesa, pero algo más empresarial»; seed: 5a00b4525529bb0f.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
