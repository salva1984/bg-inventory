using System.Globalization;
using System.Text.RegularExpressions;

namespace BgInventario.Infrastructure.Invoices;

/// <summary>
/// The numbers printed in a text. When a PDF has a text layer, every quantity and price the LLM
/// returns must be one of them; a number that is not there was misread or made up.
/// </summary>
internal static partial class PrintedNumbers
{
    // A digit, optionally followed by digits and separators and ending in a digit: 7, 4.50, 1.234,56.
    [GeneratedRegex(@"\d(?:[\d.,]*\d)?")]
    private static partial Regex NumberToken();

    /// <summary>
    /// Each token is read both ways, because invoices use either convention: "1.234,56" (comma as
    /// decimal separator, usual in Ecuador) and "1,234.56". A wrong reading only adds a value that
    /// no line will have, so keeping both is harmless.
    /// </summary>
    public static HashSet<decimal> Find(string text)
    {
        var numbers = new HashSet<decimal>();

        foreach (Match token in NumberToken().Matches(text))
        {
            var withDecimalPoint = token.Value.Replace(",", "");
            var withDecimalComma = token.Value.Replace(".", "").Replace(',', '.');

            AddIfNumber(numbers, withDecimalPoint);
            AddIfNumber(numbers, withDecimalComma);
        }

        return numbers;
    }

    private static void AddIfNumber(HashSet<decimal> numbers, string candidate)
    {
        if (decimal.TryParse(candidate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            numbers.Add(value);
        }
    }
}
