using System.Globalization;
using System.Text;

namespace FoodStreetMobile.Services;

public static class TextNormalizer
{
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('d', 'd')
            .Replace('Ð', 'D');
    }

    public static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return RemoveDiacritics(text).Trim().ToLowerInvariant();
    }
}
