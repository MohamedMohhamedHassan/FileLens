using Microsoft.Extensions.DependencyInjection;
using FileLens.Domain.Interfaces;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;
using FileLens.Infrastructure.Text;
using FileLens.Infrastructure.Extraction;
using FileLens.Infrastructure.Ocr;
using FileLens.Infrastructure.Visual;

namespace FileLens.Infrastructure;

/// <summary>
/// Registers infrastructure services with the dependency injection container.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Adds SQLite database, text normalization, format extractors and filesystem infrastructure services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<IFileDiscoveryService, SafeFileDiscoveryService>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IIndexDatabase, SqliteDatabase>();

        // Text normalizer
        services.AddSingleton<ITextNormalizer, TextNormalizer>();

        // Format Extractors
        services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentExtractor, DelimitedTextExtractor>();
        services.AddSingleton<IDocumentExtractor, JsonDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, XmlDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, HtmlDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, SourceCodeExtractor>();
        services.AddSingleton<IDocumentExtractor, DocxDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, XlsxDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, PptxDocumentExtractor>();
        services.AddSingleton<IDocumentExtractor, PdfDocumentExtractor>();

        // Registry
        services.AddSingleton<IDocumentExtractionRegistry, DocumentExtractionRegistry>();

        // Phase 3: OCR Engine, Worker & Health Check
        services.AddSingleton<OcrWorkerClient>();
        services.AddSingleton<IOcrEngine, FallbackOcrEngine>();
        services.AddSingleton<IOcrService, LocalOcrService>();
        services.AddSingleton<IAiWorkerHealthCheck, ProductionAiWorkerHealthCheckService>();

        // Phase 4: semantic SigLIP worker and separate OpenCV feature provider.
        services.AddSingleton<VisualWorkerClient>();
        services.AddSingleton<SigLIP2VisualEmbeddingProvider>();
        services.AddSingleton<LocalVisualEmbeddingProvider>();
        services.AddSingleton<IImageEmbeddingProvider>(sp => sp.GetRequiredService<SigLIP2VisualEmbeddingProvider>());
        services.AddSingleton<IVectorIndex, SqliteVectorIndex>();
        services.AddSingleton<VisualIndexingService>();

        return services;
    }
}
