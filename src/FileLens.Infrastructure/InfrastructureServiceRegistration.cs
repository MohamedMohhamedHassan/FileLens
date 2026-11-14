using Microsoft.Extensions.DependencyInjection;
using FileLens.Domain.Interfaces;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;
using FileLens.Infrastructure.Text;
using FileLens.Infrastructure.Extraction;

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

        return services;
    }
}
