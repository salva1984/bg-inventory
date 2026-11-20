// Database schema of each entity: column sizes, indexes, relationships and check constraints.
// Sizes come from the *Limits constants shared with the request validation.
//
// Soft delete: products and suppliers are never removed, only stamped with DeletedAt. Each one has a
// query filter that hides deleted rows, and its unique indexes only cover active rows, so a deleted
// code or name can be reused.
using BgInventario.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BgInventario.Infrastructure.Persistence;

/// <summary>SQL filter of the unique indexes of soft-deletable entities: uniqueness only among active rows.</summary>
file static class SoftDelete
{
    public const string ActiveRowsOnly = "[DeletedAt] IS NULL";
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

        // A soft-deleted product keeps its lots (hidden by the lot filter). The cascade only applies
        // if a product row is purged directly in the database.
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

        // A lot always points to an existing supplier row; the service only soft-deletes suppliers
        // without active inventory.
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
        // Last line of defense if a value skips the API validation (scripts, other clients).
        b.ToTable("InventoryLots", t =>
        {
            t.HasCheckConstraint("CK_InventoryLots_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_InventoryLots_Stock", "[Stock] >= 0");
        });
        b.Property(lot => lot.LotNumber).IsRequired().HasMaxLength(InventoryLotLimits.LotNumberMaxLength);
        b.Property(lot => lot.Price).HasPrecision(InventoryLotLimits.PricePrecision, InventoryLotLimits.PriceScale);

        // Lots of a deleted product (or supplier) disappear with it, also from totals and counts.
        b.HasQueryFilter(lot => lot.Product.DeletedAt == null && lot.Supplier.DeletedAt == null);

        // One row per product + supplier + lot.
        b.HasIndex(lot => new { lot.ProductId, lot.SupplierId, lot.LotNumber }).IsUnique();
        b.HasIndex(lot => lot.SupplierId);
    }
}
