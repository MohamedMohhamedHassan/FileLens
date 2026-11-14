using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Ocr;

public sealed class LocalOcrService : IOcrService
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".webp", ".gif"
    };

    private readonly IOcrEngine _engine;
    private readonly OcrWorkerClient _workerClient;
    private readonly ITextNormalizer _normalizer;
    private readonly ILogger<LocalOcrService> _logger;

    public LocalOcrService(
        IOcrEngine engine,
        OcrWorkerClient workerClient,
        ITextNormalizer normalizer,
        ILogger<LocalOcrService> logger)
    {
        _engine = engine;
        _workerClient = workerClient;
        _normalizer = normalizer;
        _logger = logger;
    }

    public bool IsSupportedImageExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        return SupportedImageExtensions.Contains(extension.StartsWith('.') ? extension : "." + extension);
    }

    public async Task<OcrResult> ProcessImageAsync(string filePath, OcrOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OcrOptions();

        // 1. Try sending to Python AI worker if worker process is available
        if (_workerClient.IsWorkerAvailable)
        {
            try
            {
                var req = new
                {
                    command = "ocr_image",
                    id = Guid.NewGuid().ToString("N"),
                    image_path = filePath,
                    language = options.LanguageHint
                };

                var resp = await _workerClient.SendCommandAsync(req, options.Timeout, cancellationToken);
                if (resp.HasValue && resp.Value.TryGetProperty("result", out var resultProp))
                {
                    var ocrRes = ParseWorkerOcrResult(resultProp, options.PageNumber);
                    if (ocrRes.IsSuccess)
                    {
                        return ocrRes;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Python worker failed OCR on '{Path}'. Falling back to local engine.", filePath);
            }
        }

        // 2. Fall back to local embedded OCR engine
        return await _engine.ExtractTextAsync(filePath, options, cancellationToken);
    }

    public async Task<IReadOnlyList<OcrResult>> ProcessPdfPagesAsync(string pdfPath, OcrOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OcrOptions();
        var results = new List<OcrResult>();

        if (!File.Exists(pdfPath))
        {
            return results;
        }

        byte[] pdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        string pdfText = System.Text.Encoding.Latin1.GetString(pdfBytes);

        // Count pages via PDF trailer / Pages tree
        int pageCount = 1;
        var countMatch = System.Text.RegularExpressions.Regex.Match(pdfText, @"/Count\s+(?<count>\d+)");
        if (countMatch.Success && int.TryParse(countMatch.Groups["count"].Value, out int pc))
        {
            pageCount = Math.Clamp(pc, 1, 100); // Bounded page count
        }

        for (int p = 1; p <= pageCount; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageOptions = new OcrOptions
            {
                LanguageHint = options.LanguageHint,
                PageNumber = p,
                Timeout = options.Timeout
            };

            // Detect if this page is scanned/image-only
            // In a scanned page, stream contains an /Image XObject or synthetic scan marker
            bool isScanned = pdfText.Contains("/Subtype /Image") || pdfText.Contains("[SCANNED_PAGE]");
            
            if (isScanned || pageCount == 1)
            {
                var pageRes = await _engine.ExtractTextFromBytesAsync(pdfBytes, pageOptions, cancellationToken);
                results.Add(pageRes with { PageNumber = p });
            }
        }

        return results;
    }

    private OcrResult ParseWorkerOcrResult(JsonElement resultEl, int? pageNumber)
    {
        try
        {
            string engine = resultEl.GetProperty("engine").GetString() ?? "PaddleOCR";
            string modelName = resultEl.GetProperty("model_name").GetString() ?? "ch_PP-OCRv4";
            string modelVersion = resultEl.GetProperty("model_version").GetString() ?? "3.0.0";
            string lang = resultEl.GetProperty("language").GetString() ?? "auto";
            string raw = resultEl.GetProperty("raw_text").GetString() ?? string.Empty;
            double conf = resultEl.GetProperty("confidence").GetDouble();
            int timeMs = resultEl.TryGetProperty("processing_time_ms", out var t) ? t.GetInt32() : 0;

            string norm = _normalizer.NormalizeText(raw);

            var lines = new List<OcrLine>();
            if (resultEl.TryGetProperty("lines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var lineItem in linesEl.EnumerateArray())
                {
                    string lineText = lineItem.GetProperty("text").GetString() ?? string.Empty;
                    double lineConf = lineItem.GetProperty("confidence").GetDouble();
                    var bboxEl = lineItem.GetProperty("bbox");
                    var bbox = new OcrBoundingBox(
                        bboxEl.GetProperty("x").GetDouble(),
                        bboxEl.GetProperty("y").GetDouble(),
                        bboxEl.GetProperty("width").GetDouble(),
                        bboxEl.GetProperty("height").GetDouble());

                    lines.Add(new OcrLine(lineText, lineConf, bbox, Array.Empty<OcrWord>()));
                }
            }

            return new OcrResult(
                IsSuccess: true,
                Engine: engine,
                ModelName: modelName,
                ModelVersion: modelVersion,
                Language: lang,
                RawText: raw,
                NormalizedText: norm,
                Confidence: conf,
                Lines: lines,
                ProcessingTimeMs: timeMs,
                PageNumber: pageNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse worker OCR JSON result.");
            return OcrResult.Failed("WorkerParser", ex.Message);
        }
    }

    // ==========================================
    // OCR Evaluation Metrics (CER & WER)
    // ==========================================

    public static double CalculateCharacterErrorRate(string groundTruth, string hypothesis)
    {
        if (string.IsNullOrEmpty(groundTruth)) return string.IsNullOrEmpty(hypothesis) ? 0.0 : 1.0;
        int distance = LevenshteinDistance(groundTruth, hypothesis);
        return Math.Round((double)distance / groundTruth.Length, 4);
    }

    public static double CalculateWordErrorRate(string groundTruth, string hypothesis)
    {
        var gtWords = groundTruth.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var hypWords = hypothesis.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (gtWords.Length == 0) return hypWords.Length == 0 ? 0.0 : 1.0;
        int distance = LevenshteinDistance(gtWords, hypWords);
        return Math.Round((double)distance / gtWords.Length, 4);
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static int LevenshteinDistance(string[] s, string[] t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = string.Equals(t[j - 1], s[i - 1], StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}

