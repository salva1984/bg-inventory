using BgInventario.Core.Common;
using BgInventario.Core.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace BgInventario.Core.Features.Invoices;

/// <summary>
/// Finds which catalog product each invoice line refers to. It never decides alone: it returns a
/// status and candidates, and the user confirms. Rules, from most to least reliable:
///
/// 1. Known equivalence: the supplier's item code or the description was already confirmed for
///    this supplier (an alias), or the item code is one of our product codes → Matched.
/// 2. Approximate search: products sharing the most trigrams with the description, scored with
///    the Dice coefficient → Suggested when the best one is close, NeedsReview when all are weak.
/// 3. Nothing usable (a too generic description, no similar product) → Manual.
///
/// Every step is an indexed query; the catalog is never loaded into memory.
/// </summary>
public sealed class InvoiceLineMatcher(IAppDbContext db)
{
    // How many products the trigram query brings before scoring them. Larger than the candidates
    // returned because the query ranks by shared trigrams, and the score also penalizes long names.
    internal const int CandidatePoolSize = 20;

    // Candidates shown to the user per line.
    internal const int MaxCandidates = 5;

    // Dice score from which the best candidate is offered as a suggestion.
    internal const double SuggestedMinScore = 0.45;

    // Below this score a product is noise, not a candidate.
    internal const double CandidateMinScore = 0.2;

    // A description with fewer trigrams (e.g. "AB") matches almost anything; it goes straight to Manual.
    internal const int MinSearchTrigrams = 4;

    // Score of a product identified by a known equivalence.
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

    /// <summary>
    /// Loads in two queries everything the invoice could already be known by: the supplier's aliases
    /// for these descriptions or item codes, and our products whose code appears as an item code.
    /// </summary>
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

    /// <summary>Approximate search: an indexed GROUP BY over the trigram table, then the Dice score.</summary>
    private async Task<LineMatch> SearchCandidatesAsync(string description, CancellationToken ct)
    {
        var lineTrigrams = ProductSearchIndex.GetTrigrams(description);
        var isTooGeneric = lineTrigrams.Count < MinSearchTrigrams;
        if (isTooGeneric)
        {
            return ManualMatch;
        }

        // Products that share the most trigrams with the line.
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

        // Name, code and trigram count of only those products, to compute the score.
        var poolIds = pool.Select(x => x.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => poolIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, TrigramCount = p.Trigrams.Count })
            .ToListAsync(ct);

        var sharedByProduct = pool.ToDictionary(x => x.ProductId, x => x.SharedTrigrams);
        var candidates = products
            .Select(p =>
            {
                var score = ProductSearchIndex.Dice(sharedByProduct[p.Id], lineTrigrams.Count, p.TrigramCount);
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

    /// <summary>Item codes are compared like our product codes: trimmed and in upper case.</summary>
    internal static string? NormalizeItemCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : ProductService.NormalizeCode(code);

    private sealed record KnownAlias(string NormalizedDescription, string? SupplierItemCode, ProductCandidate Product);

    /// <summary>The equivalences loaded for one invoice, applied line by line in order of reliability.</summary>
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
