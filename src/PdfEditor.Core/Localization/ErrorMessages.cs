using System.Globalization;
using PdfEditor.Core.Documents;

namespace PdfEditor.Core.Localization;

/// <summary>Turns domain error codes into the Hebrew text shown to the user.</summary>
public static class ErrorMessages
{
    public static string ForOpenError(PdfOpenError error) => error switch
    {
        PdfOpenError.FileNotFound => Strings.ErrorFileNotFound,
        PdfOpenError.AccessDenied => Strings.ErrorAccessDenied,
        PdfOpenError.NotAPdf => Strings.ErrorNotAPdf,
        PdfOpenError.Corrupted => Strings.ErrorCorrupted,
        PdfOpenError.PasswordRequired => Strings.ErrorPasswordRequired,
        PdfOpenError.UnsupportedEncryption => Strings.ErrorUnsupportedEncryption,
        _ => Strings.ErrorUnknown
    };

    public static string ForRangeError(PageRangeError error, string? offending)
    {
        var token = offending ?? string.Empty;
        return error switch
        {
            PageRangeError.Empty => Strings.RangeErrorEmpty,
            PageRangeError.InvalidCharacter => Format(Strings.RangeErrorInvalidCharacter, token),
            PageRangeError.MalformedRange => Format(Strings.RangeErrorMalformed, token),
            PageRangeError.NotANumber => Format(Strings.RangeErrorNotANumber, token),
            PageRangeError.ZeroOrNegative => Format(Strings.RangeErrorZeroOrNegative, token),
            PageRangeError.ReversedRange => Format(Strings.RangeErrorReversed, token),
            PageRangeError.OutOfBounds => Format(Strings.RangeErrorOutOfBounds, token),
            _ => Strings.ErrorUnknown
        };
    }

    /// <summary>
    /// Formats a message that embeds a Latin token inside Hebrew text. The token is wrapped in
    /// first-strong isolates so a file name or range never scrambles the surrounding sentence.
    /// </summary>
    public static string Format(string template, params object[] args)
    {
        var isolated = args.Select(a => "⁨" + a + "⁩").Cast<object>().ToArray();
        return string.Format(CultureInfo.CurrentCulture, template, isolated);
    }
}
