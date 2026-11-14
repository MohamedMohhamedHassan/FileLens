namespace FileLens.Domain.Interfaces;

/// <summary>
/// Normalizes text for bilingual Arabic and English search and indexing.
/// Preserves original text while generating search-optimized representations.
/// </summary>
public interface ITextNormalizer
{
    /// <summary>
    /// Performs standard normalization (Unicode NFKC, whitespace, lowercasing, and Arabic normalization).
    /// </summary>
    string NormalizeText(string text);

    /// <summary>
    /// Normalizes Arabic characters:
    /// - Normalizes Alef forms (أ, إ, آ, ٱ -> ا)
    /// - Normalizes Taa Marbuta (ة -> ه)
    /// - Normalizes Ya forms (ى, ي -> ي)
    /// - Strips Tashkeel (diacritics: Fatha, Damma, Kasra, Sukun, Shadda, Tanwin)
    /// - Strips Tatweel (Kashida)
    /// </summary>
    string NormalizeArabic(string arabicText);

    /// <summary>
    /// Normalizes a user search query while preserving FTS syntax (quotes for exact phrases, AND/OR/NOT, prefix asterisks).
    /// </summary>
    string NormalizeQuery(string query);
}

