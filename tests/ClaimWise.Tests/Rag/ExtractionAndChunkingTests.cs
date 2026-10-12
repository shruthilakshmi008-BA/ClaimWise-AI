using System.IO.Compression;
using System.Text;
using ClaimWise.Rag;
using ClaimWise.Rag.Chunking;
using ClaimWise.Rag.Extraction;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace ClaimWise.Tests.Rag;

public class ExtractionTests
{
    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void PlainText_ExtractsAndNormalisesLineEndings()
    {
        var doc = new PlainTextExtractor().Extract(Bytes("﻿Line one\r\nLine two"));
        Assert.Equal("Line one\nLine two", doc.FullText);
        Assert.Single(doc.Pages);
    }

    [Fact]
    public void PlainText_RejectsBinaryAndEmptyAndInvalidUtf8()
    {
        var ex1 = Assert.Throws<ExtractionException>(() => new PlainTextExtractor().Extract(new MemoryStream([1, 2, 0, 3])));
        Assert.Contains("binary", ex1.Message);
        Assert.Throws<ExtractionException>(() => new PlainTextExtractor().Extract(Bytes("   \n ")));
        Assert.Throws<ExtractionException>(() => new PlainTextExtractor().Extract(new MemoryStream([0xC3, 0x28])));
    }

    [Fact]
    public void Pdf_ExtractsTextPerPage_AndKeepsPageNumbers()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(595, 842).AddText("Annual outpatient limit is 5000", 12, new PdfPoint(40, 780), font);
        builder.AddPage(595, 842).AddText("Dental waiting period is 90 days", 12, new PdfPoint(40, 780), font);

        var doc = new PdfTextExtractor().Extract(new MemoryStream(builder.Build()));

        Assert.Equal(2, doc.Pages.Count);
        Assert.Contains("outpatient limit", doc.Pages[0].Text);
        Assert.Equal(2, doc.Pages[1].Number);
        Assert.Contains("waiting period", doc.Pages[1].Text);
    }

    [Fact]
    public void Pdf_RejectsGarbageAndImageOnlyPdfs()
    {
        Assert.Throws<ExtractionException>(() => new PdfTextExtractor().Extract(Bytes("not a pdf")));

        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842); // blank page: no text layer
        var ex = Assert.Throws<ExtractionException>(() => new PdfTextExtractor().Extract(new MemoryStream(builder.Build())));
        Assert.Contains("no extractable text", ex.Message);
    }

    private static MemoryStream Docx(string bodyXml)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(entry.Open());
            w.Write($"""<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{bodyXml}</w:body></w:document>""");
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Docx_ExtractsParagraphsAndHeadingStyles()
    {
        var doc = new DocxTextExtractor().Extract(Docx(
            """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>2. Outpatient benefits</w:t></w:r></w:p><w:p><w:r><w:t>The plan pays </w:t></w:r><w:r><w:t>80%.</w:t></w:r></w:p>"""));
        Assert.StartsWith("# 2. Outpatient benefits", doc.FullText);
        Assert.Contains("The plan pays 80%.", doc.FullText);
    }

    [Fact]
    public void Docx_RejectsNonDocxAndXmlEntityTricks()
    {
        Assert.Throws<ExtractionException>(() => new DocxTextExtractor().Extract(Bytes("not a zip")));
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            w.Write("""<?xml version="1.0"?><!DOCTYPE d [<!ENTITY x SYSTEM "file:///etc/passwd">]><d>&x;</d>""");
        }
        ms.Position = 0;
        Assert.Throws<ExtractionException>(() => new DocxTextExtractor().Extract(ms));
    }

    [Fact]
    public void Registry_RefusesUnknownExtensions()
    {
        var registry = TextExtractorRegistry.CreateDefault();
        Assert.True(registry.IsSupported("Policy.PDF"));
        Assert.False(registry.IsSupported("malware.exe"));
        Assert.Throws<ExtractionException>(() => registry.Extract("x.exe", Bytes("x")));
    }
}

public class ChunkerTests
{
    private static ExtractedDocument Doc(string text) => new([new ExtractedPage(1, text)]);

