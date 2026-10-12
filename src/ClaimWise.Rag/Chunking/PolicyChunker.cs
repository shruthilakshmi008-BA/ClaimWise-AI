using System.Text;
using System.Text.RegularExpressions;
using ClaimWise.Rag.Extraction;

namespace ClaimWise.Rag.Chunking;

/// <summary>
/// Structure-aware chunker for policy documents. It detects headings (Markdown, numbered clauses such as
/// "4.2 Dental limits", and short ALL-CAPS lines), never lets a chunk cross a clause boundary, and splits long
/// clauses on sentence boundaries with a small overlap. Every chunk keeps its clause reference, section path,
/// page number and source offsets.
/// </summary>
public sealed partial class PolicyChunker(ChunkingOptions? options = null)
{
    private readonly ChunkingOptions _options = options ?? new ChunkingOptions();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$")]
    private static partial Regex MarkdownHeading();

    // "4.2 Title", "4.2. Title", "Section 4.2: Title", "Clause 7 - Title"
    [GeneratedRegex(@"^(?:(?:Section|Clause|Article)\s+)?(\d{1,3}(?:\.\d{1,3}){0,4})[.):\-]?\s+([\p{Lu}][^\r\n]{1,100})$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedHeading();

    // A body paragraph that opens with "2.1 " is a numbered clause and becomes its own chunk(s).
    [GeneratedRegex(@"^(\d{1,3}(?:\.\d{1,3})+)[.)]?\s+\S")]
    private static partial Regex ClauseStart();

    [GeneratedRegex(@"[.!?:;]$")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{Lu}\p{N}(""'])")]
    private static partial Regex SentenceSplit();

    private sealed record Line(string Text, int Offset, int Page);

    private sealed class Section
    {
        public string? Heading;
        public string? ClauseRef;
        public string Path = "";
        public readonly List<List<Line>> Paragraphs = [];
    }

    public IReadOnlyList<TextChunk> Chunk(ExtractedDocument document)
    {
        var sections = BuildSections(document);
        var chunks = new List<TextChunk>();
        foreach (var section in sections)
            foreach (var chunk in ChunkSection(section))
                chunks.Add(chunk with { Ordinal = chunks.Count });
        return chunks;
    }

    // ---------- structure ----------

    private List<Section> BuildSections(ExtractedDocument document)
    {
        var sections = new List<Section>();
        var headingStack = new List<(int Level, string Title)>();
        var current = new Section { Path = "" };
        var paragraph = new List<Line>();
        sections.Add(current);

        void FlushParagraph()
        {
            if (paragraph.Count > 0) current.Paragraphs.Add(paragraph);
            paragraph = [];
        }

        int baseOffset = 0;
        foreach (var page in document.Pages)
        {
            int offset = baseOffset;
            foreach (var rawLine in page.Text.Split('\n'))
            {
                var text = rawLine.Trim();
                var lineOffset = offset;
                offset += rawLine.Length + 1;

                if (text.Length == 0) { FlushParagraph(); continue; }

                if (TryParseHeading(text, out var level, out var clause, out var title))
                {
                    FlushParagraph();
                    while (headingStack.Count > 0 && headingStack[^1].Level >= level) headingStack.RemoveAt(headingStack.Count - 1);
                    var label = clause is null ? title : $"{clause} {title}";
                    headingStack.Add((level, label));

                    current = new Section
                    {
                        Heading = label,
                        ClauseRef = clause,
                        Path = string.Join(" > ", headingStack.Select(h => h.Title))
                    };
                    sections.Add(current);
                    continue;
                }

                var clauseMatch = ClauseStart().Match(text);
                if (clauseMatch.Success)
                {
                    FlushParagraph();
                    // New sub-section under the same heading so the chunk can cite its own clause number.
                    current = new Section { Heading = current.Heading, ClauseRef = clauseMatch.Groups[1].Value, Path = current.Path };
                    sections.Add(current);
                }

                paragraph.Add(new Line(text, lineOffset, page.Number));
            }
            FlushParagraph();
            baseOffset += page.Text.Length + 2; // pages are joined with a blank line in FullText
        }

        return sections.Where(s => s.Paragraphs.Count > 0).ToList();
    }

    private static bool TryParseHeading(string line, out int level, out string? clause, out string title)
    {
        level = 0; clause = null; title = "";

        var md = MarkdownHeading().Match(line);
        if (md.Success)
        {
            level = md.Groups[1].Length;
            title = md.Groups[2].Value.Trim();
            var inner = NumberedHeading().Match(title);
            if (inner.Success) { clause = inner.Groups[1].Value; title = inner.Groups[2].Value.Trim(); }
            return true;
        }

        var num = NumberedHeading().Match(line);
        if (num.Success && !SentenceEnd().IsMatch(line) && TextAnalysis.WordCount(line) <= 14)
        {
            clause = num.Groups[1].Value;
            title = num.Groups[2].Value.Trim();
            level = clause.Count(c => c == '.') + 1;
            return true;
        }

        if (line.Length is >= 4 and <= 80 && !SentenceEnd().IsMatch(line) &&
            line.Any(char.IsLetter) && line.Where(char.IsLetter).All(char.IsUpper))
        {
            title = line;
            level = 1;
            return true;
        }

        return false;
    }

    // ---------- splitting ----------

    private IEnumerable<TextChunk> ChunkSection(Section section)
    {
        // Units are sentences, each remembering its source offsets/page.
        var units = new List<(string Text, int Start, int End, int Page)>();
        foreach (var paragraph in section.Paragraphs)
        {
            var text = TextAnalysis.NormalizeWhitespace(string.Join(" ", paragraph.Select(l => l.Text)));
            var start = paragraph[0].Offset;
            var end = paragraph[^1].Offset + paragraph[^1].Text.Length;
            var page = paragraph[0].Page;

            foreach (var sentence in SentenceSplit().Split(text))
            {
                foreach (var piece in HardSplit(sentence))
                    units.Add((piece, start, end, page));
            }
        }

        var buffer = new List<(string Text, int Start, int End, int Page)>();
        int bufferWords = 0;

        TextChunk? Emit()
        {
            if (buffer.Count == 0) return null;
            var text = string.Join(" ", buffer.Select(u => u.Text));
            var words = TextAnalysis.WordCount(text);
            if (words < _options.MinWords && section.Heading is null) return null;
            return new TextChunk(0, text, section.Heading, section.ClauseRef,
                section.Path.Length > 0 ? section.Path : "(document start)",
                buffer[0].Page, buffer.Min(u => u.Start), buffer.Max(u => u.End), words);
        }

        foreach (var unit in units)
        {
            var words = TextAnalysis.WordCount(unit.Text);
            if (bufferWords + words > _options.MaxWords && buffer.Count > 0)
            {
                var chunk = Emit();
                if (chunk is not null) yield return chunk;

                // Carry the last sentence(s) forward as overlap so a fact split across the boundary stays findable.
                var carry = new List<(string Text, int Start, int End, int Page)>();
                int carryWords = 0;
                for (int i = buffer.Count - 1; i >= 0; i--)
                {
                    var w = TextAnalysis.WordCount(buffer[i].Text);
                    if (carryWords + w > _options.OverlapWords) break;
                    carry.Insert(0, buffer[i]);
                    carryWords += w;
                }
                buffer = carry;
                bufferWords = carryWords;
            }

            buffer.Add(unit);
            bufferWords += words;
        }

        var last = Emit();
        if (last is not null) yield return last;
    }

    /// <summary>A single "sentence" longer than the limit (tables, run-on lists) is cut on word boundaries.</summary>
    private IEnumerable<string> HardSplit(string sentence)
    {
        if (TextAnalysis.WordCount(sentence) <= _options.MaxWords) { yield return sentence; yield break; }

        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        int count = 0;
        foreach (var word in words)
        {
            if (count == _options.MaxWords) { yield return sb.ToString(); sb.Clear(); count = 0; }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
            count++;
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
