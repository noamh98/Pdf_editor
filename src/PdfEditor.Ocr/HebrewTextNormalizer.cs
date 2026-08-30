using System.Globalization;
using System.Text;

namespace PdfEditor.Ocr;

/// <summary>
/// Normalises Hebrew text so a search matches what a reader would consider the same word.
/// </summary>
/// <remarks>
/// Two Hebrew-specific problems break naive search. A letter at the end of a word is written with a
/// final form (ך ם ן ף ץ), so "ספר" and "לספר" share a stem that a literal comparison misses at word
/// boundaries. And scanned or typed text may or may not carry nikud, which is invisible to most
/// readers but changes every code point.
/// </remarks>
public static class HebrewTextNormalizer
{
    private const char NikudStart = '֑';
    private const char NikudEnd = 'ׇ';

    private static readonly Dictionary<char, char> FinalForms = new()
    {
        ['ך'] = 'כ',
        ['ם'] = 'מ',
        ['ן'] = 'נ',
        ['ף'] = 'פ',
        ['ץ'] = 'צ'
    };

    /// <summary>Removes nikud and cantillation marks.</summary>
    public static string StripNikud(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c < NikudStart || c > NikudEnd) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Replaces final letter forms with their regular counterparts.</summary>
    public static string FoldFinalForms(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(FinalForms.GetValueOrDefault(c, c));
        return sb.ToString();
    }

    /// <summary>
    /// The form used for comparison: no nikud, no final forms, no bidi controls, lower case,
    /// and runs of whitespace collapsed to a single space.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var stripped = FoldFinalForms(StripNikud(text));
        var sb = new StringBuilder(stripped.Length);
        bool lastWasSpace = false;

        foreach (char c in stripped)
        {
            if (IsBidiControl(c)) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            lastWasSpace = false;
            sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsBidiControl(char c) =>
        c is '‎' or '‏' or '‪' or '‫' or '‬' or '‭' or '‮'
          or '⁦' or '⁧' or '⁨' or '⁩' or '​' or '﻿';
}
