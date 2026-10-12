namespace ClaimWise.Rag.Extraction;

/// <summary>Picks the extractor for a file name. Unknown extensions are refused rather than guessed.</summary>
public sealed class TextExtractorRegistry(IEnumerable<ITextExtractor> extractors)
{
    private readonly Dictionary<string, ITextExtractor> _byExtension = extractors
        .SelectMany(e => e.SupportedExtensions.Select(ext => (ext, e)))
        .ToDictionary(x => x.ext, x => x.e, StringComparer.OrdinalIgnoreCase);

    public static TextExtractorRegistry CreateDefault() =>
        new([new PlainTextExtractor(), new PdfTextExtractor(), new DocxTextExtractor()]);

    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;

    public bool IsSupported(string fileName) => _byExtension.ContainsKey(Path.GetExtension(fileName));

    public ExtractedDocument Extract(string fileName, Stream content)
    {
        if (!_byExtension.TryGetValue(Path.GetExtension(fileName), out var extractor))
            throw new ExtractionException($"Unsupported file type. Supported: {string.Join(", ", SupportedExtensions.Order())}.");
        return extractor.Extract(content);
    }
}
