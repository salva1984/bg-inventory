namespace BgInventario.Core.Entities;

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

public class User : IAuditable
{
    public int Id { get; set; }

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

    public List<ProductTrigram> Trigrams { get; set; } = [];
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

    public const int PricePrecision = 18;
    public const int PriceScale = 2;
    public const double MinPrice = 0.01;
    public const double MaxPrice = 999_999_999;
}

public class ProductTrigram
{
    public int ProductId { get; set; }
    public required string Trigram { get; set; }
    public Product Product { get; set; } = null!;
}

public static class ProductTrigramLimits
{
    public const int TrigramLength = 3;
}

public class SupplierProductAlias : IAuditable
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public int ProductId { get; set; }

    public required string Description { get; set; }

    public required string NormalizedDescription { get; set; }

    public string? SupplierItemCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public static class SupplierProductAliasLimits
{
    public const int DescriptionMaxLength = 300;
    public const int SupplierItemCodeMaxLength = 50;
}

public class InvoiceReceipt : IAuditable
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public required string SupplierName { get; set; }

    public required string InvoiceNumber { get; set; }
    public DateOnly? IssueDate { get; set; }

    public decimal Subtotal { get; set; }

    public decimal? InvoiceTotal { get; set; }

    public int RegisteredByUserId { get; set; }

    public required string FileName { get; set; }
    public required string FileContentType { get; set; }
    public required byte[] FileContent { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // No navigation to Supplier or Product: they have soft-delete query filters, and joining
    // through them would hide the history of a deleted supplier or product. The copied names are used.
    public User RegisteredBy { get; set; } = null!;
    public List<InvoiceReceiptLine> Lines { get; set; } = [];
}

public static class InvoiceReceiptLimits
{
    // An invoice is a few pages; the limit protects the server's memory and keeps the stored file small.
    public const int MaxFileMegabytes = 10;
    public const int MaxFileBytes = MaxFileMegabytes * 1024 * 1024;

    public const int FileNameMaxLength = 255;

    public const int FileContentTypeMaxLength = 50;

    // Amounts of a whole invoice. A line can reach int.MaxValue units × MaxPrice (about 2.1e18),
    // which needs 19 integer digits: more than the 16 of a price's decimal(18,2).
    public const int AmountPrecision = 21;
    public const string MaxAmount = "9999999999999999999.99";
}

public class InvoiceReceiptLine
{
    public int Id { get; set; }
    public int InvoiceReceiptId { get; set; }

    public int LineNumber { get; set; }

    public required string Description { get; set; }
    public string? SupplierItemCode { get; set; }

    public int ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required string ProductName { get; set; }

    public bool ProductCreated { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public InvoiceReceipt InvoiceReceipt { get; set; } = null!;
}
