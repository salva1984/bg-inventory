// Domain entities of the inventory. Each entity has a *Limits class next to it: the same
// constants size the database columns (EF configuration) and validate the requests (DTOs),
// so a rule like "a code has at most 30 characters" lives in a single place.
namespace BgInventario.Core.Entities;

/// <summary>Timestamps filled automatically by an EF SaveChanges interceptor.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Soft delete: deleting only stamps <see cref="DeletedAt"/> and a global EF query filter hides the
/// row from every query, so the history stays in the database without leaking into the API.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}

public enum UserRole
{
    Admin,
    Viewer
}

/// <summary>Application user. Only used for authentication; not related to the inventory.</summary>
public class User : IAuditable
{
    public int Id { get; set; }

    /// <summary>Login identifier. Stored lowercase so the lookup is case-insensitive.</summary>
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class UserLimits
{
    // Longest address allowed by SMTP (RFC 5321 path limit).
    public const int EmailMaxLength = 254;
    public const int FullNameMaxLength = 150;
    public const int PasswordMaxLength = 100;

    // Identity's PBKDF2 hashes are ~84 chars in base64; the margin allows future algorithms.
    public const int PasswordHashMaxLength = 500;
    public const int RoleMaxLength = 20;
}

/// <summary>Catalog item. Its price and stock live in its <see cref="InventoryLot"/>s.</summary>
public class Product : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<InventoryLot> Lots { get; set; } = [];
}

public static class ProductLimits
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;
}

public class Supplier : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<InventoryLot> Lots { get; set; } = [];
}

public static class SupplierLimits
{
    public const int NameMaxLength = 150;
    public const int ContactEmailMaxLength = 150;
    public const int PhoneMaxLength = 30;
}

/// <summary>
/// Inventory record: price and stock of a product from a given supplier and lot number.
/// A product has one row per supplier/lot combination, e.g. the same monitor bought from three suppliers.
/// </summary>
public class InventoryLot : IAuditable
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SupplierId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
}

public static class InventoryLotLimits
{
    public const int LotNumberMaxLength = 50;

    // Money is stored as decimal(18,2): 18 digits in total, 2 of them after the decimal point.
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
    public const double MinPrice = 0.01;
    public const double MaxPrice = 999_999_999;
}
