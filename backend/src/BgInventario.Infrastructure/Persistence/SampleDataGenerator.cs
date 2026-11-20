using BgInventario.Core.Entities;
using Bogus;

namespace BgInventario.Infrastructure.Persistence;

/// <summary>Suppliers and products created by <see cref="SampleDataGenerator"/>, ready to be added to the context.</summary>
internal sealed record GeneratedSampleData(List<Supplier> Suppliers, List<Product> Products);

/// <summary>
/// Generates volume sample data with Bogus on top of the fixed products of the challenge, so the list,
/// paging and search can be tried with a realistic amount of rows. Names come from <see cref="SampleCatalog"/>;
/// Bogus only combines them and fills prices, stock, phones and model numbers.
/// </summary>
internal static class SampleDataGenerator
{
    // Fixed seed: every run generates exactly the same data, so whoever reviews the app sees the same catalog.
    private const int RandomSeed = 2026;

    private const int MaxLotsPerProduct = 3;
    private const int MaxStock = 150;

    /// <summary>
    /// Creates the catalog suppliers and <paramref name="productCount"/> products. Each product gets lots from
    /// one to three different suppliers, picked among the new ones and <paramref name="existingSuppliers"/>.
    /// </summary>
    public static GeneratedSampleData Generate(int productCount, IEnumerable<Supplier> existingSuppliers)
    {
        var faker = new Faker("es") { Random = new Randomizer(RandomSeed) };

        var suppliers = SampleCatalog.Suppliers
            .Select(s => Supplier(faker, s))
            .ToList();

        var supplierPool = existingSuppliers.Concat(suppliers).ToList();

        var products = new List<Product>(productCount);
        for (var sequence = 1; sequence <= productCount; sequence++)
            products.Add(Product(faker, sequence, supplierPool));

        return new GeneratedSampleData(suppliers, products);
    }

    private static Supplier Supplier(Faker faker, SampleSupplier sample) => new()
    {
        Name = sample.Name,
        ContactEmail = $"ventas@{sample.EmailDomain}",
        Phone = faker.Phone.PhoneNumber("04#######") // Guayaquil landline format, like the fixed suppliers
    };

    private static Product Product(Faker faker, int sequence, IReadOnlyList<Supplier> supplierPool)
    {
        var type = faker.PickRandom(SampleCatalog.ProductTypes);
        var brand = faker.PickRandom(SampleCatalog.Brands);
        var model = faker.Random.Replace("??-####").ToUpperInvariant(); // '?' is a letter, '#' a digit

        // Distinct suppliers, so the unique (product, supplier, lot number) index is never violated.
        var lotCount = faker.Random.Int(1, MaxLotsPerProduct);
        var lotSuppliers = faker.PickRandom(supplierPool, lotCount);

        return new Product
        {
            // The sequence keeps codes unique even when the same type is picked many times.
            Code = $"{type.CodePrefix}-{sequence:D5}",
            Name = $"{type.Name} {brand} {model}",
            Description = type.Description,
            Lots = lotSuppliers.Select(supplier => Lot(faker, supplier, type)).ToList()
        };
    }

    private static InventoryLot Lot(Faker faker, Supplier supplier, SampleProductType type)
    {
        return new InventoryLot
        {
            Supplier = supplier,
            LotNumber = $"L-{faker.Random.Int(1000, 9999)}",
            Price = faker.Finance.Amount(type.MinPrice, type.MaxPrice),
            Stock = faker.Random.Int(0, MaxStock)
        };
    }
}
