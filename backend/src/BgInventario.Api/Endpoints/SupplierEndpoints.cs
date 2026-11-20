using BgInventario.Api.Setup;
using BgInventario.Core.Common;
using BgInventario.Core.Features.Suppliers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BgInventario.Api.Endpoints;

/// <summary>
/// Supplier routes: any authenticated user can read, only Admin can write.
/// Handlers just call the use case and map its Result to an HTTP response.
/// </summary>
public static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/suppliers")
            .WithTags("Suppliers")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", GetPaged).WithSummary("Listado paginado de proveedores");
        group.MapGet("/lookup", GetLookup).WithSummary("Lista liviana (id, nombre) para combos");
        group.MapGet("/{id:int}", GetById).WithSummary("Detalle de proveedor")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Write operations: same routes, with the Admin policy on top.
        var write = group.MapGroup("")
            .RequireAuthorization(ServiceCollectionExtensions.AdminPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        write.MapPost("/", Create).WithSummary("Crea un proveedor")
            .ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPut("/{id:int}", Update).WithSummary("Actualiza un proveedor")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        write.MapDelete("/{id:int}", Delete).WithSummary("Elimina un proveedor sin productos asociados")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<PagedResult<SupplierItem>>> GetPaged(
        [AsParameters] SupplierQuery query, SupplierService suppliers, CancellationToken ct) =>
        TypedResults.Ok(await suppliers.GetPagedAsync(query, ct));

    private static async Task<Ok<IReadOnlyList<SupplierLookup>>> GetLookup(SupplierService suppliers, CancellationToken ct) =>
        TypedResults.Ok(await suppliers.GetLookupAsync(ct));

    private static async Task<Results<Ok<SupplierItem>, ProblemHttpResult>> GetById(
        int id, SupplierService suppliers, CancellationToken ct)
    {
        var result = await suppliers.GetByIdAsync(id, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<Created<SupplierItem>, ProblemHttpResult>> Create(
        SaveSupplierRequest request, SupplierService suppliers, CancellationToken ct)
    {
        var result = await suppliers.CreateAsync(request, ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/suppliers/{result.Value.Id}", result.Value)
            : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<SupplierItem>, ProblemHttpResult>> Update(
        int id, SaveSupplierRequest request, SupplierService suppliers, CancellationToken ct)
    {
        var result = await suppliers.UpdateAsync(id, request, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        int id, SupplierService suppliers, CancellationToken ct)
    {
        var result = await suppliers.DeleteAsync(id, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }
}
