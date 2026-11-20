// Approximate text search over the catalog, built on trigrams (the idea behind PostgreSQL's pg_trgm).
// SQL Server has no trigram index, so each product stores its trigrams in the ProductTrigrams table
// and an indexed GROUP BY counts how many of them a search text shares with each product.
// The same similarity also compares two short texts in memory (e.g. supplier names).
using System.Globalization;
using System.Text;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Products;

public static class ProductSearchIndex
{
    /// <summary>
    /// Lowercase, without accents, and with every symbol turned into a space:
    /// "Tóner HP-85A (negro)" → "toner hp 85a negro". Invoices and the catalog rarely agree on
    /// casing, accents or punctuation, so they are compared in this form.
    /// </summary>
    public static string Normalize(string text)
    {
        // FormD splits "ó" into "o" + a combining accent, which the loop below drops.
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // avoids a leading space

        foreach (var character in decomposed)
        {
            var isAccent = CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark;
            if (isAccent)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Distinct trigrams of a text, computed word by word like pg_trgm: each word is padded with two
    /// spaces in front and one behind, so "24" yields "  2", " 24" and "24 ". The padding lets
    /// short words (sizes, models) count and gives weight to how words start.
    /// </summary>
    public static HashSet<string> GetTrigrams(string text)
    {
        var trigrams = new HashSet<string>(StringComparer.Ordinal);
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var padded = $"  {word} ";
            for (var start = 0; start + ProductTrigramLimits.TrigramLength <= padded.Length; start++)
            {
                trigrams.Add(padded.Substring(start, ProductTrigramLimits.TrigramLength));
            }
        }

        return trigrams;
    }

    /// <summary>
    /// Dice coefficient: 2·shared / (trigrams of one text + trigrams of the other). It is 1 for the
    /// same text and drops both when one text has words the other lacks and the other way round.
    /// Rounded because it is only used to compare and rank.
    /// </summary>
    public static double Dice(int sharedTrigrams, int trigramsA, int trigramsB)
    {
        var hasNoText = trigramsA + trigramsB == 0;
        if (hasNoText)
        {
            return 0;
        }

        var score = 2.0 * sharedTrigrams / (trigramsA + trigramsB);
        return Math.Round(score, 2);
    }

    /// <summary>Dice similarity of two texts computed in memory, for short lists like supplier names.</summary>
    public static double Similarity(string a, string b)
    {
        var trigramsA = GetTrigrams(a);
        var trigramsB = GetTrigrams(b);
        var shared = trigramsA.Count(trigramsB.Contains);
        return Dice(shared, trigramsA.Count, trigramsB.Count);
    }

    /// <summary>Text a product is found by: its name and its code.</summary>
    public static HashSet<string> GetProductTrigrams(Product product) =>
        GetTrigrams($"{product.Name} {product.Code}");

    /// <summary>
    /// Makes the product's trigram rows match its current name and code. Only the difference is
    /// added or removed: EF would reject removing and re-adding a row with the same key.
    /// The product must be loaded with its <see cref="Product.Trigrams"/>.
    /// </summary>
    public static void Refresh(Product product)
    {
        var wanted = GetProductTrigrams(product);

        product.Trigrams.RemoveAll(existing => !wanted.Contains(existing.Trigram));

        var existingTrigrams = product.Trigrams.Select(t => t.Trigram).ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(trigram => !existingTrigrams.Contains(trigram));
        product.Trigrams.AddRange(missing.Select(trigram => new ProductTrigram { Trigram = trigram }));
    }
}
