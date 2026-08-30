using PdfEditor.Core.Documents;
using PdfEditor.Core.Localization;

namespace PdfEditor.App.ViewModels;

/// <summary>A structural change the user can apply to a range of pages.</summary>
public enum PageOperation
{
    RotateLeft,
    RotateRight,
    Delete,
    Extract
}

/// <summary>
/// The page operations dialog: a range, an operation, and an honest summary of what will come out.
/// </summary>
/// <remarks>
/// Every operation writes a new file. The source is never modified, which keeps a mistake in a range
/// from costing anything, and means this view model never has to mutate the open document.
/// </remarks>
public sealed class PageOperationsViewModel : ViewModelBase
{
    private readonly int _pageCount;
    private string? _rangeText;
    private string? _rangeError;
    private PageOperation _operation = PageOperation.RotateRight;
    private IReadOnlyList<int> _selection;

    public PageOperationsViewModel(int pageCount, int currentPageIndex)
    {
        _pageCount = pageCount;
        _rangeText = (currentPageIndex + 1).ToString();
        _selection = [currentPageIndex];
        Recalculate();
    }

    public int PageCount => _pageCount;

    /// <summary>Page range such as <c>1-3,5</c>. Empty means every page.</summary>
    public string? RangeText
    {
        get => _rangeText;
        set
        {
            if (!SetProperty(ref _rangeText, value)) return;
            Recalculate();
        }
    }

    public string? RangeError
    {
        get => _rangeError;
        private set
        {
            if (!SetProperty(ref _rangeError, value)) return;
            RaisePropertyChanged(nameof(HasRangeError));
        }
    }

    public bool HasRangeError => !string.IsNullOrEmpty(_rangeError);

    public PageOperation Operation
    {
        get => _operation;
        set
        {
            if (!SetProperty(ref _operation, value)) return;
            RaiseAll(nameof(IsRotateLeft), nameof(IsRotateRight), nameof(IsDelete), nameof(IsExtract),
                nameof(SummaryText));
        }
    }

    public bool IsRotateLeft => _operation == PageOperation.RotateLeft;
    public bool IsRotateRight => _operation == PageOperation.RotateRight;
    public bool IsDelete => _operation == PageOperation.Delete;
    public bool IsExtract => _operation == PageOperation.Extract;

    /// <summary>Zero based indices of the pages the range selects.</summary>
    public IReadOnlyList<int> SelectedPageIndices => _selection;

    /// <summary>How many pages the new file will contain.</summary>
    public int ResultingPageCount => _operation switch
    {
        PageOperation.Delete => Math.Max(1, _pageCount - _selection.Count),
        PageOperation.Extract => Math.Max(1, _selection.Count),
        _ => _pageCount
    };

    public string SummaryText => HasRangeError
        ? string.Empty
        : ErrorMessages.Format(Strings.PageOperationSummary, ResultingPageCount);

    public bool CanApply => !HasRangeError && _selection.Count > 0;

    /// <summary>The edits to hand to the writer, expressed against the source page indices.</summary>
    public IReadOnlyList<PageEdit> BuildEdits() => _operation switch
    {
        PageOperation.RotateLeft => [.. _selection.Select(i => new PageEdit.Rotate(i, -90))],
        PageOperation.RotateRight => [.. _selection.Select(i => new PageEdit.Rotate(i, 90))],
        PageOperation.Delete => [.. _selection.Select(i => new PageEdit.Delete(i))],
        // Extraction is a reorder that keeps only the selected pages, in the order given.
        _ => [new PageEdit.Reorder(_selection)]
    };

    /// <summary>A suggested file name for the result, so the save dialog opens somewhere sensible.</summary>
    public string SuggestOutputName(string sourceName) => _operation switch
    {
        PageOperation.RotateLeft or PageOperation.RotateRight => Suffix(sourceName, "מסובב"),
        PageOperation.Delete => Suffix(sourceName, "לאחר מחיקה"),
        _ => Suffix(sourceName, "עמודים נבחרים")
    };

    private static string Suffix(string name, string suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        return $"{stem} - {suffix}.pdf";
    }

    private void Recalculate()
    {
        if (string.IsNullOrWhiteSpace(_rangeText))
        {
            RangeError = null;
            _selection = [.. Enumerable.Range(0, _pageCount)];
        }
        else
        {
            var parsed = PageRangeParser.Parse(_rangeText, _pageCount);
            if (parsed.Success)
            {
                RangeError = null;
                _selection = [.. parsed.ToPageNumbers().Select(p => p - 1)];
            }
            else
            {
                RangeError = ErrorMessages.ForRangeError(parsed.Error, parsed.Offending);
                _selection = [];
            }
        }

        RaiseAll(nameof(SelectedPageIndices), nameof(ResultingPageCount), nameof(SummaryText),
            nameof(CanApply));
    }
}
