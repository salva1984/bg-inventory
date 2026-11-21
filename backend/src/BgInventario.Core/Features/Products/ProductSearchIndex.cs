using System.Globalization;
using System.Text;
using BgInventario.Core.Entities;

namespace BgInventario.Core.Features.Products;

public static class ProductSearchIndex
{
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

    public static double CalculateDiceCoefficient(int sharedTrigrams, int trigramsACount, int trigramsBCount)
    {
        var hasNoText = trigramsACount + trigramsBCount == 0;
        if (hasNoText)
        {
            return 0;
        }

        var score = 2.0 * sharedTrigrams / (trigramsACount + trigramsBCount);
        return Math.Round(score, 2);
    }

    public static double TrigramsSimilarity(string a, string b)
    {
        var trigramsA = GetTrigrams(a);
        var trigramsB = GetTrigrams(b);
        var shared = trigramsA.Count(trigramsB.Contains);
        return CalculateDiceCoefficient(shared, trigramsA.Count, trigramsB.Count);
    }

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
