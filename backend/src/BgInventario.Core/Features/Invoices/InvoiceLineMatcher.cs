using BgInventario.Core.Common;
using BgInventario.Core.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Invoices;

public sealed class InvoiceLineMatcher(IAppDbContext db)
{
    // How many products the trigram query brings before scoring them. Larger than the candidates
    // returned because the query ranks by shared trigrams, and the score also penalizes long names.
    internal const int CandidatePoolSize = 20;

    internal const int MaxCandidates = 5;

    internal const double SuggestedMinScore = 0.45;

    internal const double CandidateMinScore = 0.2;

    // A description with fewer trigrams (e.g. "AB") matches almost anything; it goes straight to Manual.
    internal const int MinSearchTrigrams = 4;

    private const double KnownEquivalenceScore = 1;

    public async Task<IReadOnlyList<LineMatch>> MatchAsync(
        int supplierId, IReadOnlyList<ExtractedInvoiceLine> lines, CancellationToken ct)
    {
        var known = await LoadKnownEquivalencesAsync(supplierId, lines, ct);

        var matches = new List<LineMatch>(lines.Count);
        foreach (var line in lines)
        {
            var match = known.Find(line) ?? await SearchCandidatesAsync(line.Description, ct);
            matches.Add(match);
        }

        return matches;
    }

    private async Task<KnownEquivalences> LoadKnownEquivalencesAsync(
        int supplierId, IReadOnlyList<ExtractedInvoiceLine> lines, CancellationToken ct)
    {
        var descriptions = lines
            .Select(line => ProductSearchIndex.Normalize(line.Description))
            .Distinct()
            .ToList();
        var itemCodes = lines
            .Select(line => NormalizeItemCode(line.SupplierItemCode))
            .OfType<string>()
            .Distinct()
            .ToList();

        var aliases = await db.SupplierProductAliases.AsNoTracking()
            .Where(a => a.SupplierId == supplierId)
            .Where(a => descriptions.Contains(a.NormalizedDescription)
                || (a.SupplierItemCode != null && itemCodes.Contains(a.SupplierItemCode)))
            .Select(a => new KnownAlias(
                a.NormalizedDescription,
                a.SupplierItemCode,
                new ProductCandidate(a.ProductId, a.Product.Code, a.Product.Name, KnownEquivalenceScore)))
            .ToListAsync(ct);

        var productsByCode = await db.Products.AsNoTracking()
            .Where(p => itemCodes.Contains(p.Code))
            .Select(p => new ProductCandidate(p.Id, p.Code, p.Name, KnownEquivalenceScore))
            .ToDictionaryAsync(p => p.Code, ct);

        return new KnownEquivalences(aliases, productsByCode);
    }

    private async Task<LineMatch> SearchCandidatesAsync(string description, CancellationToken ct)
    {
        var lineTrigrams = ProductSearchIndex.GetTrigrams(description);
        var isTooGeneric = lineTrigrams.Count < MinSearchTrigrams;
        if (isTooGeneric)
        {
            return ManualMatch;
        }

        var searchTrigrams = lineTrigrams.ToList();
        var pool = await db.ProductTrigrams.AsNoTracking()
            .Where(t => searchTrigrams.Contains(t.Trigram))
            .GroupBy(t => t.ProductId)
            .Select(group => new { ProductId = group.Key, SharedTrigrams = group.Count() })
            .OrderByDescending(x => x.SharedTrigrams)
            .ThenBy(x => x.ProductId)
            .Take(CandidatePoolSize)
            .ToListAsync(ct);
        if (pool.Count == 0)
        {
            return ManualMatch;
        }

        var poolIds = pool.Select(x => x.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => poolIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, TrigramCount = p.Trigrams.Count })
            .ToListAsync(ct);

        var sharedByProduct = pool.ToDictionary(x => x.ProductId, x => x.SharedTrigrams);
        var candidates = products
            .Select(p =>
            {
                var score = ProductSearchIndex.CalculateDiceCoefficient(sharedByProduct[p.Id], lineTrigrams.Count, p.TrigramCount);
                return new ProductCandidate(p.Id, p.Code, p.Name, score);
            })
            .Where(c => c.Score >= CandidateMinScore)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Name)
            .Take(MaxCandidates)
            .ToList();

        if (candidates.Count == 0)
        {
            return ManualMatch;
        }

        var bestIsClose = candidates[0].Score >= SuggestedMinScore;
        var status = bestIsClose ? LineMatchStatus.Suggested : LineMatchStatus.NeedsReview;
        return new LineMatch(status, null, candidates);
    }

    private static readonly LineMatch ManualMatch = new(LineMatchStatus.Manual, null, []);

    internal static string? NormalizeItemCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : ProductService.NormalizeCode(code);

    private sealed record KnownAlias(string NormalizedDescription, string? SupplierItemCode, ProductCandidate Product);

    private sealed class KnownEquivalences(
        IReadOnlyList<KnownAlias> aliases, IReadOnlyDictionary<string, ProductCandidate> productsByCode)
    {
        public LineMatch? Find(ExtractedInvoiceLine line)
        {
            var itemCode = NormalizeItemCode(line.SupplierItemCode);
            if (itemCode is not null)
            {
                var bySupplierCode = FindBySupplierItemCode(itemCode);
                if (bySupplierCode is not null)
                {
                    return Matched(MatchedBy.SupplierItemCode, bySupplierCode);
                }

                if (productsByCode.TryGetValue(itemCode, out var byProductCode))
                {
                    return Matched(MatchedBy.ProductCode, byProductCode);
                }
            }

            var description = ProductSearchIndex.Normalize(line.Description);
            var byDescription = aliases.FirstOrDefault(a => a.NormalizedDescription == description);
            return byDescription is null ? null : Matched(MatchedBy.Description, byDescription.Product);
        }

        /// <summary>
        /// The supplier's code identifies a product only when every alias with that code agrees.
        /// If the supplier used it for two different products, it is ambiguous and is not reused.
        /// </summary>
        private ProductCandidate? FindBySupplierItemCode(string itemCode)
        {
            var products = aliases
                .Where(a => a.SupplierItemCode == itemCode)
                .Select(a => a.Product)
                .DistinctBy(p => p.ProductId)
                .ToList();
            return products.Count == 1 ? products[0] : null;
        }

        private static LineMatch Matched(MatchedBy reason, ProductCandidate product) =>
            new(LineMatchStatus.Matched, reason, [product]);
    }
}
