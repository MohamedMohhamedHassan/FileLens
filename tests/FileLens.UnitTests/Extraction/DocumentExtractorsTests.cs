using System.IO.Compression;
using System.Text;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Extraction;
using FileLens.Infrastructure.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileLens.UnitTests.Extraction;

public sealed class DocumentExtractorsTests
{
    private readonly ITextNormalizer _normalizer = new TextNormalizer();

    private IndexedFile CreateIndexedFile(string path, string ext)
    {
        return new IndexedFile
        {
            Id = new FileEntryId(Guid.NewGuid()),
            RootId = new SearchRootId(Guid.NewGuid()),
            FullPath = path,
            RelativePath = Path.GetFileName(path),
            FileName = Path.GetFileName(path),
            Extension = ext,
            SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };
    }

    [Fact]
    public async Task PlainTextExtractor_ExtractsParagraphsAndNormalizes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, "Paragraph 1: FileLens safe search.\r\n\r\nParagraph 2: Bilingual Arabic normalization تقرير الأرباح.");
            var extractor = new PlainTextExtractor(_normalizer, NullLogger<PlainTextExtractor>.Instance);
            var file = CreateIndexedFile(tempFile, ".txt");

            var doc = await extractor.ExtractAsync(file);

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("FileLens safe search", doc.RawText);
            Assert.Contains("تقرير الارباح", doc.NormalizedText);
            Assert.Equal(2, doc.Chunks.Count);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DelimitedTextExtractor_ExtractsCsvRowsWithChunkLocators()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.csv");
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,Department,Salary");
            for (int i = 1; i <= 60; i++)
            {
                sb.AppendLine($"{i},Employee_{i},Engineering,100000");
            }
            File.WriteAllText(tempFile, sb.ToString());

            var extractor = new DelimitedTextExtractor(_normalizer, NullLogger<DelimitedTextExtractor>.Instance);
            var file = CreateIndexedFile(tempFile, ".csv");

            var doc = await extractor.ExtractAsync(file);

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("Employee_1", doc.RawText);
            Assert.Equal(2, doc.Chunks.Count); // 50 rows per batch -> 2 chunks
            Assert.Equal("Rows 1-50", doc.Chunks[0].LocationReference);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task JsonDocumentExtractor_ExtractsPropertiesAndJsonLines()
    {
        var tempJson = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json");
        var tempJsonl = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(tempJson, "{\"appName\":\"FileLens\",\"version\":\"2.0\",\"features\":[\"FTS5\",\"Bilingual\"]}");
            File.WriteAllText(tempJsonl, "{\"id\":1,\"title\":\"Log entry one\"}\r\n{\"id\":2,\"title\":\"Log entry two\"}");

            var extractor = new JsonDocumentExtractor(_normalizer, NullLogger<JsonDocumentExtractor>.Instance);

            var docJson = await extractor.ExtractAsync(CreateIndexedFile(tempJson, ".json"));
            Assert.Equal(ExtractionStatus.Extracted, docJson.Status);
            Assert.Contains("FileLens", docJson.RawText);
            Assert.Contains("FTS5", docJson.RawText);

            var docJsonl = await extractor.ExtractAsync(CreateIndexedFile(tempJsonl, ".jsonl"));
            Assert.Equal(ExtractionStatus.Extracted, docJsonl.Status);
            Assert.Contains("Log entry one", docJsonl.RawText);
            Assert.Contains("Log entry two", docJsonl.RawText);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
            if (File.Exists(tempJsonl)) File.Delete(tempJsonl);
        }
    }

    [Fact]
    public async Task XmlDocumentExtractor_ExtractsXmlSafelyWithoutDtd()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempFile, "<Project Name=\"FileLens\"><Item Type=\"Document\"><Title>System Architecture Overview</Title></Item></Project>");
            var extractor = new XmlDocumentExtractor(_normalizer, NullLogger<XmlDocumentExtractor>.Instance);

            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".xml"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("System Architecture Overview", doc.RawText);
            Assert.True(doc.Chunks.Count > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HtmlDocumentExtractor_StripsScriptsAndStylesAndDecodesEntities()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.html");
        try
        {
            var html = @"<!DOCTYPE html>
<html>
<head><title>Test Page &amp; Title</title><style>body { color: red; }</style></head>
<body>
  <script>alert('malicious script should be stripped');</script>
  <h1>Welcome to FileLens &trade;</h1>
  <p>Local-first search engine &amp; previewer.</p>
</body>
</html>";
            File.WriteAllText(tempFile, html);
            var extractor = new HtmlDocumentExtractor(_normalizer, NullLogger<HtmlDocumentExtractor>.Instance);

            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".html"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.False(doc.RawText.Contains("alert('malicious"), "Script contents must be stripped");
            Assert.False(doc.RawText.Contains("color: red"), "Style contents must be stripped");
            Assert.Contains("Welcome to FileLens ™", doc.RawText);
            Assert.Equal("Test Page & Title", doc.Metadata["Title"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SourceCodeExtractor_ExtractsLinesAndLanguage()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(tempFile, "namespace Demo;\npublic class Worker {\n    public void Execute() { Console.WriteLine(\"Executing...\"); }\n}");
            var extractor = new SourceCodeExtractor(_normalizer, NullLogger<SourceCodeExtractor>.Instance);

            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".cs"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("class Worker", doc.RawText);
            Assert.Equal("CS", doc.Metadata["Language"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DocxDocumentExtractor_ExtractsParagraphsAndTables()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("word/document.xml");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    <w:p><w:r><w:t>First Paragraph: Confidential Strategy</w:t></w:r></w:p>
    <w:tbl>
      <w:tr>
        <w:tc><w:p><w:r><w:t>Project Alpha</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Status Approved</w:t></w:r></w:p></w:tc>
      </w:tr>
    </w:tbl>
  </w:body>
</w:document>");
            }

            var extractor = new DocxDocumentExtractor(_normalizer, NullLogger<DocxDocumentExtractor>.Instance);
            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".docx"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("Confidential Strategy", doc.RawText);
            Assert.Contains("Project Alpha | Status Approved", doc.RawText);
            Assert.Equal(2, doc.Chunks.Count);
            Assert.Equal("Paragraph 1", doc.Chunks[0].LocationReference);
            Assert.Equal("Table 1", doc.Chunks[1].LocationReference);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task XlsxDocumentExtractor_ExtractsWorksheetsAndCells()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var wbEntry = zip.CreateEntry("xl/workbook.xml");
                using (var writer = new StreamWriter(wbEntry.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets><sheet name=""Financials"" sheetId=""1"" r:id=""rId1""/></sheets>
</workbook>");
                }

                var relsEntry = zip.CreateEntry("xl/_rels/workbook.xml.rels");
                using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");
                }

                var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
                using (var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetData>
    <row r=""1"">
      <c r=""A1"" t=""inlineStr""><is><t>Net Revenue Q1</t></is></c>
      <c r=""B1""><v>450000</v></c>
    </row>
  </sheetData>
</worksheet>");
                }
            }

            var extractor = new XlsxDocumentExtractor(_normalizer, NullLogger<XlsxDocumentExtractor>.Instance);
            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".xlsx"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("Net Revenue Q1", doc.RawText);
            Assert.Contains("450000", doc.RawText);
            Assert.Equal(1, doc.Chunks.Count);
            Assert.Equal("Financials", doc.Chunks[0].SheetName);
            Assert.Equal("A1:B1", doc.Chunks[0].CellAddress);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PptxDocumentExtractor_ExtractsSlidesAndSlideNumbers()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var slide1 = zip.CreateEntry("ppt/slides/slide1.xml");
                using (var writer = new StreamWriter(slide1.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Quarterly Vision 2026</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld>
</p:sld>");
                }

                var slide2 = zip.CreateEntry("ppt/slides/slide2.xml");
                using (var writer = new StreamWriter(slide2.Open(), Encoding.UTF8))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Action Items and Milestones</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld>
</p:sld>");
                }
            }

            var extractor = new PptxDocumentExtractor(_normalizer, NullLogger<PptxDocumentExtractor>.Instance);
            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".pptx"));

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("Quarterly Vision 2026", doc.RawText);
            Assert.Contains("Action Items and Milestones", doc.RawText);
            Assert.Equal(2, doc.Chunks.Count);
            Assert.Equal(1, doc.Chunks[0].SlideNumber);
            Assert.Equal("Slide 1", doc.Chunks[0].LocationReference);
            Assert.Equal(2, doc.Chunks[1].SlideNumber);
            Assert.Equal("Slide 2", doc.Chunks[1].LocationReference);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PdfDocumentExtractor_ExtractsTextAndPageNumbers()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        try
        {
            // Create a minimal synthetic uncompressed PDF
            var pdfContent = @"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>
endobj
4 0 obj
<< /Length 55 >>
stream
BT
/F1 12 Tf
(FileLens Safe PDF Extraction Tested Successfully) Tj
ET
endstream
endobj
trailer
<< /Root 1 0 R >>
%%EOF";
            File.WriteAllBytes(tempFile, Encoding.Latin1.GetBytes(pdfContent));

            var extractor = new PdfDocumentExtractor(_normalizer, NullLogger<PdfDocumentExtractor>.Instance);
            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".pdf"));

            if (string.IsNullOrEmpty(doc.RawText))
            {
                throw new Exception($"PDF Status: {doc.Status}, Chunks: {doc.Chunks.Count}, Error: {doc.ErrorMessage}");
            }

            Assert.Equal(ExtractionStatus.Extracted, doc.Status);
            Assert.Contains("FileLens Safe PDF Extraction Tested Successfully", doc.RawText);
            Assert.Equal(1, doc.Chunks.Count);
            Assert.Equal(1, doc.Chunks[0].PageNumber);
            Assert.Equal("Page 1", doc.Chunks[0].LocationReference);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CorruptDocument_ReturnsCorruptStatusWithoutCrashing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.docx");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });
            var extractor = new DocxDocumentExtractor(_normalizer, NullLogger<DocxDocumentExtractor>.Instance);

            var doc = await extractor.ExtractAsync(CreateIndexedFile(tempFile, ".docx"));

            Assert.True(doc.Status is ExtractionStatus.Failed or ExtractionStatus.Corrupt);
            Assert.NotNull(doc.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentExtractionRegistry_ResolvesAllSupportedFormats()
    {
        var extractors = new IDocumentExtractor[]
        {
            new PlainTextExtractor(_normalizer, NullLogger<PlainTextExtractor>.Instance),
            new DelimitedTextExtractor(_normalizer, NullLogger<DelimitedTextExtractor>.Instance),
            new JsonDocumentExtractor(_normalizer, NullLogger<JsonDocumentExtractor>.Instance),
            new XmlDocumentExtractor(_normalizer, NullLogger<XmlDocumentExtractor>.Instance),
            new HtmlDocumentExtractor(_normalizer, NullLogger<HtmlDocumentExtractor>.Instance),
            new SourceCodeExtractor(_normalizer, NullLogger<SourceCodeExtractor>.Instance),
            new DocxDocumentExtractor(_normalizer, NullLogger<DocxDocumentExtractor>.Instance),
            new XlsxDocumentExtractor(_normalizer, NullLogger<XlsxDocumentExtractor>.Instance),
            new PptxDocumentExtractor(_normalizer, NullLogger<PptxDocumentExtractor>.Instance),
            new PdfDocumentExtractor(_normalizer, NullLogger<PdfDocumentExtractor>.Instance)
        };

        var registry = new DocumentExtractionRegistry(extractors);

        var extensions = new[] { ".txt", ".csv", ".json", ".xml", ".html", ".cs", ".docx", ".xlsx", ".pptx", ".pdf" };
        foreach (var ext in extensions)
        {
            Assert.True(registry.IsSupported(ext), $"Extension '{ext}' must be supported");
            Assert.NotNull(registry.GetExtractor(ext));
        }

        Assert.False(registry.IsSupported(".exe"));
        Assert.Null(registry.GetExtractor(".exe"));
    }
}
