using System.Linq.Expressions;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;
using BgInventario.Core.Features.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Suppliers;

/// <summary>
/// Supplier use cases: listing, CRUD and the business rules (unique name, cannot delete if in use).
/// Every successful write invalidates the dashboard summary, which shows supplier count and names.
/// </summary>
public sealed class SupplierService(IAppDbContext db, TimeProvider clock, DashboardCache dashboardCache)
{
    public static readonly ErrorDetail NotFound = ErrorDetail.NotFound("supplier.not_found", "El proveedor no existe.");
    public static readonly ErrorDetail DuplicateName = ErrorDetail.Conflict("supplier.duplicate_name", "Ya existe un proveedor con ese nombre.");
    public static readonly ErrorDetail InUse = ErrorDetail.Conflict("supplier.in_use", "No se puede eliminar: el proveedor tiene productos asociados.");

    // Shared projection (translated to SQL): ProductCount counts distinct products, not lots.
    private static readonly Expression<Func<Supplier, SupplierItem>> ToItem = s => new SupplierItem(
        s.Id,
        s.Name,
        s.ContactEmail,
        s.Phone,
        s.Lots.Select(lot => lot.ProductId).Distinct().Count());

    public Task<PagedResult<SupplierItem>> GetPagedAsync(SupplierQuery query, CancellationToken ct)
    {
        var suppliers = db.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            suppliers = suppliers.Where(s => s.Name.Contains(term));
        }

        return suppliers.OrderBy(s => s.Name).Select(ToItem).ToPagedResultAsync(query, ct);
    }

    public async Task<IReadOnlyList<SupplierLookup>> GetLookupAsync(CancellationToken ct) =>
        await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SupplierLookup(s.Id, s.Name))
            .ToListAsync(ct);

    public async Task<Result<SupplierItem>> GetByIdAsync(int id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(ToItem)
            .FirstOrDefaultAsync(ct);

        return supplier is null ? NotFound : supplier;
    }

    public async Task<Result<SupplierItem>> CreateAsync(SaveSupplierRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await db.Suppliers.AnyAsync(s => s.Name == name, ct))
        {
            return DuplicateName;
        }

        var supplier = new Supplier { Name = name };
        Apply(supplier, request);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);

        return new SupplierItem(supplier.Id, supplier.Name, supplier.ContactEmail, supplier.Phone, ProductCount: 0);
    }

    public async Task<Result<SupplierItem>> UpdateAsync(int id, SaveSupplierRequest request, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null)
        {
            return NotFound;
        }

        var name = request.Name.Trim();
        var nameTakenByAnother = await db.Suppliers.AnyAsync(s => s.Name == name && s.Id != id, ct);
        if (nameTakenByAnother)
        {
            return DuplicateName;
        }

        Apply(supplier, request);
        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null)
        {
            return NotFound;
        }

        // Deleting it would leave products without the supplier of their stock. Lots of deleted
        // products are hidden by the query filter, so they do not block the delete.
        var hasInventory = await db.InventoryLots.AnyAsync(lot => lot.SupplierId == id, ct);
        if (hasInventory)
        {
            return InUse;
        }

        // Soft delete: the row stays for history and the query filter hides it from now on.
        supplier.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await dashboardCache.InvalidateAsync(ct);
        return Result.Success();
    }

    private static void Apply(Supplier supplier, SaveSupplierRequest request)
    {
        supplier.Name = request.Name.Trim();
        supplier.ContactEmail = NullIfEmpty(request.ContactEmail);
        supplier.Phone = NullIfEmpty(request.Phone);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
