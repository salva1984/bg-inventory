# 📦 Inventario de productos

Aplicación web para gestionar el inventario de una empresa con **precios y stock por proveedor y número de lote**, con dashboard de indicadores e ingreso de mercadería a partir de facturas de proveedores leídas con un LLM.

**Stack:** Angular 22 + Angular Material · .NET 10 (Minimal APIs) · SQL Server / Azure SQL · JWT · Serilog + OpenTelemetry · GitHub Actions → Azure

## Qué incluye

- CRUD de productos, proveedores y lotes, con paginación, búsqueda y soft delete.
- Matriz de precios por proveedor (`GET /api/products/price-matrix`).
- Dashboard: valor del inventario, valor por proveedor, menor stock y mayor diferencia de precio.
- Ingreso de stock desde facturas (PDF o imagen) con extracción por LLM y conciliación de productos.
- Salida rápida de stock desde el listado de productos: se elige el lote y la cantidad (`POST /api/products/{id}/lots/{lotId}/withdraw`, solo admin, sin historial).
- Autenticación JWT con dos roles: `admin` (lectura y escritura) y `viewer` (solo lectura).
- Observabilidad: logs, trazas y métricas en el Aspire Dashboard, incluidos los errores del navegador.

## Servicios en la nube (remotos)

Cada push a `main` despliega solo. No hay que instalar nada: abre el frontend e inicia sesión con los usuarios de la sección siguiente.

| Servicio | Dónde corre | URL |
|---|---|---|
| **Frontend** (Angular) | Azure App Service | https://front-angular-ewh7gydndudycjdv.westus3-01.azurewebsites.net |
| **API** (.NET 10) | Azure Container Apps | https://container-bg-2.jollycoast-a36c9d38.westus3.azurecontainerapps.io |
| Health de la API | | https://container-bg-2.jollycoast-a36c9d38.westus3.azurecontainerapps.io/health |
| **Base de datos** | Azure SQL Database (serverless) | `bg-inventario.database.windows.net` (solo acepta conexiones con usuario y contraseña) |
| **Imágenes** de la API | Azure Container Registry | `bgcontainers.azurecr.io/bg-backend:<sha>` |

> **Primera carga lenta:** la API escala a cero réplicas cuando nadie la usa y la base serverless se pausa. La primera petición después de un rato puede tardar unos segundos.


## Servicios en local

Requisitos: Docker con Compose y Node.js 22.22.3+, 24.15+ o 26+ (solo para el frontend). Para desarrollar la API sin contenedor, además el SDK de .NET 10.

### 1. Base de datos, API y observabilidad

```bash
cp .env.example .env
docker compose up -d --build
```

### 2. Frontend

En otra terminal:

```bash
cd frontend
npm ci
npm start
```

### Dónde ver cada cosa

| Servicio | URL | Para qué |
|---|---|---|
| **Frontend** | http://localhost:4200 | La aplicación |
| **API + Swagger** | http://localhost:8080/swagger | Documentación y pruebas de los endpoints |
| **Health** | http://localhost:8080/health | Estado de la API y de la base |
| **Aspire Dashboard** | http://localhost:18888 | Logs, trazas y métricas (se vacían al reiniciar el contenedor) |
| **SQL Server** | `localhost,1433` · usuario `sa` · clave `DB_PASSWORD` del `.env` | Conectar con Azure Data Studio, DBeaver, etc. Base `BgInventario` |

Los puertos se cambian en el `.env` (`API_PORT`, `DASHBOARD_UI_PORT`, `DB_PORT`).

### Usuarios de prueba

Los usuarios y las contraseñas de local salen del `.env`. En Azure las contraseñas vienen de los secretos `SEED_ADMIN_PASSWORD` y `SEED_VIEWER_PASSWORD` de GitHub, así que pueden ser otras.

