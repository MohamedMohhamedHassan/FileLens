using System.IO;
using System.IO.Compression;
using System.Text;
using FileLens.Application.Services;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.Extraction;
using FileLens.Infrastructure.FileSystem;
using FileLens.Infrastructure.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileLens.IntegrationTests;

public sealed class Phase2VerificationTests
{
    private static async Task<(SqliteDatabase db, string dbPath, string testDir)> CreateTestEnvironmentAsync()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"FileLens_Phase2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        var dbPath = Path.Combine(testDir, "test_filelens.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var db = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        await db.InitializeAsync();

        return (db, dbPath, testDir);
    }

    private static IDocumentExtractionRegistry CreateRegistry(ITextNormalizer normalizer)
    {
        var extractors = new IDocumentExtractor[]
        {
            new PlainTextExtractor(normalizer, NullLogger<PlainTextExtractor>.Instance),
            new DelimitedTextExtractor(normalizer, NullLogger<DelimitedTextExtractor>.Instance),
            new JsonDocumentExtractor(normalizer, NullLogger<JsonDocumentExtractor>.Instance),
            new XmlDocumentExtractor(normalizer, NullLogger<XmlDocumentExtractor>.Instance),
            new HtmlDocumentExtractor(normalizer, NullLogger<HtmlDocumentExtractor>.Instance),
            new SourceCodeExtractor(normalizer, NullLogger<SourceCodeExtractor>.Instance),
            new DocxDocumentExtractor(normalizer, NullLogger<DocxDocumentExtractor>.Instance),
            new XlsxDocumentExtractor(normalizer, NullLogger<XlsxDocumentExtractor>.Instance),
            new PptxDocumentExtractor(normalizer, NullLogger<PptxDocumentExtractor>.Instance),
            new PdfDocumentExtractor(normalizer, NullLogger<PdfDocumentExtractor>.Instance)
        };
        return new DocumentExtractionRegistry(extractors);
    }

    private static void CreateDocx(string path, string text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body>
</w:document>");
    }

    private static void CreateXlsx(string path, string sheetName, string cellA1, string cellB1)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var wbEntry = zip.CreateEntry("xl/workbook.xml");
        using (var writer = new StreamWriter(wbEntry.Open(), Encoding.UTF8))
        {
            writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets><sheet name=""{sheetName}"" sheetId=""1"" r:id=""rId1""/></sheets>
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
            writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetData>
    <row r=""1"">
      <c r=""A1"" t=""inlineStr""><is><t>{cellA1}</t></is></c>
      <c r=""B1"" t=""inlineStr""><is><t>{cellB1}</t></is></c>
    </row>
  </sheetData>
</worksheet>");
        }
    }

    private static void CreatePptx(string path, string slide1Text, string slide2Text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var s1 = zip.CreateEntry("ppt/slides/slide1.xml");
        using (var writer = new StreamWriter(s1.Open(), Encoding.UTF8))
        {
            writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{slide1Text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld>
</p:sld>");
        }

        var s2 = zip.CreateEntry("ppt/slides/slide2.xml");
        using (var writer = new StreamWriter(s2.Open(), Encoding.UTF8))
        {
            writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{slide2Text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld>
</p:sld>");
        }
    }

    private static void CreatePdf(string path, string text)
    {
        var pdfContent = $@"%PDF-1.4
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
<< /Length {text.Length + 40} >>
stream
BT
/F1 12 Tf
({text}) Tj
ET
endstream
endobj
trailer
<< /Root 1 0 R >>
%%EOF";
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(pdfContent));
    }

    [Fact]
    public async Task EndToEnd_Discovery_Extraction_And_Search_WorksAcrossAllTenFormats()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var registry = CreateRegistry(normalizer);
            var pathValidator = new PathValidator();
            var discovery = new SafeFileDiscoveryService(pathValidator, NullLogger<SafeFileDiscoveryService>.Instance);
            var rootService = new RootManagementService(db, pathValidator, NullLogger<RootManagementService>.Instance);
            var indexingEngine = new IndexingJobEngine(db, discovery, NullLogger<IndexingJobEngine>.Instance);
            var extractionEngine = new DocumentExtractionEngine(db, registry, NullLogger<DocumentExtractionEngine>.Instance);
            var searchService = new SearchService(db, normalizer, NullLogger<SearchService>.Instance);

            // Create test files for all 10 formats with specific synthetic keywords
            File.WriteAllText(Path.Combine(testDir, "notes.txt"), "The quick brown fox jumps over the lazy dog. TXT format verified.");
            File.WriteAllText(Path.Combine(testDir, "data.csv"), "Id,Department,Location\n1,QuantumResearch,BuildingB\n2,SecurityAudit,BuildingC");
            File.WriteAllText(Path.Combine(testDir, "config.json"), "{\"databaseName\":\"FileLensLocal\",\"engineVersion\":\"2.0\",\"securityTier\":\"High\"}");
            File.WriteAllText(Path.Combine(testDir, "app.xml"), "<Application Name=\"FileLens\"><Module Code=\"CryptoSecurity\">Active</Module></Application>");
            File.WriteAllText(Path.Combine(testDir, "page.html"), "<html><head><title>Web Portal</title></head><body><h1>Enterprise System</h1><p>Welcome to cloudless search.</p></body></html>");
            File.WriteAllText(Path.Combine(testDir, "service.cs"), "namespace Infrastructure;\npublic class SecurityService {\n    public bool IsSecure() => true;\n}");
            CreateDocx(Path.Combine(testDir, "contract.docx"), "Master Services Agreement between AlphaCorp and BetaLLC.");
            CreateXlsx(Path.Combine(testDir, "budget.xlsx"), "Q1_Budget", "OperatingExpense", "HardwareUpgrades");
            CreatePptx(Path.Combine(testDir, "strategy.pptx"), "Slide 1: Annual Mission Statement", "Slide 2: Strategic Initiatives 2026");
            CreatePdf(Path.Combine(testDir, "whitepaper.pdf"), "Architectural Whitepaper: Safe FileLens Ingestion Architecture");

            // Step 1: Add Root & Run File Discovery
            var addResult = await rootService.ValidateAndAddRootAsync(testDir);
            Assert.True(addResult.IsValid, "Add root failed");
            var root = (await rootService.GetAllRootsAsync()).Single();

            await indexingEngine.StartIndexingAsync(root.Id);

            // Wait for background indexing job to complete
            int waitMs = 0;
            while (indexingEngine.IsRunning && waitMs < 10000)
            {
                await Task.Delay(100);
                waitMs += 100;
            }

            Assert.False(indexingEngine.IsRunning, "Indexing job should complete");

            var dbFiles = await db.GetFilesByRootAsync(root.Id, 100);
            Assert.True(dbFiles.Count >= 10, $"Expected at least 10 discovered files, got {dbFiles.Count}");

            // Step 2: Run Document Extraction
            await extractionEngine.RunExtractionAsync();

            var extStats = await db.GetExtractionStatisticsAsync();
            Assert.True(extStats.SuccessfullyExtracted >= 10, $"Expected at least 10 extracted docs, got {extStats.SuccessfullyExtracted}");

            // Step 3: Test Searches for each format
            var txtRes = await searchService.SearchAsync(new SearchQuery { Text = "quick brown fox" });
            Assert.True(txtRes.TotalMatches > 0, "TXT search failed");

            var csvRes = await searchService.SearchAsync(new SearchQuery { Text = "QuantumResearch" });
            Assert.True(csvRes.TotalMatches > 0, "CSV search failed");

            var jsonRes = await searchService.SearchAsync(new SearchQuery { Text = "FileLensLocal" });
            Assert.True(jsonRes.TotalMatches > 0, "JSON search failed");

            var xmlRes = await searchService.SearchAsync(new SearchQuery { Text = "CryptoSecurity" });
            Assert.True(xmlRes.TotalMatches > 0, "XML search failed");

            var htmlRes = await searchService.SearchAsync(new SearchQuery { Text = "cloudless" });
            Assert.True(htmlRes.TotalMatches > 0, "HTML search failed");

            var codeRes = await searchService.SearchAsync(new SearchQuery { Text = "SecurityService" });
            Assert.True(codeRes.TotalMatches > 0, "Source code search failed");

            var docxRes = await searchService.SearchAsync(new SearchQuery { Text = "AlphaCorp" });
            Assert.True(docxRes.TotalMatches > 0, "DOCX search failed");

            var xlsxRes = await searchService.SearchAsync(new SearchQuery { Text = "HardwareUpgrades" });
            Assert.True(xlsxRes.TotalMatches > 0, "XLSX search failed");
            Assert.Equal("Q1_Budget", xlsxRes.Items[0].SheetName);

            var pptxRes = await searchService.SearchAsync(new SearchQuery { Text = "Strategic Initiatives" });
            Assert.True(pptxRes.TotalMatches > 0, "PPTX search failed");
            Assert.Equal(2, pptxRes.Items[0].SlideNumber);

            var pdfRes = await searchService.SearchAsync(new SearchQuery { Text = "Architectural Whitepaper" });
            Assert.True(pdfRes.TotalMatches > 0, "PDF search failed");
            Assert.Equal(1, pdfRes.Items[0].PageNumber);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ExactPhraseAndPrefixSearch_WorksAccurately()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var searchService = new SearchService(db, normalizer, NullLogger<SearchService>.Instance);

            var root = new SearchRoot
            {
                Id = SearchRootId.New(),
                Path = testDir,
                NormalizedPath = testDir.ToLowerInvariant(),
                RootType = RootType.LocalFolder,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            await db.AddRootAsync(root);

            var file1 = new IndexedFile
            {
                Id = new FileEntryId(Guid.NewGuid()),
                RootId = root.Id,
                FullPath = Path.Combine(testDir, "spec1.txt"),
                NormalizedPath = Path.Combine(testDir, "spec1.txt").ToLowerInvariant(),
                RelativePath = "spec1.txt",
                FileName = "spec1.txt",
                Extension = ".txt",
                SizeBytes = 100,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                Status = FileStatus.Indexed
            };
            var file2 = new IndexedFile
            {
                Id = new FileEntryId(Guid.NewGuid()),
                RootId = root.Id,
                FullPath = Path.Combine(testDir, "spec2.txt"),
                NormalizedPath = Path.Combine(testDir, "spec2.txt").ToLowerInvariant(),
                RelativePath = "spec2.txt",
                FileName = "spec2.txt",
                Extension = ".txt",
                SizeBytes = 100,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                Status = FileStatus.Indexed
            };

            await db.UpsertFilesBatchAsync(new[] { file1, file2 });

            var doc1 = ExtractedDocument.CreateSuccess(
                file1.Id.Value,
                file1.FullPath,
                "PlainTextExtractor",
                "High performance computing cluster deployment guidelines.",
                normalizer.NormalizeText("High performance computing cluster deployment guidelines."),
                new[] { new DocumentChunk { Id = Guid.NewGuid(), FileId = file1.Id.Value, ChunkIndex = 0, RawText = "High performance computing cluster deployment guidelines." } });

            var doc2 = ExtractedDocument.CreateSuccess(
                file2.Id.Value,
                file2.FullPath,
                "PlainTextExtractor",
                "High security audit cluster performance guidelines.",
                normalizer.NormalizeText("High security audit cluster performance guidelines."),
                new[] { new DocumentChunk { Id = Guid.NewGuid(), FileId = file2.Id.Value, ChunkIndex = 0, RawText = "High security audit cluster performance guidelines." } });

            await db.UpsertDocumentTextAsync(doc1);
            await db.UpsertDocumentTextAsync(doc2);

            // Exact phrase test: "high performance"
            var exactRes = await searchService.SearchAsync(new SearchQuery { Text = "\"high performance\"" });
            Assert.Equal(1, exactRes.TotalMatches);
            Assert.Equal("spec1.txt", exactRes.Items[0].FileName);

            // Prefix test: deploy*
            var prefixRes = await searchService.SearchAsync(new SearchQuery { Text = "deploy*" });
            Assert.Equal(1, prefixRes.TotalMatches);
            Assert.Equal("spec1.txt", prefixRes.Items[0].FileName);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ArabicBilingualSearch_NormalizesDiacriticsAndLetterVariants()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var searchService = new SearchService(db, normalizer, NullLogger<SearchService>.Instance);

            var root = new SearchRoot
            {
                Id = SearchRootId.New(),
                Path = testDir,
                NormalizedPath = testDir.ToLowerInvariant(),
                RootType = RootType.LocalFolder,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            await db.AddRootAsync(root);

            var file1 = new IndexedFile
            {
                Id = new FileEntryId(Guid.NewGuid()),
                RootId = root.Id,
                FullPath = Path.Combine(testDir, "arabic_doc.txt"),
                NormalizedPath = Path.Combine(testDir, "arabic_doc.txt").ToLowerInvariant(),
                RelativePath = "arabic_doc.txt",
                FileName = "arabic_doc.txt",
                Extension = ".txt",
                SizeBytes = 200,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                Status = FileStatus.Indexed
            };
            await db.UpsertFilesBatchAsync(new[] { file1 });

            // Text stored with Alef with Hamza (أ), Taa Marbuta (ة), Tashkeel (َ ُ ِ ً) and Tatweel (ـ):
            // "تَقْرِيرُ الأَرْبَاحِ السَّنَوِيَّةِ لِلشَّرِكَةِ"
            var rawArabic = "تَقْرِيرُ الأَرْبَاحِ السَّنَوِيَّةِ لِلشَّرِكَةِ";
            var normArabic = normalizer.NormalizeText(rawArabic);

            var doc = ExtractedDocument.CreateSuccess(
                file1.Id.Value,
                file1.FullPath,
                "PlainTextExtractor",
                rawArabic,
                normArabic,
                new[] { new DocumentChunk { Id = Guid.NewGuid(), FileId = file1.Id.Value, ChunkIndex = 0, RawText = rawArabic, NormalizedText = normArabic } });

            await db.UpsertDocumentTextAsync(doc);

            // Query with bare Alef (ا), no Tashkeel, regular Haa (ه): "تقرير الارباح السنويه"
            var query = new SearchQuery { Text = "تقرير الارباح السنويه" };
            var results = await searchService.SearchAsync(query);

            Assert.True(results.TotalMatches > 0, "Arabic normalized search should match regardless of diacritics and Alef/Taa Marbuta variations");
            Assert.Equal("arabic_doc.txt", results.Items[0].FileName);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task DuplicateExtractionAndIncrementalUpdate_UpdatesExistingRecordsWithoutDuplication()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var searchService = new SearchService(db, normalizer, NullLogger<SearchService>.Instance);

            var root = new SearchRoot
            {
                Id = SearchRootId.New(),
                Path = testDir,
                NormalizedPath = testDir.ToLowerInvariant(),
                RootType = RootType.LocalFolder,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            await db.AddRootAsync(root);

            var file = new IndexedFile
            {
                Id = new FileEntryId(Guid.NewGuid()),
                RootId = root.Id,
                FullPath = Path.Combine(testDir, "evolving.txt"),
                NormalizedPath = Path.Combine(testDir, "evolving.txt").ToLowerInvariant(),
                RelativePath = "evolving.txt",
                FileName = "evolving.txt",
                Extension = ".txt",
                SizeBytes = 50,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow,
                Status = FileStatus.Indexed
            };
            await db.UpsertFilesBatchAsync(new[] { file });

            // Version 1
            var docV1 = ExtractedDocument.CreateSuccess(
                file.Id.Value,
                file.FullPath,
                "PlainTextExtractor",
                "Initial Draft of Patent Application 1001",
                normalizer.NormalizeText("Initial Draft of Patent Application 1001"),
                new[] { new DocumentChunk { Id = Guid.NewGuid(), FileId = file.Id.Value, ChunkIndex = 0, RawText = "Initial Draft of Patent Application 1001" } });
            await db.UpsertDocumentTextAsync(docV1);

            var res1 = await searchService.SearchAsync(new SearchQuery { Text = "Patent" });
            Assert.Equal(1, res1.TotalMatches);

            // Version 2: Updated content, old content removed
            var docV2 = ExtractedDocument.CreateSuccess(
                file.Id.Value,
                file.FullPath,
                "PlainTextExtractor",
                "Final Approved Trademark Registration 9999",
                normalizer.NormalizeText("Final Approved Trademark Registration 9999"),
                new[] { new DocumentChunk { Id = Guid.NewGuid(), FileId = file.Id.Value, ChunkIndex = 0, RawText = "Final Approved Trademark Registration 9999" } });
            await db.UpsertDocumentTextAsync(docV2);

            // Search for new term
            var res2 = await searchService.SearchAsync(new SearchQuery { Text = "Trademark" });
            Assert.Equal(1, res2.TotalMatches);

            // Search for old term - must return 0 (no stale index!)
            var resOld = await searchService.SearchAsync(new SearchQuery { Text = "Patent" });
            Assert.Equal(0, resOld.TotalMatches);

            // Preview must return updated text
            var preview = await searchService.GetDocumentPreviewAsync(file.Id.Value);
            Assert.NotNull(preview);
            Assert.Contains("Final Approved Trademark", preview!.RawText);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }
}