    [Fact]
    public void Chunker_KeepsClauseReferenceSectionPathAndPage()
    {
        var chunks = new PolicyChunker().Chunk(Doc(
            "# Policy A\n\n## 2. Outpatient benefits\n\n2.1 Annual limit. The plan pays up to 5,000 per year.\n\n2.2 Co-payment. The member pays 20% of each claim."));

        var c21 = Assert.Single(chunks, c => c.ClauseRef == "2.1");
        Assert.Contains("5,000", c21.Text);
        Assert.Equal("2 Outpatient benefits", c21.Heading);
        Assert.Equal("Policy A > 2 Outpatient benefits", c21.SectionPath);
        Assert.Equal(1, c21.PageNumber);
        Assert.DoesNotContain("20%", c21.Text);          // clauses never merge
        Assert.Contains(chunks, c => c.ClauseRef == "2.2" && c.Text.Contains("20%"));
    }

    [Fact]
    public void Chunker_SplitsLongClausesWithOverlap_AndRespectsMaxWords()
    {
        var sentences = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"Sentence number {i} describes a covered benefit in some detail here."));
        var chunks = new PolicyChunker(new ChunkingOptions { MaxWords = 60, OverlapWords = 20 })
            .Chunk(Doc($"## 3. Long section\n\n3.1 {sentences}"));

        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.True(c.WordCount <= 60 + 20, $"chunk {c.Ordinal} has {c.WordCount} words"));
        Assert.All(chunks, c => Assert.Equal("3.1", c.ClauseRef));
        // Overlap: the last sentence of a chunk reappears at the start of the next.
        var firstEnd = chunks[0].Text.Split(". ").Last().TrimEnd('.');
        Assert.Contains(firstEnd, chunks[1].Text);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void Chunker_RecordsPageNumbersForMultiPageDocuments()
    {
        var doc = new ExtractedDocument([
            new ExtractedPage(1, "## 1. First\n\n1.1 Alpha clause text here."),
            new ExtractedPage(2, "## 2. Second\n\n2.1 Beta clause text here.")]);

        var chunks = new PolicyChunker().Chunk(doc);

        Assert.Equal(1, chunks.Single(c => c.ClauseRef == "1.1").PageNumber);
        Assert.Equal(2, chunks.Single(c => c.ClauseRef == "2.1").PageNumber);
    }

    [Fact]
    public void Chunker_OffsetsPointBackIntoTheSourceText()
    {
        var text = "## 1. Scope\n\n1.1 The plan covers outpatient care.\n\n1.2 Dental care is excluded.";
        var doc = Doc(text);
        foreach (var chunk in new PolicyChunker().Chunk(doc))
        {
            var span = doc.FullText[chunk.StartOffset..chunk.EndOffset];
            Assert.Contains(chunk.Text.Split(' ')[^1].TrimEnd('.'), span);
        }
    }

    [Fact]
    public void Chunker_DetectsAllCapsAndNumberedHeadings()
    {
        var chunks = new PolicyChunker().Chunk(Doc("GENERAL EXCLUSIONS\n\nCosmetic procedures are not covered by this plan.\n\n4.2 Dental limits\n\nDental cover is capped at 1,500 per year."));
        Assert.Contains(chunks, c => c.Heading == "GENERAL EXCLUSIONS");
        Assert.Contains(chunks, c => c.Heading == "4.2 Dental limits" && c.ClauseRef == "4.2");
    }

    [Fact]
    public void SamplePolicy_ProducesClauseLevelChunksWithMetadata()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "policies", "gold-plan-policy-2024.md");
        var doc = new PlainTextExtractor().Extract(File.OpenRead(path));
        var chunks = new PolicyChunker().Chunk(doc);

        // The file has 27 numbered clauses; each must be reachable as its own cited unit.
        Assert.Equal(27, chunks.Select(c => c.ClauseRef).Where(r => r is not null).Distinct().Count());
        var limit = Assert.Single(chunks, c => c.ClauseRef == "2.1");
        Assert.Contains("5,000", limit.Text);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.SectionPath)));
    }
}
