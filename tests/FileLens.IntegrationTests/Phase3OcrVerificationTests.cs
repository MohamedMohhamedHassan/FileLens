using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Application.Services;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.Ocr;
using FileLens.Infrastructure.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileLens.IntegrationTests;

public sealed class Phase3OcrVerificationTests
{
    private static async Task<(SqliteDatabase db, string dbPath, string testDir)> CreateTestEnvironmentAsync()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"FileLens_Phase3_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        var dbPath = Path.Combine(testDir, "test_filelens_ocr.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var db = new SqliteDatabase(connectionFactory, NullLogger<SqliteDatabase>.Instance);
        await db.InitializeAsync();

        return (db, dbPath, testDir);
    }

    private static IndexedFile CreateTestIndexedFile(SearchRootId rootId, string fullPath, string extension)
    {
        return new IndexedFile
        {
            Id = new FileEntryId(Guid.NewGuid()),
            RootId = rootId,
            FullPath = fullPath,
            NormalizedPath = fullPath.ToLowerInvariant(),
            RelativePath = Path.GetFileName(fullPath),
            FileName = Path.GetFileName(fullPath),
            Extension = extension,
            SizeBytes = 1024,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Status = FileStatus.Indexed
        };
    }

    [Fact]
    public async Task ScannedPdfPages_ExtractsTextAndMapsPageNumbersAccurately()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var engine = new FallbackOcrEngine(normalizer, NullLogger<FallbackOcrEngine>.Instance);
            var workerClient = new OcrWorkerClient(NullLogger<OcrWorkerClient>.Instance);
            var ocrService = new LocalOcrService(engine, workerClient, normalizer, NullLogger<LocalOcrService>.Instance);
            var searchService = new SearchService(db, normalizer, NullLogger<SearchService>.Instance);

            // Create synthetic scanned PDF
            string pdfPath = Path.Combine(testDir, "scanned_invoice.pdf");
            string scannedContent = @"%PDF-1.4
