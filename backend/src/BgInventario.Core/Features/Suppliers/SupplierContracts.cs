// Request/response shapes of the supplier endpoints. Validation attributes run automatically
// before the endpoint (built-in Minimal API validation in .NET 10).
using System.ComponentModel.DataAnnotations;
using BgInventario.Core.Common;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Suppliers;

public sealed record SupplierQuery : PageQuery
{
    public string? Search { get; init; }
}

public sealed record SupplierItem(int Id, string Name, string? ContactEmail, string? Phone, int ProductCount);

/// <summary>Minimal shape for dropdowns.</summary>
public sealed record SupplierLookup(int Id, string Name);

public sealed record SaveSupplierRequest(
    [property: Required, StringLength(SupplierLimits.NameMaxLength)] string Name,
    [property: EmailAddress, StringLength(SupplierLimits.ContactEmailMaxLength)] string? ContactEmail,
    [property: StringLength(SupplierLimits.PhoneMaxLength)] string? Phone);
