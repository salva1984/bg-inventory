using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Products;

public sealed class ProductService(IAppDbContext db, TimeProvider clock, DashboardCache dashboardCache)
{
    public static readonly ErrorDetail NotFound = ErrorDetail.NotFound("product.not_found", "El producto no existe.");
    public static readonly ErrorDetail DuplicateCode = ErrorDetail.Conflict("product.duplicate_code", "Ya existe un producto con ese código.");
    public static readonly ErrorDetail DuplicateLot = ErrorDetail.Validation("product.duplicate_lot", "Hay registros de inventario repetidos con el mismo proveedor y número de lote.");
    public static readonly ErrorDetail UnknownSupplier = ErrorDetail.Validation("product.unknown_supplier", "Uno o más proveedores no existen.");
    public static readonly ErrorDetail UnknownLot = ErrorDetail.Validation("product.unknown_lot", "Uno o más registros de inventario no pertenecen a este producto.");
    public static readonly ErrorDetail LotNotFound = ErrorDetail.NotFound("product.lot_not_found", "El lote no existe o no pertenece a este producto.");
    public static readonly ErrorDetail InsufficientStock = ErrorDetail.Conflict("product.insufficient_stock", "El lote no tiene existencias suficientes para esa salida.");

    public Task<PagedResult<ProductListItem>> GetPagedAsync(ProductQuery query, CancellationToken ct)
    {
        var products = ApplyProductQuery(query);
        var sorted = SortProducts(products, query.SortBy ?? ProductSortBy.Name, query.Descending ?? false);

        var items = sorted.Select(p => new ProductListItem(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.Lots.Select(lot => lot.SupplierId).Distinct().Count(),
            p.Lots.Sum(lot => (int?)lot.Stock) ?? 0,
            p.Lots.Min(lot => (decimal?)lot.Price),
            p.Lots.Max(lot => (decimal?)lot.Price),
            p.UpdatedAt));

        return items.ToPagedResultAsync(query, ct);
    }

    public async Task<PriceMatrix> GetPriceMatrixAsync(ProductQuery query, CancellationToken ct)
    {
        var products = ApplyProductQuery(query);
        var sorted = SortProducts(products, query.SortBy ?? ProductSortBy.Name, query.Descending ?? false);

        var productPageResult = await sorted
            .Select(p => new { p.Id, p.Code, p.Name })
            .ToPagedResultAsync(query, ct);

        var cells = await GetMatrixCellsAsync([.. productPageResult.Items.Select(p => p.Id)], ct);

        var suppliers = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new PriceMatrixSupplier(s.Id, s.Name))
            .ToListAsync(ct);

        var cellsByProduct = cells.ToLookup(c => c.ProductId);
        var rows = productPageResult.Items
            .Select(p =>
            {
                var productCells = cellsByProduct[p.Id].ToDictionary(c => c.SupplierId, c => c.Cell);
                return new PriceMatrixRow(p.Id, p.Code, p.Name, productCells);
            })
            .ToList();

