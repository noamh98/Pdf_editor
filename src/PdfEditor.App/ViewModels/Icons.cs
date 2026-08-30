namespace PdfEditor.App.ViewModels;

/// <summary>
/// Path geometries for the interface icons.
/// </summary>
/// <remarks>
/// Drawn as vectors rather than loaded from an icon font or image set, so the application ships
/// no external asset, scales cleanly at every Windows display scaling factor, and inherits the
/// current theme's colours.
/// </remarks>
public static class Icons
{
    public const string Cursor = "M4,2 L4,17 L8,13 L11,19 L13,18 L10,12 L16,12 Z";
    public const string Text = "M3,3 H17 V6 H12 V18 H8 V6 H3 Z";
    public const string Rectangle = "M3,5 H17 V15 H3 Z M4.5,6.5 V13.5 H15.5 V6.5 Z";
    public const string Ellipse = "M10,4 A7,5.5 0 1 0 10,15 A7,5.5 0 1 0 10,4 Z M10,5.5 A5.5,4 0 1 1 10,13.5 A5.5,4 0 1 1 10,5.5 Z";
    public const string Line = "M3.6,15.4 L15.4,3.6 L16.4,4.6 L4.6,16.4 Z";
    public const string Arrow = "M3.6,15.4 L12,7 L13,8 L4.6,16.4 Z M11,4 H17 V10 H15.4 V6.7 L12.6,9.5 L11.5,8.4 L14.3,5.6 H11 Z";
    public const string Pen = "M3,17 L4.2,13.2 L13,4.4 L15.6,7 L6.8,15.8 Z M14,3.4 L15,2.4 A1.4,1.4 0 0 1 17.6,5 L16.6,6 Z";
    public const string Highlight = "M3,16 H17 V18 H3 Z M5,13 L11,4 L15,7.5 L9.5,13 Z";
    public const string Check = "M3.5,10.5 L5,9 L8,12 L15,4.5 L16.5,6 L8,15 Z";
    public const string Cross = "M4.5,3.5 L10,9 L15.5,3.5 L16.5,4.5 L11,10 L16.5,15.5 L15.5,16.5 L10,11 L4.5,16.5 L3.5,15.5 L9,10 L3.5,4.5 Z";
    public const string Signature = "M2,15 C5,15 5,7 8,7 C10,7 9,14 11,14 C13,14 13,9 15,9 C16.5,9 17,11 18,11 L18,12.5 C16,12.5 16,10.5 15,10.5 C13.8,10.5 13.6,15.5 11,15.5 C8.6,15.5 9.2,8.5 8,8.5 C6.4,8.5 6.6,16.5 2,16.5 Z";

