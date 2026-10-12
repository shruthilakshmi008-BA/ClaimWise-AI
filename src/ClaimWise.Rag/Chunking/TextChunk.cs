namespace ClaimWise.Rag.Chunking;

/// <summary>
/// A retrievable passage plus the source metadata needed to cite it.
/// <see cref="Text"/> is the verbatim passage (whitespace-normalised); <see cref="Heading"/>/<see cref="SectionPath"/>
/// describe where it sits; offsets point into the document's extracted full text.
/// </summary>
public sealed record TextChunk(
    int Ordinal,
    string Text,
    string? Heading,
    string? ClauseRef,
    string SectionPath,
    int PageNumber,
    int StartOffset,
    int EndOffset,
    int WordCount)
{
    /// <summary>The text that is indexed for search: heading context plus passage.</summary>
    public string IndexText => string.IsNullOrEmpty(Heading) ? Text : $"{Heading}. {Text}";
}

public sealed record ChunkingOptions
{
    public int MaxWords { get; init; } = 160;
    public int OverlapWords { get; init; } = 25;
    public int MinWords { get; init; } = 4;
}
