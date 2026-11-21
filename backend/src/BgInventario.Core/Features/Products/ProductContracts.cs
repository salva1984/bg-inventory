using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Products;

public enum ProductSortBy
{
    Name,
    Code,
    TotalStock,
    MinPrice,
    UpdatedAt
}

public sealed record ProductQuery : PageQuery
{
    public string? Search { get; init; }
    public int? SupplierId { get; init; }
    public ProductSortBy? SortBy { get; init; }
    public bool? Descending { get; init; }
}

public sealed record ProductListItem(
    int Id,
    string Code,
    string Name,
    string? Description,
    int SupplierCount,
    int TotalStock,
    decimal? MinPrice,
    decimal? MaxPrice,
    DateTime UpdatedAt);

public sealed record ProductDetail(
    int Id,
    string Code,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<InventoryLotItem> Lots);

public sealed record InventoryLotItem(
    int Id,
    int SupplierId,
    string SupplierName,
    string LotNumber,
    decimal Price,
    int Stock);

/// <summary>Create/update payload. On update the lots are synced: lots not sent are removed.</summary>
public sealed record SaveProductRequest(
    [property: Required, StringLength(ProductLimits.CodeMaxLength)] string Code,
    [property: Required, StringLength(ProductLimits.NameMaxLength)] string Name,
    [property: StringLength(ProductLimits.DescriptionMaxLength)] string? Description,
    IReadOnlyList<SaveInventoryLotRequest>? Lots);

public sealed record SaveInventoryLotRequest(
    int? Id,
    [property: Range(1, int.MaxValue)] int SupplierId,
    [property: Required, StringLength(InventoryLotLimits.LotNumberMaxLength)] string LotNumber,
    [property: Range(InventoryLotLimits.MinPrice, InventoryLotLimits.MaxPrice)] decimal Price,
    [property: Range(0, int.MaxValue)] int Stock);

public sealed record PriceMatrix(
    IReadOnlyList<PriceMatrixSupplier> Suppliers,
    PagedResult<PriceMatrixRow> Products);

public sealed record PriceMatrixSupplier(int Id, string Name);

/// <summary>Cells keyed by supplier id. If a supplier has several lots, shows the lowest price and the total stock.</summary>
public sealed record PriceMatrixRow(
    int ProductId,
    string Code,
    string Name,
    IReadOnlyDictionary<int, PriceMatrixCell> Cells);

public sealed record PriceMatrixCell(decimal Price, int Stock, int Lots);
