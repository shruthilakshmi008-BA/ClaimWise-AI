using System.IO.Compression;
using System.Text;
using System.Xml;

namespace ClaimWise.Rag.Extraction;

/// <summary>
/// Extracts paragraph text from a .docx (a ZIP of XML) without extra dependencies. Heading styles become Markdown
/// headings so the chunker can keep section structure. Guards against zip bombs and XML entity attacks.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const long MaxDocumentXmlBytes = 25 * 1024 * 1024;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx"];

    public ExtractedDocument Extract(Stream content)
    {
        try
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            buffer.Position = 0;

            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml")
                        ?? throw new ExtractionException("The file is not a valid DOCX (word/document.xml is missing).");
            if (entry.Length > MaxDocumentXmlBytes)
                throw new ExtractionException("The DOCX is too large to process.");

            var text = ReadParagraphs(entry);
            if (string.IsNullOrWhiteSpace(text))
                throw new ExtractionException("The DOCX contains no text.");
            return new ExtractedDocument([new ExtractedPage(1, text)]);
        }
        catch (ExtractionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            throw new ExtractionException("The DOCX could not be read. It may be corrupt or password protected.", ex);
        }
    }

    private static string ReadParagraphs(ZipArchiveEntry entry)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxDocumentXmlBytes };
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);

        var output = new StringBuilder();
        var paragraph = new StringBuilder();
        int headingLevel = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == WordNs)
            {
                switch (reader.LocalName)
                {
                    case "p":
                        paragraph.Clear();
                        headingLevel = 0;
                        break;
                    case "pStyle":
                        var style = reader.GetAttribute("val", WordNs) ?? "";
                        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(style.AsSpan(7), out var level))
                            headingLevel = Math.Clamp(level, 1, 6);
                        else if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
                            headingLevel = 1;
                        break;
                    case "t":
                        paragraph.Append(reader.ReadElementContentAsString());
                        break;
                    case "tab":
                        paragraph.Append('\t');
                        break;
                    case "br":
                        paragraph.Append('\n');
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.NamespaceURI == WordNs && reader.LocalName == "p")
            {
                var line = paragraph.ToString().Trim();
                if (line.Length > 0)
                    output.Append(headingLevel > 0 ? new string('#', headingLevel) + " " + line : line).Append("\n\n");
                else
                    output.Append('\n');
            }
        }

        return output.ToString().Trim();
    }
}
