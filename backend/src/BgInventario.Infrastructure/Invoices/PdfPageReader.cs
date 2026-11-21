using BgInventario.Core.Common;
using BgInventario.Core.Features.Invoices;
using PDFtoImage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace BgInventario.Infrastructure.Invoices;

internal abstract record InvoicePage(int Number);

internal sealed record TextPage(int Number, string Text) : InvoicePage(Number);

internal sealed record ScannedPage(int Number, byte[] Image) : InvoicePage(Number);

internal static class PdfPageReader
{
    private const int MinLettersForTextPage = 20;

    // Minimum share of readable letters: fonts without a Unicode map give replacement or
    // private-use characters, and then the image beats the text.
    private const double MinReadableRatio = 0.9;

    private const int RenderDpi = 200;

    internal const int MaxPages = 20;

    // Characters sent to the model (~25,000 tokens): bounds the cost of one analysis.
    internal const int MaxTextCharacters = 100_000;

    public static Result<List<InvoicePage>> ReadPages(byte[] pdf, int maxScannedPages)
    {
        using var document = PdfDocument.Open(pdf);
        if (ExceedsPageLimit(document))
        {
            return InvoiceErrors.DocumentTooLarge;
        }

        // Text pages are read now (cheap); scanned ones are only counted, since rendering is
        // expensive and the limits are checked first.
        var (textPages, scannedPageNumbers) = SplitByTextLayer(document);

        if (ExceedsTextLimit(textPages))
        {
            return InvoiceErrors.DocumentTooLarge;
        }

        if (ExceedsScannedPageLimit(scannedPageNumbers, maxScannedPages))
        {
            return InvoiceErrors.TooManyScannedPages;
        }

        var scannedPages = RenderScannedPages(pdf, scannedPageNumbers);
        return InPageOrder(textPages, scannedPages);
    }

    private static bool ExceedsPageLimit(PdfDocument document) => document.NumberOfPages > MaxPages;

    private static bool ExceedsTextLimit(List<TextPage> textPages) =>
        textPages.Sum(page => page.Text.Length) > MaxTextCharacters;

    private static bool ExceedsScannedPageLimit(List<int> scannedPageNumbers, int maxScannedPages) =>
        scannedPageNumbers.Count > maxScannedPages;

    private static (List<TextPage> TextPages, List<int> ScannedPageNumbers) SplitByTextLayer(PdfDocument document)
    {
        var textPages = new List<TextPage>();
        var scannedPageNumbers = new List<int>();

        foreach (var page in document.GetPages())
        {
            var letters = WithoutOverlappingDuplicates(page.Letters);
            if (HasTextLayer(letters))
            {
                textPages.Add(ReadTextPage(page.Number, letters));
            }
            else
            {
                scannedPageNumbers.Add(page.Number);
            }
        }

        return (textPages, scannedPageNumbers);
    }

    private static TextPage ReadTextPage(int pageNumber, List<Letter> letters)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters).ToList();
        var text = PageLayoutText.Build(words);
        return new TextPage(pageNumber, text);
    }

    private static List<ScannedPage> RenderScannedPages(byte[] pdf, List<int> scannedPageNumbers)
    {
        var scannedPages = new List<ScannedPage>();
        foreach (var pageNumber in scannedPageNumbers)
        {
            var pageIndex = pageNumber - 1; // PdfPig counts pages from 1, PDFtoImage from 0
            var image = RenderPng(pdf, pageIndex);
            scannedPages.Add(new ScannedPage(pageNumber, image));
        }

        return scannedPages;
    }

    private static List<InvoicePage> InPageOrder(List<TextPage> textPages, List<ScannedPage> scannedPages)
    {
        List<InvoicePage> pages = [.. textPages, .. scannedPages];
        return pages.OrderBy(page => page.Number).ToList();
    }

    /// <summary>
    /// Some generators fake bold by drawing each letter twice ("TToottaall"): letters with the
    /// same value at the same rounded position are kept once.
    /// </summary>
    private static List<Letter> WithoutOverlappingDuplicates(IReadOnlyList<Letter> letters) =>
        letters
            .DistinctBy(letter => (
                letter.Value,
                X: Math.Round(letter.StartBaseLine.X),
                Y: Math.Round(letter.StartBaseLine.Y)))
            .ToList();

    private static bool HasTextLayer(IReadOnlyList<Letter> letters)
    {
        var visible = letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).ToList();
        if (visible.Count < MinLettersForTextPage)
        {
            return false;
        }

        var readable = visible.Count(letter => letter.Value.All(IsReadableCharacter));
        var readableRatio = readable / (double)visible.Count;
        return readableRatio >= MinReadableRatio;
    }

    private static bool IsReadableCharacter(char character)
    {
        if (character == '�') // Unicode replacement character
        {
            return false;
        }

        var category = char.GetUnicodeCategory(character);
        return category is not (System.Globalization.UnicodeCategory.PrivateUse
            or System.Globalization.UnicodeCategory.Control
            or System.Globalization.UnicodeCategory.OtherNotAssigned);
    }

    private static byte[] RenderPng(byte[] pdf, int pageIndex)
    {
        // PDFium ships native binaries only for these systems. Also satisfies analyzer CA1416.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            using var image = new MemoryStream();
            Conversion.SavePng(image, pdf, pageIndex, options: new RenderOptions { Dpi = RenderDpi });
            return image.ToArray();
        }

        throw new PlatformNotSupportedException("Rendering PDF pages requires Linux, Windows or macOS.");
    }
}

internal static class PageLayoutText
{
    private const double SameLineTolerance = 0.5;

    public static string Build(IReadOnlyList<Word> words)
    {
        var visibleWords = words.Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        if (visibleWords.Count == 0)
        {
            return string.Empty;
        }

        var totalWidth = visibleWords.Sum(word => word.BoundingBox.Width);
        var totalCharacters = visibleWords.Sum(word => word.Text.Length);
        var characterWidth = Math.Max(totalWidth / totalCharacters, 1);

        var typicalHeight = visibleWords.Average(word => word.BoundingBox.Height);
        var lineTolerance = typicalHeight * SameLineTolerance;
        var pageLeft = visibleWords.Min(word => word.BoundingBox.Left);

        var lines = GroupIntoLines(visibleWords, lineTolerance);
        var renderedLines = lines.Select(line => RenderLine(line, pageLeft, characterWidth));
        return string.Join('\n', renderedLines);
    }

    /// <summary>Top to bottom; in PDF coordinates the vertical axis grows upwards.</summary>
    private static List<List<Word>> GroupIntoLines(List<Word> words, double lineTolerance)
    {
        var lines = new List<List<Word>>();
        List<Word>? currentLine = null;
        var currentBaseline = double.NaN;

        foreach (var word in words.OrderByDescending(word => word.BoundingBox.Bottom))
        {
            var startsNewLine = currentLine is null || Math.Abs(word.BoundingBox.Bottom - currentBaseline) > lineTolerance;
            if (startsNewLine)
            {
                currentLine = [];
                lines.Add(currentLine);
                currentBaseline = word.BoundingBox.Bottom;
            }

            currentLine!.Add(word);
        }

        return lines;
    }

    private static string RenderLine(List<Word> line, double pageLeft, double characterWidth)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var word in line.OrderBy(word => word.BoundingBox.Left))
        {
            var column = (int)Math.Round((word.BoundingBox.Left - pageLeft) / characterWidth);
            var spaces = builder.Length == 0 ? column : Math.Max(column - builder.Length, 1);
            builder.Append(' ', spaces);
            builder.Append(word.Text);
        }

        return builder.ToString();
    }
}
