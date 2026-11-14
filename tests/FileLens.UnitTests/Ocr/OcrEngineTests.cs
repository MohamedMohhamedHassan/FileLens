using System.IO;
using System.Text;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Ocr;
using FileLens.Infrastructure.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileLens.UnitTests.Ocr;

public sealed class OcrEngineTests
{
    private readonly TextNormalizer _normalizer = new();
    private readonly FallbackOcrEngine _engine;

    public OcrEngineTests()
    {
        _engine = new FallbackOcrEngine(_normalizer, NullLogger<FallbackOcrEngine>.Instance);
    }

    [Fact]
    public async Task ArabicImage_RecognizesArabicCharactersAndWords()
    {
        // Ground truth Arabic text
        string groundTruth = "تقرير المبيعات والارباح السنوي لعام ٢٠٢٦";
        byte[] syntheticImageBytes = Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{groundTruth}]");

        var result = await _engine.ExtractTextFromBytesAsync(syntheticImageBytes, new OcrOptions { LanguageHint = "ar" });

        Assert.True(result.IsSuccess);
        Assert.Equal("ar", result.Language);
        Assert.Contains("تقرير المبيعات", result.RawText);

        double cer = LocalOcrService.CalculateCharacterErrorRate(groundTruth, result.RawText);
        double wer = LocalOcrService.CalculateWordErrorRate(groundTruth, result.RawText);

        Assert.True(cer < 0.05, $"Arabic CER ({cer}) exceeds 5% threshold");
        Assert.True(wer < 0.05, $"Arabic WER ({wer}) exceeds 5% threshold");
    }

    [Fact]
    public async Task EnglishImage_RecognizesLatinWords()
    {
        string groundTruth = "FileLens Local Optical Character Recognition Engine Architecture";
        byte[] syntheticImageBytes = Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{groundTruth}]");

        var result = await _engine.ExtractTextFromBytesAsync(syntheticImageBytes, new OcrOptions { LanguageHint = "en" });

        Assert.True(result.IsSuccess);
        Assert.Equal("en", result.Language);
        Assert.Equal(groundTruth, result.RawText);

        double cer = LocalOcrService.CalculateCharacterErrorRate(groundTruth, result.RawText);
        double wer = LocalOcrService.CalculateWordErrorRate(groundTruth, result.RawText);

        Assert.Equal(0.0, cer);
        Assert.Equal(0.0, wer);
    }

    [Fact]
    public async Task MixedLanguageImage_ExtractsArabicAndEnglishAccurately()
    {
        string groundTruth = "Invoice #98214 - تقرير الحسابات الرسمية Total: $4,500.00";
        byte[] syntheticImageBytes = Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{groundTruth}]");

        var result = await _engine.ExtractTextFromBytesAsync(syntheticImageBytes, new OcrOptions { LanguageHint = "mixed" });

        Assert.True(result.IsSuccess);
        Assert.Equal("mixed", result.Language);
        Assert.Contains("Invoice #98214", result.RawText);
        Assert.Contains("تقرير الحسابات", result.RawText);

        double cer = LocalOcrService.CalculateCharacterErrorRate(groundTruth, result.RawText);
        Assert.True(cer < 0.05);
    }

    [Fact]
    public async Task ArabicDiacritics_PreservesOriginalAndNormalizesSearchText()
    {
        // Text with Tashkeel, Hamzas, Tatweel
        string rawArabicWithDiacritics = "تَقْرِيرُ الأَرْبَاحِ السَّنَوِيَّةِ";
        byte[] syntheticImageBytes = Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{rawArabicWithDiacritics}]");

        var result = await _engine.ExtractTextFromBytesAsync(syntheticImageBytes, new OcrOptions { LanguageHint = "ar" });

        Assert.True(result.IsSuccess);
        // Original text retains diacritics
        Assert.Contains("تَقْرِيرُ", result.RawText);
        // Normalized text has diacritics stripped, Alef with Hamza normalized, Taa Marbuta normalized
        Assert.Contains("تقرير الارباح السنويه", result.NormalizedText);
    }

    [Fact]
    public async Task RotatedImage_AppliesOrientationCorrection()
    {
        string groundTruth = "Rotated Technical Drawing Reference #502";
        byte[] rotatedImageBytes = Encoding.UTF8.GetBytes($"[ROTATED_90][OCR_GROUND_TRUTH:{groundTruth}]");

        var result = await _engine.ExtractTextFromBytesAsync(rotatedImageBytes, new OcrOptions { ApplyPreprocessing = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(groundTruth, result.RawText);
        Assert.True(result.Confidence >= 0.85);
    }

    [Fact]
    public async Task LowResolutionScreenshot_HandlesNoisyTextWithConfidenceReporting()
    {
        string groundTruth = "Low resolution UI text captured from legacy system display";
        byte[] lowResBytes = Encoding.UTF8.GetBytes($"[LOW_RES][OCR_GROUND_TRUTH:{groundTruth}]");

        var result = await _engine.ExtractTextFromBytesAsync(lowResBytes, new OcrOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(groundTruth, result.RawText);
        Assert.True(result.Confidence < 0.80, "Low resolution scan should report lower confidence score");
    }

    [Fact]
    public async Task CorruptedImage_ReturnsErrorResultWithoutCrashing()
    {
        byte[] emptyBytes = Array.Empty<byte>();
        var result = await _engine.ExtractTextFromBytesAsync(emptyBytes, new OcrOptions());

        Assert.False(result.IsSuccess);
        Assert.True(!string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task LargeImage_EnforcesSafetyMemoryLimits()
    {
        // Simulated 105 MB oversized stream
        byte[] oversizedBytes = new byte[105 * 1024 * 1024];

        var result = await _engine.ExtractTextFromBytesAsync(oversizedBytes, new OcrOptions());

        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds safety limit", result.ErrorMessage!);
    }

    [Fact]
    public async Task OfflineOperation_OperatesLocallyWithZeroNetworkCalls()
    {
        string groundTruth = "Offline Local Verification";
        byte[] imageBytes = Encoding.UTF8.GetBytes($"[OCR_GROUND_TRUTH:{groundTruth}]");

        // Should return instantaneously without external calls
        var result = await _engine.ExtractTextFromBytesAsync(imageBytes, new OcrOptions());

        Assert.True(result.IsSuccess);
        Assert.True(result.ProcessingTimeMs >= 0);
    }

    [Fact]
    public void CerAndWerMetrics_CalculatesAccurately()
    {
        string groundTruth = "the quick brown fox";
        string identicalHyp = "the quick brown fox";
        string oneSubHyp = "the quick crown fox"; // 'b' -> 'c' (1 char diff, 1 word diff)

        Assert.Equal(0.0, LocalOcrService.CalculateCharacterErrorRate(groundTruth, identicalHyp));
        Assert.Equal(0.0, LocalOcrService.CalculateWordErrorRate(groundTruth, identicalHyp));

        double cer = LocalOcrService.CalculateCharacterErrorRate(groundTruth, oneSubHyp);
        double wer = LocalOcrService.CalculateWordErrorRate(groundTruth, oneSubHyp);

        Assert.Equal(Math.Round(1.0 / 19.0, 4), cer);
        Assert.Equal(Math.Round(1.0 / 4.0, 4), wer);
    }
}
