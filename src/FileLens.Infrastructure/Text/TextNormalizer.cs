using System.Text;
using System.Text.RegularExpressions;
using FileLens.Domain.Interfaces;

namespace FileLens.Infrastructure.Text;

/// <summary>
/// High-performance text normalizer providing bilingual Arabic and English text normalization
/// for SQLite FTS5 search indexing and query processing.
/// </summary>
public sealed partial class TextNormalizer : ITextNormalizer
{
    // Regex matching Arabic diacritics (Tashkeel)
    [GeneratedRegex(@"[\u064B-\u0653\u0670]", RegexOptions.Compiled)]
    private static partial Regex TashkeelRegex();

    // Regex matching Tatweel (Kashida)
    [GeneratedRegex(@"\u0640", RegexOptions.Compiled)]
    private static partial Regex TatweelRegex();

    // Regex matching multiple whitespace characters
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

    // Regex matching search terms, quoted phrases, and operators
    [GeneratedRegex(@"""([^""]+)""|(\S+)", RegexOptions.Compiled)]
    private static partial Regex QueryTokenRegex();

    public string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // 1. Unicode Compatibility Decomposition followed by Canonical Composition (NFKC)
        string nfkc = text.Normalize(NormalizationForm.FormKC);

        // 2. Arabic linguistic normalization
        string arabicNorm = NormalizeArabic(nfkc);

        // 3. Lowercase for case-insensitivity
        string lower = arabicNorm.ToLowerInvariant();

        // 4. Collapse redundant whitespace
        return MultiWhitespaceRegex().Replace(lower, " ").Trim();
    }

    public string NormalizeArabic(string arabicText)
    {
        if (string.IsNullOrEmpty(arabicText))
        {
            return string.Empty;
        }

        // 1. Remove Tashkeel (diacritics: Fatha, Damma, Kasra, Sukun, Shadda, Tanwin)
        string stripped = TashkeelRegex().Replace(arabicText, string.Empty);

        // 2. Remove Tatweel / Kashida (ـ)
        stripped = TatweelRegex().Replace(stripped, string.Empty);

        // 3. Character normalization
        var sb = new StringBuilder(stripped.Length);
        foreach (char c in stripped)
        {
            switch (c)
            {
                // Alef variations -> standard bare Alef (ا)
                case '\u0622': // آ
                case '\u0623': // أ
                case '\u0625': // إ
                case '\u0671': // ٱ
                    sb.Append('\u0627');
                    break;

                // Taa Marbuta (ة) -> Haa (ه)
                case '\u0629': // ة
                    sb.Append('\u0647');
                    break;

                // Alef Maksura (ى) -> Ya (ي)
                case '\u0649': // ى
                    sb.Append('\u064A');
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    public string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var matches = QueryTokenRegex().Matches(query.Trim());
        var tokens = new List<string>(matches.Count);

        foreach (Match match in matches)
        {
            // Case 1: Quoted phrase: "..."
            if (match.Groups[1].Success)
            {
                string phrase = match.Groups[1].Value;
                string normPhrase = NormalizeText(phrase);
                if (!string.IsNullOrWhiteSpace(normPhrase))
                {
                    tokens.Add($"\"{normPhrase}\"");
                }
            }
            // Case 2: Individual token or operator
            else if (match.Groups[2].Success)
            {
                string token = match.Groups[2].Value;

                // Check for FTS5 boolean operators (case-sensitive in FTS5)
                if (string.Equals(token, "AND", StringComparison.Ordinal) ||
                    string.Equals(token, "OR", StringComparison.Ordinal) ||
                    string.Equals(token, "NOT", StringComparison.Ordinal))
                {
                    tokens.Add(token);
                }
                else
                {
                    bool hasWildcard = token.EndsWith('*');
                    string rawWord = hasWildcard ? token[..^1] : token;
                    string normWord = NormalizeText(rawWord);

                    if (!string.IsNullOrWhiteSpace(normWord))
                    {
                        // Escape any internal quotes or special characters if needed
                        normWord = normWord.Replace("\"", string.Empty);
                        tokens.Add(hasWildcard ? $"{normWord}*" : normWord);
                    }
                }
            }
        }

        return string.Join(" ", tokens);
    }
}

