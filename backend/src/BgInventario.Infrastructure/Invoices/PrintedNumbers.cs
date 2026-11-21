using System.Globalization;
using System.Text.RegularExpressions;

namespace BgInventario.Infrastructure.Invoices;

internal static partial class PrintedNumbers
{
    [GeneratedRegex(@"\d(?:[\d.,]*\d)?")]
    private static partial Regex NumberToken();

    /// <summary>
    /// Each token is read both ways: "1.234,56" (usual in Ecuador) and "1,234.56". A wrong
    /// reading adds a value no line has, so it is harmless.
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