        var pagedRows = new PagedResult<PriceMatrixRow>(rows, productPageResult.Page, productPageResult.PageSize, productPageResult.TotalCount);
        return new PriceMatrix(suppliers, pagedRows);
    }

    public async Task<Result<ProductDetail>> GetByIdAsync(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDetail(
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.CreatedAt,
                p.UpdatedAt,
                p.Lots
                    .OrderBy(lot => lot.Supplier.Name).ThenBy(lot => lot.LotNumber)
                    .Select(lot => new InventoryLotItem(lot.Id, lot.SupplierId, lot.Supplier.Name, lot.LotNumber, lot.Price, lot.Stock))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return product is null ? NotFound : product;
    }

    public async Task<Result<ProductDetail>> CreateAsync(SaveProductRequest request, CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        if (await db.Products.AnyAsync(p => p.Code == code, ct))
        {
            return DuplicateCode;
        }

        var lots = request.Lots ?? [];
        var noExistingLots = new HashSet<int>();
        var lotsCheck = await ValidateLotsAsync(lots, noExistingLots, ct);
        if (!lotsCheck.IsSuccess)
        {
            return lotsCheck.Error!;
        }

        var product = new Product { Code = code, Name = request.Name.Trim(), Description = Clean(request.Description) };
        product.Lots.AddRange(lots.Select(ToEntity));
        ProductSearchIndex.Refresh(product);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);

        return await GetByIdAsync(product.Id, ct);
    }

    public async Task<Result<ProductDetail>> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await db.Products
            .Include(p => p.Lots)
            .Include(p => p.Trigrams)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound;
        }

        var code = NormalizeCode(request.Code);
        var codeTakenByAnother = await db.Products.AnyAsync(p => p.Code == code && p.Id != id, ct);
        if (codeTakenByAnother)
        {
            return DuplicateCode;
        }

        var lots = request.Lots ?? [];
        var existingLotIds = product.Lots.Select(lot => lot.Id).ToHashSet();
        var lotsCheck = await ValidateLotsAsync(lots, existingLotIds, ct);
        if (!lotsCheck.IsSuccess)
        {
            return lotsCheck.Error!;
        }

        product.Code = code;
        product.Name = request.Name.Trim();
        product.Description = Clean(request.Description);
        SyncLots(product, lots);
        ProductSearchIndex.Refresh(product); // the name or code may have changed

        // Editing only the inventory does not modify a column of the product, so the audit
        // interceptor would not stamp it; we mark it explicitly so "last updated" is truthful.
        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var deletedRows = await db.Products
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.DeletedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        if (deletedRows == 0)
        {
            return NotFound;
        }

        await dashboardCache.InvalidateAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Quick stock exit: subtracts units from one lot of the product. Returns the updated product
    /// so the client can show the new stock without another request.
    /// </summary>
    public async Task<Result<ProductDetail>> WithdrawStockAsync(
        int productId, int lotId, WithdrawStockRequest request, CancellationToken ct)
    {
        var lotBelongsToProduct = await db.InventoryLots
            .AnyAsync(lot => lot.Id == lotId && lot.ProductId == productId, ct);
        if (!lotBelongsToProduct)
        {
            return LotNotFound;
        }

        var withdrawn = await TrySubtractStockAsync(productId, lotId, request.Quantity, ct);
        if (!withdrawn)
        {
            return InsufficientStock;
        }

        await dashboardCache.InvalidateAsync(ct);
        return await GetByIdAsync(productId, ct);
    }

    /// <summary>
    /// Subtracts in a single conditional UPDATE: if two people withdraw at the same time, the
    /// database decides who gets the units and the stock never goes below zero.
    /// Returns false when the lot does not have enough stock.
    /// </summary>
    private async Task<bool> TrySubtractStockAsync(int productId, int lotId, int quantity, CancellationToken ct)
    {
        // ExecuteUpdate skips the audit interceptor, so both dates are stamped here.
        var now = clock.GetUtcNow().UtcDateTime;

        // The soft-delete filter joins Product and Supplier, which SQLite cannot translate inside an
        // UPDATE. It is safe to skip it: the caller already checked the lot with the filters applied.
        var updatedLots = await db.InventoryLots
            .IgnoreQueryFilters()
            .Where(lot => lot.Id == lotId && lot.Stock >= quantity)
            .ExecuteUpdateAsync(set => set
                .SetProperty(lot => lot.Stock, lot => lot.Stock - quantity)
                .SetProperty(lot => lot.UpdatedAt, now), ct);
        if (updatedLots == 0)
        {
            return false;
        }

        // The product list shows "last updated"; a stock exit counts as a change of the product.
        await db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.UpdatedAt, now), ct);
        return true;
    }

    private IQueryable<Product> ApplyProductQuery(ProductQuery query)
    {
        var products = db.Products.AsNoTracking();
        
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.Trim();
            products = products.Where(p => p.Code.Contains(searchTerm) || p.Name.Contains(searchTerm));
        }

        if (query.SupplierId is { } supplierId)
        {
            products = products.Where(p => p.Lots.Any(lot => lot.SupplierId == supplierId));
        }

        return products;
    }

    /// <summary>
    /// Sorting is applied to the entity, before projecting: EF cannot translate an ORDER BY over a
    /// positional record. Ties on totals fall back to the name so pages are stable.
    /// </summary>
    private static IQueryable<Product> SortProducts(IQueryable<Product> products, ProductSortBy sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            (ProductSortBy.Code, false) => products.OrderBy(p => p.Code),
            (ProductSortBy.Code, true) => products.OrderByDescending(p => p.Code),
            (ProductSortBy.TotalStock, false) => products.OrderBy(p => p.Lots.Sum(lot => (int?)lot.Stock) ?? 0).ThenBy(p => p.Name),
            (ProductSortBy.TotalStock, true) => products.OrderByDescending(p => p.Lots.Sum(lot => (int?)lot.Stock) ?? 0).ThenBy(p => p.Name),
            (ProductSortBy.MinPrice, false) => products.OrderBy(p => p.Lots.Min(lot => (decimal?)lot.Price)).ThenBy(p => p.Name),
            (ProductSortBy.MinPrice, true) => products.OrderByDescending(p => p.Lots.Min(lot => (decimal?)lot.Price)).ThenBy(p => p.Name),
            (ProductSortBy.UpdatedAt, false) => products.OrderBy(p => p.UpdatedAt),
            (ProductSortBy.UpdatedAt, true) => products.OrderByDescending(p => p.UpdatedAt),
            (_, true) => products.OrderByDescending(p => p.Name),
            _ => products.OrderBy(p => p.Name)
        };

    private sealed record MatrixCell(int ProductId, int SupplierId, string SupplierName, PriceMatrixCell Cell);

    private async Task<List<MatrixCell>> GetMatrixCellsAsync(List<int> productIds, CancellationToken ct)
    {
        var groups = await db.InventoryLots.AsNoTracking()
            .Where(lot => productIds.Contains(lot.ProductId))
            .GroupBy(lot => new { lot.ProductId, lot.SupplierId, SupplierName = lot.Supplier.Name })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.SupplierId,
                g.Key.SupplierName,
                LowestPrice = g.Min(lot => lot.Price),
                TotalStock = g.Sum(lot => lot.Stock),
                LotCount = g.Count()
            })
            .ToListAsync(ct);

        return groups
            .Select(group =>
            {
                var cell = new PriceMatrixCell(group.LowestPrice, group.TotalStock, group.LotCount);
                return new MatrixCell(group.ProductId, group.SupplierId, group.SupplierName, cell);
            })
            .ToList();
    }

    private async Task<Result> ValidateLotsAsync(
        IReadOnlyList<SaveInventoryLotRequest> lots, IReadOnlySet<int> existingLotIds, CancellationToken ct)
    {
        var hasRepeatedLot = lots
            .GroupBy(lot => (lot.SupplierId, LotNumber: NormalizeLot(lot.LotNumber)))
            .Any(group => group.Count() > 1);
        if (hasRepeatedLot)
        {
            return DuplicateLot;
        }

        // A lot id must belong to this product; otherwise a client could edit another product's stock.
        var referencesForeignLot = lots.Any(lot => lot.Id is { } lotId && !existingLotIds.Contains(lotId));
        if (referencesForeignLot)
        {
            return UnknownLot;
        }

        var supplierIds = lots.Select(lot => lot.SupplierId).Distinct().ToList();
        var foundSuppliers = await db.Suppliers.CountAsync(s => supplierIds.Contains(s.Id), ct);
        var allSuppliersExist = foundSuppliers == supplierIds.Count;
        return allSuppliersExist ? Result.Success() : UnknownSupplier;
    }

    private static void SyncLots(Product product, IReadOnlyList<SaveInventoryLotRequest> lots)
    {
        var requestedById = lots.Where(lot => lot.Id is not null).ToDictionary(lot => lot.Id!.Value);

        product.Lots.RemoveAll(existing => !requestedById.ContainsKey(existing.Id));

        foreach (var existing in product.Lots)
        {
            var requested = requestedById[existing.Id];
            existing.SupplierId = requested.SupplierId;
            existing.LotNumber = NormalizeLot(requested.LotNumber);
            existing.Price = requested.Price;
            existing.Stock = requested.Stock;
        }

        var newLots = lots.Where(lot => lot.Id is null).Select(ToEntity);
        product.Lots.AddRange(newLots);
    }

    private static InventoryLot ToEntity(SaveInventoryLotRequest lot) => new()
    {
        SupplierId = lot.SupplierId,
        LotNumber = NormalizeLot(lot.LotNumber),
        Price = lot.Price,
        Stock = lot.Stock
    };

    // Codes and lot numbers are compared case-insensitively by storing them in upper case.
    // Internal: the invoice confirmation looks up products and lots with the same rules.
    internal static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    internal static string NormalizeLot(string lot) => lot.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
