using System.Text;

namespace ClaimWise.Rag.Extraction;

/// <summary>Extracts .txt and .md files (UTF-8, BOM-aware). Refuses binary content.</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".txt", ".md"];

    public ExtractedDocument Extract(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (Array.IndexOf(bytes, (byte)0) >= 0)
            throw new ExtractionException("The file looks binary, not text. Upload a .txt or .md file, or a PDF/DOCX.");

        string text;
        try
        {
            // throwOnInvalidBytes: a Latin-1 file mis-read as UTF-8 would silently corrupt every quote.
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ExtractionException("The file is not valid UTF-8 text. Re-save it as UTF-8.", ex);
        }

        text = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(text))
            throw new ExtractionException("The file contains no text.");

        return new ExtractedDocument([new ExtractedPage(1, text)]);
    }
}
