namespace SnapTranslate.Services;

public static class LanguageDetector
{
    /// <summary>
    /// Detects whether text should be translated Thai -> English ("th" -> "en")
    /// or non-Thai -> Thai ("auto" -> "th").
    /// </summary>
    public static (string SourceLang, string TargetLang) DetectDirection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("auto", "th");
        }

        int thaiCount = 0;
        int latinCount = 0;

        foreach (char c in text)
        {
            if (c >= '\u0E00' && c <= '\u0E7F')
            {
                thaiCount++;
            }
            else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
            {
                latinCount++;
            }
        }

        if (thaiCount > 0 && (latinCount == 0 || (double)thaiCount / (thaiCount + latinCount) >= 0.25))
        {
            return ("th", "en");
        }

        return ("auto", "th");
    }

    /// <summary>
    /// Splits leading and trailing whitespace so Typing Mode preserves surrounding spacing/newlines.
    /// </summary>
    public static (string Leading, string Core, string Trailing) SplitOuterWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        int start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start == text.Length)
        {
            return (text, string.Empty, string.Empty);
        }

        int end = text.Length - 1;
        while (end >= start && char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        string leading = text[..start];
        string core = text[start..(end + 1)];
        string trailing = text[(end + 1)..];
        return (leading, core, trailing);
    }
}
