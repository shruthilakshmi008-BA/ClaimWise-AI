namespace ClaimWise.Rag.Extraction;

/// <summary>One page of extracted text. Plain-text and DOCX sources have a single logical page (1).</summary>
public sealed record ExtractedPage(int Number, string Text);

public sealed record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages)
{
    public string FullText => string.Join("\n\n", Pages.Select(p => p.Text));
}

/// <summary>Raised when a file cannot be turned into usable text. The message is safe to show to the uploader.</summary>
public sealed class ExtractionException(string message, Exception? inner = null) : Exception(message, inner);

public interface ITextExtractor
{
    /// <summary>File extensions (lower-case, with dot) this extractor handles.</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    ExtractedDocument Extract(Stream content);
}