| Usuario | Contraseña | Permisos |
|---|---|---|
| `admin@inventario.local` | `Admin123!` | Lectura y escritura |
| `viewer@inventario.local` | `Viewer123!` | Solo lectura |

### Análisis de facturas (opcional)

Para que funcione el análisis de facturas, define `OPENROUTER_API_KEY` en el `.env`. Sin la clave todo lo demás funciona y solo ese endpoint responde 503. Hay facturas de ejemplo en [docs/facturas-prueba](docs/facturas-prueba).

### Desarrollo de la API sin contenedor

```bash
docker compose up -d db dashboard
cd backend && dotnet run --project src/BgInventario.Api   # http://localhost:8080/swagger
dotnet test
```

Nueva migración:

```bash
dotnet tool restore && dotnet ef migrations add <Nombre> --project src/BgInventario.Infrastructure --startup-project src/BgInventario.Api --output-dir Persistence/Migrations
```

### Frontend: a qué API apunta

La dirección de la API está en `frontend/src/environments/`: `environment.development.ts` (localhost, la usa `npm start`) y `environment.ts` (producción, con el marcador `__API_URL__` que el pipeline reemplaza antes de compilar). Para apuntar el frontend local a la API en la nube, cambia temporalmente la URL de `environment.development.ts` y agrega `http://localhost:4200` a los orígenes CORS de la API.

Comprobar la compilación: `cd frontend && npm run build -- --configuration development`.


## Arquitectura (backend)

```
backend/src
  BgInventario.Core            Dominio + casos de uso (sin ASP.NET, sin SQL Server)
    Entities/               User, Product, Supplier, InventoryLot, ProductTrigram, SupplierProductAlias (+ sus *Limits)
    Common/                 Result/Error, paginación, IAppDbContext
    Features/<Feature>/     *Contracts.cs (DTOs + validación) y *Service.cs (casos de uso)
  BgInventario.Infrastructure  EF Core + SQL Server, migraciones, seed, JWT, hashing, extracción de facturas (PdfPig, PDFtoImage, LLM)
  BgInventario.Api             Minimal APIs, auth, CORS, Swagger, Serilog, manejo global de errores
backend/tests               Tests de servicios sobre SQLite en memoria
```

Dependencias: `Api → Infrastructure → Core`. Core no conoce a nadie.

## Decisiones de diseño

