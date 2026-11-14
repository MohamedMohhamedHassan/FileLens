using FileLens.Infrastructure.Text;

namespace FileLens.UnitTests.Text;

public sealed class TextNormalizerTests
{
    private readonly TextNormalizer _normalizer = new();

    [Fact]
    public void NormalizeText_EnglishCaseAndWhitespace_NormalizesCleanly()
    {
        string input = "  Hello   WORLD! \t\n This is FileLens.  ";
        string result = _normalizer.NormalizeText(input);

        Assert.Equal("hello world! this is filelens.", result);
    }

    [Fact]
    public void NormalizeArabic_AlefVariations_NormalizeToBareAlef()
    {
        // آ (madda), أ (hamza above), إ (hamza below), ٱ (wasla) -> ا
        string input = "أحمد إبراهيم آدم ٱستماع";
        string result = _normalizer.NormalizeArabic(input);

        Assert.Equal("احمد ابراهيم ادم استماع", result);
    }

    [Fact]
    public void NormalizeArabic_TaaMarbuta_NormalizesToHaa()
    {
        string input = "مكتبة شركة تقنية حكومية";
        string result = _normalizer.NormalizeArabic(input);

        Assert.Equal("مكتبه شركه تقنيه حكوميه", result);
    }

    [Fact]
    public void NormalizeArabic_AlefMaksura_NormalizesToYa()
    {
        string input = "مستشفى على إلى حتى";
        string result = _normalizer.NormalizeArabic(input);

        Assert.Equal("مستشفي علي الي حتي", result);
    }

    [Fact]
    public void NormalizeArabic_TashkeelDiacriticsAndTatweel_AreStripped()
    {
        // With Fatha, Damma, Kasra, Shadda, Tanwin, and Tatweel: فـَـتْــحٌ
        string input = "فـَـتْــحٌ مُـبِـيـنٌ";
        string result = _normalizer.NormalizeArabic(input);

        Assert.Equal("فتح مبين", result);
    }

    [Fact]
    public void NormalizeQuery_ExactPhrase_PreservesQuotesAndNormalizesContents()
    {
        string query = "\"تقرير الأرباح السنوي\"";
        string result = _normalizer.NormalizeQuery(query);

        Assert.Equal("\"تقرير الارباح السنوي\"", result);
    }

    [Fact]
    public void NormalizeQuery_BooleanOperators_PreservedExact()
    {
        string query = "تقرير AND أرباح NOT خسائر";
        string result = _normalizer.NormalizeQuery(query);

        Assert.Equal("تقرير AND ارباح NOT خسائر", result);
    }

    [Fact]
    public void NormalizeQuery_PrefixWildcard_PreservesAsterisk()
    {
        string query = "comp* AND تقر*";
        string result = _normalizer.NormalizeQuery(query);

        Assert.Equal("comp* AND تقر*", result);
    }
}

