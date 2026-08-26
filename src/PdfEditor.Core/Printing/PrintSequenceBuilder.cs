namespace PdfEditor.Core.Printing;

/// <summary>
/// Turns a document's pages into the exact sequence that will be sent to the printer.
/// </summary>
/// <remarks>
/// This is the logic behind the "print every content page on its own sheet" option. It never
/// modifies the source document; the caller materialises the sequence into a temporary job.
/// Rules implemented:
/// <list type="number">
/// <item>A blank page is placed between two consecutive content pages.</item>
/// <item>No blank page is appended after the final page.</item>
/// <item>A blank page already present in the document is reused instead of adding a second one.</item>
/// <item>An inserted blank copies the size, orientation and rotation of the page before it.</item>
/// </list>
/// </remarks>
public static class PrintSequenceBuilder
{
    public static PrintSequence Build(IReadOnlyList<PrintPageInfo> pages, PrintSequenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(options);

        var selected = SelectPages(pages, options.SelectedPageIndices);
        var slots = new List<PrintSlot>(selected.Count * 2);
        int content = 0, existingBlank = 0, insertedBlank = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            var page = selected[i];
            var kind = page.IsBlank ? PrintSlotKind.ExistingBlank : PrintSlotKind.Content;
            if (kind == PrintSlotKind.Content) content++; else existingBlank++;

            slots.Add(new PrintSlot(kind, page.SourcePageIndex, page.WidthPoints, page.HeightPoints, page.Rotation));

            if (!options.SeparateSheetsPerContentPage) continue;
            if (i == selected.Count - 1) continue;               // rule 2: never after the last page

            var next = selected[i + 1];
            if (page.IsBlank || next.IsBlank) continue;           // rule 3: a blank is already there

            slots.Add(new PrintSlot(PrintSlotKind.InsertedBlank, null,
                page.WidthPoints, page.HeightPoints, page.Rotation)); // rule 4
            insertedBlank++;
        }

        return new PrintSequence(slots, content, insertedBlank, existingBlank);
    }

    private static List<PrintPageInfo> SelectPages(
        IReadOnlyList<PrintPageInfo> pages,
        IReadOnlyList<int>? selection)
    {
        if (selection is null) return [.. pages];

        var byIndex = pages.ToDictionary(p => p.SourcePageIndex);
        var result = new List<PrintPageInfo>(selection.Count);
        foreach (int index in selection)
            if (byIndex.TryGetValue(index, out var page))
                result.Add(page);
        return result;
    }
}
