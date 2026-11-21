using BgInventario.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BgInventario.Infrastructure.Persistence;

file static class SoftDelete
{
    public const string ActiveRowsOnly = "[DeletedAt] IS NULL";
}

/// <summary>
/// Collation for keys computed in C# (normalized texts, trigrams). The database default is case-,
/// accent- and width-insensitive, so two values that C# keeps apart (ordinal comparison) could be
/// equal for SQL Server and break a unique key. A binary collation compares them like C# does.
/// The SQLite test database registers a collation with this name that compares ordinally (TestDb).
/// </summary>
internal static class Collations
{
    public const string Ordinal = "Latin1_General_100_BIN2";
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Email).HasMaxLength(UserLimits.EmailMaxLength);
        b.Property(u => u.FullName).HasMaxLength(UserLimits.FullNameMaxLength);
        b.Property(u => u.PasswordHash).HasMaxLength(UserLimits.PasswordHashMaxLength);
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(UserLimits.RoleMaxLength);
        b.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Code).HasMaxLength(ProductLimits.CodeMaxLength);
        b.Property(p => p.Name).HasMaxLength(ProductLimits.NameMaxLength);
        b.Property(p => p.Description).HasMaxLength(ProductLimits.DescriptionMaxLength);
        b.HasQueryFilter(p => p.DeletedAt == null);
        b.HasIndex(p => p.Code).IsUnique().HasFilter(SoftDelete.ActiveRowsOnly);
        b.HasIndex(p => p.Name); // the list is sorted and searched by name

        b.HasMany(p => p.Lots)
            .WithOne(lot => lot.Product)
            .HasForeignKey(lot => lot.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(s => s.Name).HasMaxLength(SupplierLimits.NameMaxLength);
        b.Property(s => s.ContactEmail).HasMaxLength(SupplierLimits.ContactEmailMaxLength);
        b.Property(s => s.Phone).HasMaxLength(SupplierLimits.PhoneMaxLength);
        b.HasQueryFilter(s => s.DeletedAt == null);
        b.HasIndex(s => s.Name).IsUnique().HasFilter(SoftDelete.ActiveRowsOnly);

        b.HasMany(s => s.Lots)
            .WithOne(lot => lot.Supplier)
            .HasForeignKey(lot => lot.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> b)
    {
        b.ToTable("InventoryLots", t =>
        {
            t.HasCheckConstraint("CK_InventoryLots_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_InventoryLots_Stock", "[Stock] >= 0");
        });
        b.Property(lot => lot.LotNumber).IsRequired().HasMaxLength(InventoryLotLimits.LotNumberMaxLength);
        b.Property(lot => lot.Price).HasPrecision(InventoryLotLimits.PricePrecision, InventoryLotLimits.PriceScale);

        b.HasQueryFilter(lot => lot.Product.DeletedAt == null && lot.Supplier.DeletedAt == null);

        b.HasIndex(lot => new { lot.ProductId, lot.SupplierId, lot.LotNumber }).IsUnique();
        b.HasIndex(lot => lot.SupplierId);
    }
}

internal sealed class ProductTrigramConfiguration : IEntityTypeConfiguration<ProductTrigram>
{
    public void Configure(EntityTypeBuilder<ProductTrigram> b)
    {
        // The key starts with the trigram: the candidate search filters by trigram and groups by product,
        // so this clustered index serves it directly. The product id is the second key column.
        b.HasKey(t => new { t.Trigram, t.ProductId });
        b.Property(t => t.Trigram)
            .HasMaxLength(ProductTrigramLimits.TrigramLength)
            .UseCollation(Collations.Ordinal); // part of the key, built as a HashSet with ordinal comparison

        b.HasIndex(t => t.ProductId);

        b.HasQueryFilter(t => t.Product.DeletedAt == null);

        b.HasOne(t => t.Product)
            .WithMany(p => p.Trigrams)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupplierProductAliasConfiguration : IEntityTypeConfiguration<SupplierProductAlias>
{
    public void Configure(EntityTypeBuilder<SupplierProductAlias> b)
    {
        b.Property(a => a.Description).HasMaxLength(SupplierProductAliasLimits.DescriptionMaxLength);
        b.Property(a => a.NormalizedDescription)
            .HasMaxLength(SupplierProductAliasLimits.DescriptionMaxLength)
            .UseCollation(Collations.Ordinal); // unique key, grouped in C# with ordinal comparison
        b.Property(a => a.SupplierItemCode).HasMaxLength(SupplierProductAliasLimits.SupplierItemCodeMaxLength);

        b.HasIndex(a => new { a.SupplierId, a.NormalizedDescription }).IsUnique();

        // Not unique: the same item code can arrive with different descriptions. The matcher only trusts
        // it when every alias with that code points to the same product.
        b.HasIndex(a => new { a.SupplierId, a.SupplierItemCode });

        b.HasQueryFilter(a => a.Product.DeletedAt == null && a.Supplier.DeletedAt == null);

        b.HasOne(a => a.Product)
            .WithMany()
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(a => a.Supplier)
            .WithMany()
            .HasForeignKey(a => a.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceReceiptConfiguration : IEntityTypeConfiguration<InvoiceReceipt>
{
    public void Configure(EntityTypeBuilder<InvoiceReceipt> b)
    {
        b.Property(r => r.SupplierName).HasMaxLength(SupplierLimits.NameMaxLength);
        b.Property(r => r.InvoiceNumber).HasMaxLength(InventoryLotLimits.LotNumberMaxLength);
        b.Property(r => r.Subtotal).HasPrecision(InvoiceReceiptLimits.AmountPrecision, InventoryLotLimits.PriceScale);
        b.Property(r => r.InvoiceTotal).HasPrecision(InvoiceReceiptLimits.AmountPrecision, InventoryLotLimits.PriceScale);
        b.Property(r => r.FileName).HasMaxLength(InvoiceReceiptLimits.FileNameMaxLength);
        b.Property(r => r.FileContentType).HasMaxLength(InvoiceReceiptLimits.FileContentTypeMaxLength);

        b.HasIndex(r => r.CreatedAt);

        // Not unique: an invoice can be registered again on purpose (see InvoiceService.ConfirmAsync),
        // and each registration keeps its own record. Used to know whether it was already registered.
        b.HasIndex(r => new { r.SupplierId, r.InvoiceNumber });

        // Without navigation (see InvoiceReceipt): only the foreign key. Restrict, because the history
        // must not disappear with a supplier; suppliers are only soft-deleted anyway.
        b.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(r => r.RegisteredBy)
            .WithMany()
            .HasForeignKey(r => r.RegisteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(r => r.Lines)
            .WithOne(line => line.InvoiceReceipt)
            .HasForeignKey(line => line.InvoiceReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvoiceReceiptLineConfiguration : IEntityTypeConfiguration<InvoiceReceiptLine>
{
    public void Configure(EntityTypeBuilder<InvoiceReceiptLine> b)
    {
        // Lines have no DbSet of their own; without this the table would be named in singular.
        b.ToTable("InvoiceReceiptLines");
        b.Property(line => line.Description).HasMaxLength(SupplierProductAliasLimits.DescriptionMaxLength);
        b.Property(line => line.SupplierItemCode).HasMaxLength(SupplierProductAliasLimits.SupplierItemCodeMaxLength);
        b.Property(line => line.ProductCode).HasMaxLength(ProductLimits.CodeMaxLength);
        b.Property(line => line.ProductName).HasMaxLength(ProductLimits.NameMaxLength);
        b.Property(line => line.UnitPrice).HasPrecision(InventoryLotLimits.PricePrecision, InventoryLotLimits.PriceScale);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