- **Clean Architecture liviana, organizada por feature.** Tres proyectos y no cuatro: dominio y aplicación viven juntos en `Core`, y cada feature agrupa sus contratos y su servicio. Agregar un campo toca pocos archivos.
- **Sin repositorio por entidad.** `DbContext` ya es Repository + Unit of Work. Core lo usa a través de `IAppDbContext`, a cambio de depender del paquete base de EF Core (no del proveedor SQL Server). Las consultas quedan expresivas (proyecciones, agregados) y se testean contra una base relacional real.
- **Interfaces solo donde hay una frontera real** (`IAppDbContext`, `ITokenService`, `IPasswordHasher`, `IInvoiceExtractor`). Los servicios de casos de uso son clases concretas.
- **Result pattern** para errores esperados (no existe, duplicado, regla de negocio). Un solo `ToProblem()` los convierte en ProblemDetails (RFC 7807). Las excepciones quedan para lo inesperado: `GlobalExceptionHandler` las loguea y responde un mensaje amigable con `traceId` para encontrarlas en el dashboard.
- **Validación nativa de .NET 10** (`AddValidation()` + DataAnnotations en los records): valida también colecciones anidadas, sin librerías extra.
- **Modelo:** `Product` 1:N `InventoryLot` N:1 `Supplier`. Cada `InventoryLot` es un registro de inventario (precio y stock de un producto, de un proveedor, en un lote): el mismo monitor comprado a tres proveedores es un solo producto con tres registros. Índice único producto + proveedor + lote (el lote es obligatorio) y check constraints en la base. Los límites de cada campo son constantes compartidas entre la validación y el esquema. La "matriz de precios" del enunciado es una vista pivote (`GET /api/products/price-matrix`).
- **Dashboard** (`GET /api/dashboard`): valor del inventario, valor por proveedor, menor stock y mayor diferencia de precio, calculados con agregados en SQL y guardados en `HybridCache`, que se invalida al guardar un producto o proveedor (ver [docs/decisiones.md](docs/decisiones.md)).
- **Ingreso de mercadería desde facturas** (`POST /api/invoices/analyze` y `/confirm`, solo Admin): un LLM (`openai/gpt-6-luna` por OpenRouter) lee facturas de cualquier formato, PDF o imagen, y el código decide qué producto es cada línea: código o equivalencia ya confirmada por el proveedor, luego búsqueda aproximada por trigramas en SQL. El stock solo cambia cuando el usuario confirma, y cada confirmación enseña al sistema cómo nombra ese proveedor a cada producto (ver [docs/decisiones.md](docs/decisiones.md)).
- **Rendimiento de consultas:** `AsNoTracking`, proyección directa a DTO (solo las columnas necesarias), agregados calculados en SQL, paginación del lado del servidor, índices en código, nombre y proveedor, matriz en dos consultas por página (sin N+1) y `ExecuteUpdateAsync` para el soft delete de productos (un solo `UPDATE`).
- **Seguridad:** JWT (HS256, validación de issuer, audience y lifetime), **autorización por defecto** (fallback policy, el login se declara anónimo de forma explícita), rol `Admin` para escribir, rate limit en el login, PBKDF2 (hasher de Identity) para las contraseñas, mismo mensaje para usuario o clave inválidos, contenedor sin root y secretos por variables de entorno.
- **CORS:** allow-list explícita de orígenes, métodos y headers. El SPA corre en otro origen (`:4200` → `:8080`), así que el navegador hace preflight.
- **Observabilidad:** Serilog con niveles en `appsettings`, logs estructurados a consola y por OTLP al Aspire Dashboard, request logging enriquecido con usuario e IP, y `traceId` en cada error. OpenTelemetry agrega trazas (cada request con sus consultas SQL y su duración) y métricas de ASP.NET Core; logs y trazas comparten el trace ID, así que desde un error se llega a la request completa. El destino se cambia con `OTEL_EXPORTER_OTLP_ENDPOINT` (por ejemplo, a Azure Monitor) sin tocar el código. Los errores del navegador también llegan ahí: el frontend los envía a `POST /api/client-logs` (ver [docs/decisiones.md](docs/decisiones.md)).
- **Soft delete** de productos y proveedores: eliminar solo marca `DeletedAt` y un filtro global de EF (`HasQueryFilter`) los oculta de todas las consultas, junto con los registros de inventario del producto eliminado. Los índices únicos (código de producto, nombre de proveedor) solo cubren filas activas, así que un código o nombre eliminado se puede reutilizar. Un proveedor con inventario activo sigue sin poder eliminarse.
- **Migraciones y seed** al iniciar (`Database:MigrateOnStartup`) mediante `UseAsyncSeeding` de EF. En un entorno productivo se usaría un migration bundle en el pipeline.
- **Datos de ejemplo** (`Seed:SampleData`): los proveedores y productos del enunciado, más `Seed:SampleProductCount` productos generados con [Bogus](https://github.com/bchavez/Bogus) con semilla fija (siempre los mismos datos) a partir de un catálogo de nombres realistas (`SampleCatalog.cs`).

## Roadmap

- Azure: Azure SQL (solo cambia el connection string), Container Apps, Key Vault, Application Insights como sink.
- IA: embeddings con el tipo `vector` de SQL Server 2025 / Azure SQL como segunda fuente de candidatos en la conciliación de facturas; el LLM como reordenador de los candidatos dudosos (nunca para confirmar).
- Concurrencia optimista (`rowversion`).
