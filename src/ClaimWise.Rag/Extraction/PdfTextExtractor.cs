using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ClaimWise.Rag.Extraction;

/// <summary>Extracts the text layer of a PDF page by page. Scanned (image-only) PDFs are rejected: OCR is not supported.</summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public ExtractedDocument Extract(Stream content)
    {
        try
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            buffer.Position = 0;

            using var pdf = PdfDocument.Open(buffer);
            var pages = new List<ExtractedPage>();
            foreach (var page in pdf.GetPages())
            {
                var text = ContentOrderTextExtractor.GetText(page).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (text.Length > 0) pages.Add(new ExtractedPage(page.Number, text));
            }

            if (pages.Count == 0)
                throw new ExtractionException("The PDF has no extractable text layer (it may be a scan). OCR is not supported.");
            return new ExtractedDocument(pages);
        }
        catch (ExtractionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExtractionException("The PDF could not be read. It may be corrupt, encrypted or not a PDF.", ex);
        }
    }
}
