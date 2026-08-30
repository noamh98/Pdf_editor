using System.Collections.ObjectModel;
using PdfEditor.Core.Localization;
using PdfEditor.Core.Printing;

namespace PdfEditor.App.ViewModels;

/// <summary>One entry in the preview strip.</summary>
public sealed class PrintSlotViewModel(int ordinal, PrintSlot slot)
{
    public int Ordinal { get; } = ordinal;
    public PrintSlot Slot { get; } = slot;

    public bool IsContent => Slot.Kind == PrintSlotKind.Content;

    /// <summary>
    /// Blank sheets are labelled as well as styled, so the preview does not rely on colour alone.
    /// </summary>
    public string Label => Slot.Kind switch
    {
        PrintSlotKind.Content => (Slot.SourcePageIndex + 1)?.ToString() ?? "",
        PrintSlotKind.ExistingBlank => "ריק",
        _ => "ריק (נוסף)"
    };

    public string Caption => Slot.Kind switch
    {
        PrintSlotKind.Content => $"עמוד {(Slot.SourcePageIndex ?? 0) + 1}",
        PrintSlotKind.ExistingBlank => "עמוד ריק קיים במסמך",
        _ => "עמוד ריק שנוסף לצורך ההפרדה"
    };

    public double AspectRatio => Slot.HeightPoints <= 0 ? 1 : Slot.WidthPoints / Slot.HeightPoints;
}

/// <summary>
/// Drives the print dialog: the option that forces one content page per sheet, the resulting page
/// sequence, and an honest summary of what will actually be sent to the printer.
/// </summary>
public sealed class PrintPreviewViewModel : ViewModelBase
{
    private readonly IReadOnlyList<PrintPageInfo> _pages;
    private bool _separateSheets;
    private bool _assumeDuplex = true;
    private string? _rangeText;
    private string? _rangeError;
    private PrintSequence _sequence;

    public PrintPreviewViewModel(IReadOnlyList<PrintPageInfo> pages, bool separateSheetsDefault)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _separateSheets = separateSheetsDefault;
        _sequence = Build();
        Slots = new ObservableCollection<PrintSlotViewModel>(Materialise(_sequence));
    }

    public ObservableCollection<PrintSlotViewModel> Slots { get; }

    public ObservableCollection<PrinterInfo> Printers { get; } = [];

    public PrinterInfo? SelectedPrinter { get; set; }

    public int Copies { get; set; } = 1;

    /// <summary>"הדפס כל עמוד תוכן על גיליון נפרד".</summary>
    public bool SeparateSheetsPerContentPage
    {
        get => _separateSheets;
        set
        {
            if (!SetProperty(ref _separateSheets, value)) return;
            Refresh();
        }
    }

    /// <summary>Whether the sheet estimate assumes the driver is forcing double-sided output.</summary>
    public bool AssumeDuplex
    {
        get => _assumeDuplex;
        set
        {
            if (!SetProperty(ref _assumeDuplex, value)) return;
            RaisePropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>Page range such as <c>1-3,5</c>. Empty means the whole document.</summary>
    public string? RangeText
    {
        get => _rangeText;
        set
        {
            if (!SetProperty(ref _rangeText, value)) return;
            Refresh();
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

    public PrintSequence Sequence => _sequence;

    public string SummaryText => ErrorMessages.Format(Strings.PrintSummary,
        _sequence.ContentPageCount,
        _sequence.InsertedBlankCount + _sequence.ExistingBlankCount,
        _sequence.EstimatedSheets(_assumeDuplex));

    public static string LimitationText => Strings.SeparateSheetsLimitation;

    public static string OptionHint => Strings.SeparateSheetsHint;

    private void Refresh()
    {
        _sequence = Build();
        Slots.Clear();
        foreach (var slot in Materialise(_sequence)) Slots.Add(slot);
        RaiseAll(nameof(Sequence), nameof(SummaryText));
    }

    private PrintSequence Build()
    {
        IReadOnlyList<int>? selection = null;
        if (!string.IsNullOrWhiteSpace(_rangeText))
        {
            var parsed = Core.Documents.PageRangeParser.Parse(_rangeText, _pages.Count);
            if (parsed.Success)
            {
                RangeError = null;
                selection = parsed.ToPageNumbers().Select(p => p - 1).ToList();
            }
            else
            {
                RangeError = ErrorMessages.ForRangeError(parsed.Error, parsed.Offending);
            }
        }
        else
        {
            RangeError = null;
        }

        return PrintSequenceBuilder.Build(_pages, new PrintSequenceOptions
        {
            SeparateSheetsPerContentPage = _separateSheets,
            SelectedPageIndices = selection
        });
    }

    private static IEnumerable<PrintSlotViewModel> Materialise(PrintSequence sequence) =>
        sequence.Slots.Select((slot, index) => new PrintSlotViewModel(index + 1, slot));
}
