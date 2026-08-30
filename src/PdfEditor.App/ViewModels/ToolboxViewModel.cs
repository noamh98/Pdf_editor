using PdfEditor.Core.Annotations;
using PdfEditor.Core.Localization;

namespace PdfEditor.App.ViewModels;

/// <summary>The tools the user can pick, including plain selection.</summary>
public enum EditorTool
{
    Select,
    TextBox,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Ink,
    Highlight,
    CheckMark,
    CrossMark,
    Signature
}

public sealed class ToolItemViewModel(EditorTool tool, string label, string glyph, string shortcut)
    : ViewModelBase
{
    private bool _isSelected;

    public EditorTool Tool { get; } = tool;
    public string Label { get; } = label;

    /// <summary>Path geometry for the icon, so no image asset or icon font is needed.</summary>
    public string Glyph { get; } = glyph;

    public string Shortcut { get; } = shortcut;

    public string AccessibleName => string.IsNullOrEmpty(Shortcut) ? Label : $"{Label} ({Shortcut})";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>The editing tool strip.</summary>
public sealed class ToolboxViewModel : ViewModelBase
{
    private EditorTool _activeTool = EditorTool.Select;

    public ToolboxViewModel()
    {
        Tools =
        [
            new(EditorTool.Select, Strings.ToolSelect, Icons.Cursor, "Esc"),
            new(EditorTool.TextBox, Strings.ToolTextBox, Icons.Text, "T"),
            new(EditorTool.Rectangle, Strings.ToolRectangle, Icons.Rectangle, "R"),
            new(EditorTool.Ellipse, Strings.ToolEllipse, Icons.Ellipse, "O"),
            new(EditorTool.Line, Strings.ToolLine, Icons.Line, "L"),
            new(EditorTool.Arrow, Strings.ToolArrow, Icons.Arrow, "A"),
            new(EditorTool.Ink, Strings.ToolInk, Icons.Pen, "D"),
            new(EditorTool.Highlight, Strings.ToolHighlight, Icons.Highlight, "H"),
            new(EditorTool.CheckMark, Strings.ToolCheckMark, Icons.Check, "V"),
            new(EditorTool.CrossMark, Strings.ToolCrossMark, Icons.Cross, "X"),
            new(EditorTool.Signature, Strings.ToolSignature, Icons.Signature, "S")
        ];
        Sync();
    }

    public IReadOnlyList<ToolItemViewModel> Tools { get; }

    public EditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (!SetProperty(ref _activeTool, value)) return;
            Sync();
            RaisePropertyChanged(nameof(IsSelectionTool));
            ActiveToolChanged?.Invoke(this, value);
        }
    }

    public bool IsSelectionTool => _activeTool == EditorTool.Select;

    public event EventHandler<EditorTool>? ActiveToolChanged;

    public void Select(EditorTool tool) => ActiveTool = tool;

    public void Reset() => ActiveTool = EditorTool.Select;

    private void Sync()
    {
        foreach (var tool in Tools) tool.IsSelected = tool.Tool == _activeTool;
    }

    /// <summary>The annotation a tool creates, or null for the selection tool.</summary>
    public static AnnotationKind? KindFor(EditorTool tool) => tool switch
    {
        EditorTool.TextBox => AnnotationKind.TextBox,
        EditorTool.Rectangle => AnnotationKind.Rectangle,
        EditorTool.Ellipse => AnnotationKind.Ellipse,
        EditorTool.Line => AnnotationKind.Line,
        EditorTool.Arrow => AnnotationKind.Arrow,
        EditorTool.Ink => AnnotationKind.Ink,
        EditorTool.Highlight => AnnotationKind.Highlight,
        EditorTool.CheckMark => AnnotationKind.CheckMark,
        EditorTool.CrossMark => AnnotationKind.CrossMark,
        EditorTool.Signature => AnnotationKind.Signature,
        _ => null
    };
}
