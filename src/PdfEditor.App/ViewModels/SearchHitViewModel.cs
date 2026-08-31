using PdfEditor.Core.Localization;
using PdfEditor.Ocr;

namespace PdfEditor.App.ViewModels;

/// <summary>One row in the search results list.</summary>
public sealed class SearchHitViewModel(OcrSearchHit hit)
{
    public OcrSearchHit Hit { get; } = hit;

    public int PageIndex => Hit.PageIndex;

    public string PageLabel => ErrorMessages.Format(Strings.PageLabel, Hit.PageIndex + 1);

    /// <summary>The recognised line the match came from, trimmed to something readable.</summary>
    public string Snippet => Hit.MatchedText.Length <= 90
        ? Hit.MatchedText
        : Hit.MatchedText[..90] + "…";
}