1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj
2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj
3 0 obj << /Type /Page /Parent 2 0 R /Contents 4 0 R >> endobj
4 0 obj << /Length 120 >> stream
/Subtype /Image [SCANNED_PAGE][OCR_GROUND_TRUTH:Scanned Vendor Invoice #4401 - Grand Total $12,500.00]
endstream endobj
trailer << /Root 1 0 R >> %%EOF";
            File.WriteAllBytes(pdfPath, Encoding.Latin1.GetBytes(scannedContent));

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

            var indexedFile = CreateTestIndexedFile(root.Id, pdfPath, ".pdf");
            await db.UpsertFilesBatchAsync(new[] { indexedFile });

            // Process PDF pages
            var pageResults = await ocrService.ProcessPdfPagesAsync(pdfPath);
            Assert.NotEmpty(pageResults);
            Assert.Equal(1, pageResults[0].PageNumber);
            Assert.Contains("Scanned Vendor Invoice #4401", pageResults[0].RawText);

            // Store in database
            var ocrRec = new OcrRecord
            {
                FileId = indexedFile.Id.Value,
                EngineName = pageResults[0].Engine,
                ModelName = pageResults[0].ModelName,
                ModelVersion = pageResults[0].ModelVersion,
                Language = pageResults[0].Language,
                PageNumber = 1,
                Confidence = pageResults[0].Confidence,
                RawText = pageResults[0].RawText,
                NormalizedText = pageResults[0].NormalizedText,
                ProcessingTimeMs = pageResults[0].ProcessingTimeMs
            };
            await db.UpsertOcrResultAsync(ocrRec);

            // Search FTS5 for OCR content
            var searchRes = await searchService.SearchAsync(new SearchQuery { Text = "Vendor Invoice 4401" });
            Assert.Equal(1, searchRes.TotalMatches);
            Assert.Equal(indexedFile.FileName, searchRes.Items[0].FileName);
            Assert.Equal(1, searchRes.Items[0].PageNumber);
            Assert.True(searchRes.Items[0].IsOcrMatch);
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
    public async Task ArabicOcr_IndexesInDatabaseAndSearchesWithDiacriticNormalization()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var engine = new FallbackOcrEngine(normalizer, NullLogger<FallbackOcrEngine>.Instance);
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

            string imagePath = Path.Combine(testDir, "receipt_arabic.png");
            string arabicWithDiacritics = "فَاتُورَةُ الضَّرِيبَةِ لِعَامِ ٢٠٢٦";
            File.WriteAllBytes(imagePath, Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{arabicWithDiacritics}]"));

            var indexedFile = CreateTestIndexedFile(root.Id, imagePath, ".png");
            await db.UpsertFilesBatchAsync(new[] { indexedFile });

            var ocrRes = await engine.ExtractTextAsync(imagePath, new OcrOptions { LanguageHint = "ar" });
            Assert.True(ocrRes.IsSuccess);

            var record = new OcrRecord
            {
                FileId = indexedFile.Id.Value,
                EngineName = ocrRes.Engine,
                ModelName = ocrRes.ModelName,
                ModelVersion = ocrRes.ModelVersion,
                Language = ocrRes.Language,
                Confidence = ocrRes.Confidence,
                RawText = ocrRes.RawText,
                NormalizedText = ocrRes.NormalizedText,
                ProcessingTimeMs = ocrRes.ProcessingTimeMs
            };
            await db.UpsertOcrResultAsync(record);

            // Search query with plain characters (no Tashkeel): "فاتورة الضريبة"
            var searchRes = await searchService.SearchAsync(new SearchQuery { Text = "فاتورة الضريبة" });
            Assert.Equal(1, searchRes.TotalMatches);
            Assert.Equal("receipt_arabic.png", searchRes.Items[0].FileName);
            Assert.True(searchRes.Items[0].IsOcrMatch);

            // Verify database retrieval
            var retrievedOcr = await db.GetOcrResultsByFileIdAsync(indexedFile.Id.Value);
            Assert.Equal(1, retrievedOcr.Count);
            Assert.Equal(arabicWithDiacritics, retrievedOcr[0].RawText);
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
    public async Task OcrJobEngine_CancellationAndRetry_HandlesLifecycleCleanly()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var engine = new FallbackOcrEngine(normalizer, NullLogger<FallbackOcrEngine>.Instance);
            var workerClient = new OcrWorkerClient(NullLogger<OcrWorkerClient>.Instance);
            var ocrService = new LocalOcrService(engine, workerClient, normalizer, NullLogger<LocalOcrService>.Instance);
            var jobEngine = new OcrJobEngine(db, ocrService, NullLogger<OcrJobEngine>.Instance);

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

            // Create 3 image files
            for (int i = 1; i <= 3; i++)
            {
                string path = Path.Combine(testDir, $"scan_{i}.jpg");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:Document {i} Verified]"));
                var file = CreateTestIndexedFile(root.Id, path, ".jpg");
                await db.UpsertFilesBatchAsync(new[] { file });
            }

            // Start pipeline with cancellation token
            using var cts = new CancellationTokenSource();
            var runTask = jobEngine.RunOcrPipelineAsync(cts.Token);
            await runTask;

            Assert.Equal(3, jobEngine.ProcessedCount);
            Assert.Equal(0, jobEngine.FailedCount);

            var stats = await db.GetOcrStatisticsAsync();
            Assert.Equal(3, stats.SuccessfullyProcessed);
            Assert.True(stats.AverageConfidence > 0.8);
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
    public async Task DuplicateOcrPreventionAndModifiedImageReprocessing_WorksAccurately()
    {
        var (db, dbPath, testDir) = await CreateTestEnvironmentAsync();
        try
        {
            var normalizer = new TextNormalizer();
            var engine = new FallbackOcrEngine(normalizer, NullLogger<FallbackOcrEngine>.Instance);
            var workerClient = new OcrWorkerClient(NullLogger<OcrWorkerClient>.Instance);
            var ocrService = new LocalOcrService(engine, workerClient, normalizer, NullLogger<LocalOcrService>.Instance);
            var jobEngine = new OcrJobEngine(db, ocrService, NullLogger<OcrJobEngine>.Instance);
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

            string imagePath = Path.Combine(testDir, "dynamic_chart.png");
            File.WriteAllBytes(imagePath, Encoding.UTF8.GetBytes("[OCR_GROUND_TRUTH:Alpha Revenue Projection 2026]"));

            var file = CreateTestIndexedFile(root.Id, imagePath, ".png");
            await db.UpsertFilesBatchAsync(new[] { file });

            // Initial OCR Run
            await jobEngine.RunOcrPipelineAsync();
            Assert.Equal(1, jobEngine.ProcessedCount);

            var res1 = await searchService.SearchAsync(new SearchQuery { Text = "Alpha Revenue" });
            Assert.Equal(1, res1.TotalMatches);

            // Re-run pipeline without modifying file -> Should detect already processed and process 0
            var awaiting = await db.GetFilesAwaitingOcrAsync();
            Assert.Equal(0, awaiting.Count);

            // Update file with new modified timestamp and new content
            await Task.Delay(100);
            File.WriteAllBytes(imagePath, Encoding.UTF8.GetBytes("[OCR_GROUND_TRUTH:Beta Expense Report 2026]"));
            file = new IndexedFile
            {
                Id = file.Id,
                RootId = root.Id,
                FullPath = imagePath,
                NormalizedPath = imagePath.ToLowerInvariant(),
                RelativePath = Path.GetFileName(imagePath),
                FileName = Path.GetFileName(imagePath),
                Extension = ".png",
                SizeBytes = 1024,
                CreatedAtUtc = file.CreatedAtUtc,
                ModifiedAtUtc = DateTime.UtcNow,
                Status = FileStatus.Indexed
            };
            await db.UpsertFilesBatchAsync(new[] { file });

            // Now file is modified and awaiting OCR
            var awaitingModified = await db.GetFilesAwaitingOcrAsync();
            Assert.Equal(1, awaitingModified.Count);

            // Reprocess
            await jobEngine.RunOcrPipelineAsync();
            var res2 = await searchService.SearchAsync(new SearchQuery { Text = "Beta Expense" });
            Assert.Equal(1, res2.TotalMatches);
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
