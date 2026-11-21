using BgInventario.Api.Setup;
using BgInventario.Core.Common;
using BgInventario.Core.Features.Products;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", GetPaged).WithSummary("Listado paginado con búsqueda, filtro por proveedor y orden");
        group.MapGet("/price-matrix", GetPriceMatrix).WithSummary("Matriz Producto × Proveedor con precios");
        group.MapGet("/{id:int}", GetById).WithSummary("Detalle con su inventario por proveedor/lote")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var write = group.MapGroup("")
            .RequireAuthorization(ServiceCollectionExtensions.AdminPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        write.MapPost("/", Create).WithSummary("Crea un producto con su inventario por proveedor/lote")
            .ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPut("/{id:int}", Update).WithSummary("Actualiza el producto y sincroniza su inventario")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        write.MapDelete("/{id:int}", Delete).WithSummary("Elimina el producto y su inventario")
            .ProducesProblem(StatusCodes.Status404NotFound);
        write.MapPost("/{id:int}/lots/{lotId:int}/withdraw", WithdrawStock)
            .WithSummary("Saca unidades de un lote (salida rápida de inventario)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<PagedResult<ProductListItem>>> GetPaged(
        [AsParameters] ProductQuery query, ProductService products, CancellationToken ct) =>
        TypedResults.Ok(await products.GetPagedAsync(query, ct));

    private static async Task<Ok<PriceMatrix>> GetPriceMatrix(
        [AsParameters] ProductQuery query, ProductService products, CancellationToken ct) =>
        TypedResults.Ok(await products.GetPriceMatrixAsync(query, ct));

    private static async Task<Results<Ok<ProductDetail>, ProblemHttpResult>> GetById(
        int id, ProductService products, CancellationToken ct)
    {
        var result = await products.GetByIdAsync(id, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<Created<ProductDetail>, ProblemHttpResult>> Create(
        SaveProductRequest request, ProductService products, CancellationToken ct)
    {
        var result = await products.CreateAsync(request, ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/products/{result.Value.Id}", result.Value)
            : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<ProductDetail>, ProblemHttpResult>> Update(
        int id, SaveProductRequest request, ProductService products, CancellationToken ct)
    {
        var result = await products.UpdateAsync(id, request, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        int id, ProductService products, CancellationToken ct)
    {
        var result = await products.DeleteAsync(id, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<ProductDetail>, ProblemHttpResult>> WithdrawStock(
        int id, int lotId, WithdrawStockRequest request, ProductService products, CancellationToken ct)
    {
        var result = await products.WithdrawStockAsync(id, lotId, request, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }
}