    public const string Open = "M2,5 H8 L10,7 H18 V16 H2 Z M3.5,8.5 V14.5 H16.5 V8.5 Z";
    public const string Save = "M3,3 H14 L17,6 V17 H3 Z M6,3 V8 H13 V3 Z M5,11 H15 V17 H5 Z";
    public const string SaveAs = "M3,3 H12 L15,6 V10 H13.5 V7 H11 V4.5 H4.5 V15.5 H9 V17 H3 Z M13,12 H14.5 V14.5 H17 V16 H14.5 V18.5 H13 V16 H10.5 V14.5 H13 Z";
    public const string Export = "M3,12 H4.5 V15.5 H15.5 V12 H17 V17 H3 Z M9.25,2.5 H10.75 V9.4 L13.2,7 L14.3,8 L10,12.3 L5.7,8 L6.8,7 L9.25,9.4 Z";
    public const string Print = "M5,2 H15 V6 H5 Z M2,7 H18 V14 H15 V11 H5 V14 H2 Z M6,12 H14 V18 H6 Z";
    public const string Undo = "M6,6 H12 A5,5 0 0 1 12,16 H7 V14.4 H12 A3.4,3.4 0 0 0 12,7.6 H6 V10.5 L1.6,6.8 L6,3.1 Z";
    public const string Redo = "M14,6 H8 A5,5 0 0 0 8,16 H13 V14.4 H8 A3.4,3.4 0 0 1 8,7.6 H14 V10.5 L18.4,6.8 L14,3.1 Z";
    public const string Search = "M8.8,2.5 A6.3,6.3 0 1 1 8.8,15.1 A6.3,6.3 0 1 1 8.8,2.5 Z M8.8,4.1 A4.7,4.7 0 1 0 8.8,13.5 A4.7,4.7 0 1 0 8.8,4.1 Z M13.4,12.3 L14.6,11.1 L18.4,14.9 L17.2,16.1 Z";
    public const string Settings = "M10,6.6 A3.4,3.4 0 1 0 10,13.4 A3.4,3.4 0 1 0 10,6.6 Z M10,8.2 A1.8,1.8 0 1 1 10,11.8 A1.8,1.8 0 1 1 10,8.2 Z M8.9,1.5 H11.1 L11.5,3.7 L13.3,4.5 L15.2,3.3 L16.7,4.8 L15.5,6.7 L16.3,8.5 L18.5,8.9 V11.1 L16.3,11.5 L15.5,13.3 L16.7,15.2 L15.2,16.7 L13.3,15.5 L11.5,16.3 L11.1,18.5 H8.9 L8.5,16.3 L6.7,15.5 L4.8,16.7 L3.3,15.2 L4.5,13.3 L3.7,11.5 L1.5,11.1 V8.9 L3.7,8.5 L4.5,6.7 L3.3,4.8 L4.8,3.3 L6.7,4.5 L8.5,3.7 Z";
    public const string ZoomIn = "M8.8,2.5 A6.3,6.3 0 1 1 8.8,15.1 A6.3,6.3 0 1 1 8.8,2.5 Z M8.8,4.1 A4.7,4.7 0 1 0 8.8,13.5 A4.7,4.7 0 1 0 8.8,4.1 Z M8,6 H9.6 V8 H11.6 V9.6 H9.6 V11.6 H8 V9.6 H6 V8 H8 Z M13.4,12.3 L14.6,11.1 L18.4,14.9 L17.2,16.1 Z";
    public const string ZoomOut = "M8.8,2.5 A6.3,6.3 0 1 1 8.8,15.1 A6.3,6.3 0 1 1 8.8,2.5 Z M8.8,4.1 A4.7,4.7 0 1 0 8.8,13.5 A4.7,4.7 0 1 0 8.8,4.1 Z M6,8 H11.6 V9.6 H6 Z M13.4,12.3 L14.6,11.1 L18.4,14.9 L17.2,16.1 Z";
    public const string FitWidth = "M2,4 H18 V16 H2 Z M3.5,5.5 V14.5 H16.5 V5.5 Z M5,10 L7.5,7.5 V9.2 H12.5 V7.5 L15,10 L12.5,12.5 V10.8 H7.5 V12.5 Z";
    public const string FitPage = "M5,2 H15 V18 H5 Z M6.5,3.5 V16.5 H13.5 V3.5 Z M10,5 L12,7.5 H10.8 V12.5 H12 L10,15 L8,12.5 H9.2 V7.5 H8 Z";
    public const string Merge = "M2,3 H9 V8 H2 Z M2,12 H9 V17 H2 Z M11,7.5 H16 V5 L19,8.75 L16,12.5 V10 H11 Z";
    public const string Split = "M11,3 H18 V8 H11 Z M11,12 H18 V17 H2 V12 Z M9,7.5 H4 V5 L1,8.75 L4,12.5 V10 H9 Z";
    public const string Pages = "M4,2 H12 L16,6 V18 H4 Z M5.5,3.5 V16.5 H14.5 V7 H11 V3.5 Z";
    public const string Ocr = "M2,2 H7 V3.6 H3.6 V7 H2 Z M13,2 H18 V7 H16.4 V3.6 H13 Z M2,13 H3.6 V16.4 H7 V18 H2 Z M16.4,13 H18 V18 H13 V16.4 H16.4 Z M6,6.5 H14 V8 H10.8 V13.5 H9.2 V8 H6 Z";
    public const string Chevron = "M7,4 L13,10 L7,16 Z";
}
