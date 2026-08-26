using System.Globalization;

namespace PdfEditor.Core.Text;

/// <summary>
/// Maps Unicode code points to their <see cref="BidiClass"/>.
/// </summary>
/// <remarks>
/// This is a pragmatic classifier rather than a full copy of DerivedBidiClass.txt: explicit ranges
/// cover every class that matters for Hebrew, Arabic, Latin and digits, and everything else is
/// derived from the general Unicode category. Behaviour is exact for the scripts this application
/// targets (Hebrew + Latin + digits + common punctuation); exotic scripts fall back to
/// <see cref="BidiClass.L"/> or <see cref="BidiClass.ON"/>. See docs/KNOWN_LIMITATIONS.md.
/// </remarks>
public static class BidiClassifier
{
    private readonly record struct Range(int Start, int End, BidiClass Class);

    // Ordered, non-overlapping ranges. Binary searched.
    private static readonly Range[] Ranges = BuildRanges();

    public static BidiClass Classify(int codePoint)
    {
        int lo = 0, hi = Ranges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            ref readonly var r = ref Ranges[mid];
            if (codePoint < r.Start) hi = mid - 1;
            else if (codePoint > r.End) lo = mid + 1;
            else return r.Class;
        }
        return FromCategory(codePoint);
    }

    public static BidiClass Classify(char c) => Classify((int)c);

    private static BidiClass FromCategory(int codePoint)
    {
        if (codePoint > 0x10FFFF) return BidiClass.L;
        var cat = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0);
        return cat switch
        {
            UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark => BidiClass.NSM,
            UnicodeCategory.Format => BidiClass.BN,
            UnicodeCategory.Control => BidiClass.BN,
            UnicodeCategory.DecimalDigitNumber => BidiClass.EN,
            UnicodeCategory.SpaceSeparator => BidiClass.WS,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => BidiClass.B,
            UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol => BidiClass.ON,
            _ => BidiClass.L
        };
    }

    private static Range[] BuildRanges()
    {
        var list = new List<Range>
        {
            // --- Controls / boundary neutrals -----------------------------------------------
            new(0x0000, 0x0008, BidiClass.BN),
            new(0x0009, 0x0009, BidiClass.S),
            new(0x000A, 0x000A, BidiClass.B),
            new(0x000B, 0x000B, BidiClass.S),
            new(0x000C, 0x000C, BidiClass.WS),
            new(0x000D, 0x000D, BidiClass.B),
            new(0x000E, 0x001B, BidiClass.BN),
            new(0x001C, 0x001E, BidiClass.B),
            new(0x001F, 0x001F, BidiClass.S),
            new(0x0020, 0x0020, BidiClass.WS),
            new(0x0021, 0x0022, BidiClass.ON),
            new(0x0023, 0x0025, BidiClass.ET),
            new(0x0026, 0x002A, BidiClass.ON),
            new(0x002B, 0x002B, BidiClass.ES),
            new(0x002C, 0x002C, BidiClass.CS),
            new(0x002D, 0x002D, BidiClass.ES),
            new(0x002E, 0x002F, BidiClass.CS),
            new(0x0030, 0x0039, BidiClass.EN),
            new(0x003A, 0x003A, BidiClass.CS),
            new(0x003B, 0x0040, BidiClass.ON),
            new(0x0041, 0x005A, BidiClass.L),
            new(0x005B, 0x0060, BidiClass.ON),
            new(0x0061, 0x007A, BidiClass.L),
            new(0x007B, 0x007E, BidiClass.ON),
            new(0x007F, 0x0084, BidiClass.BN),
            new(0x0085, 0x0085, BidiClass.B),
            new(0x0086, 0x009F, BidiClass.BN),
            new(0x00A0, 0x00A0, BidiClass.CS),
            new(0x00A1, 0x00A1, BidiClass.ON),
            new(0x00A2, 0x00A5, BidiClass.ET),
            new(0x00A6, 0x00A9, BidiClass.ON),
            new(0x00AA, 0x00AA, BidiClass.L),
            new(0x00AB, 0x00AC, BidiClass.ON),
            new(0x00AD, 0x00AD, BidiClass.BN),
            new(0x00AE, 0x00AF, BidiClass.ON),
            new(0x00B0, 0x00B1, BidiClass.ET),
            new(0x00B2, 0x00B3, BidiClass.EN),
            new(0x00B4, 0x00B4, BidiClass.ON),
            new(0x00B6, 0x00B8, BidiClass.ON),
            new(0x00B9, 0x00B9, BidiClass.EN),
            new(0x00BA, 0x00BA, BidiClass.L),
            new(0x00BB, 0x00BF, BidiClass.ON),

            // --- Hebrew ---------------------------------------------------------------------
            new(0x0590, 0x0590, BidiClass.R),
            new(0x0591, 0x05BD, BidiClass.NSM), // te'amim + nikud: inherit the base letter's level
            new(0x05BE, 0x05BE, BidiClass.R),   // maqaf
            new(0x05BF, 0x05BF, BidiClass.NSM), // rafe
            new(0x05C0, 0x05C0, BidiClass.R),   // paseq
            new(0x05C1, 0x05C2, BidiClass.NSM), // shin/sin dot
            new(0x05C3, 0x05C3, BidiClass.R),   // sof pasuq
            new(0x05C4, 0x05C5, BidiClass.NSM),
            new(0x05C6, 0x05C6, BidiClass.R),   // nun hafukha
            new(0x05C7, 0x05C7, BidiClass.NSM), // qamats qatan
            new(0x05C8, 0x05CF, BidiClass.R),
            new(0x05D0, 0x05EA, BidiClass.R),   // alef..tav
            new(0x05EB, 0x05EE, BidiClass.R),
            new(0x05EF, 0x05F4, BidiClass.R),   // yod triangle, ligatures, geresh, gershayim
            new(0x05F5, 0x05FF, BidiClass.R),

            // --- Arabic (needed so mixed documents do not misbehave) -------------------------
            new(0x0600, 0x0605, BidiClass.AN),
            new(0x0606, 0x0607, BidiClass.ON),
            new(0x0608, 0x0608, BidiClass.AL),
            new(0x0609, 0x060A, BidiClass.ET),
            new(0x060B, 0x060B, BidiClass.AL),
            new(0x060C, 0x060C, BidiClass.CS),
            new(0x060D, 0x060D, BidiClass.AL),
            new(0x060E, 0x060F, BidiClass.ON),
            new(0x0610, 0x061A, BidiClass.NSM),
            new(0x061B, 0x064A, BidiClass.AL),
            new(0x064B, 0x065F, BidiClass.NSM),
            new(0x0660, 0x0669, BidiClass.AN),
            new(0x066A, 0x066A, BidiClass.ET),
            new(0x066B, 0x066C, BidiClass.AN),
            new(0x066D, 0x066F, BidiClass.AL),
            new(0x0670, 0x0670, BidiClass.NSM),
            new(0x0671, 0x06D5, BidiClass.AL),
            new(0x06D6, 0x06DC, BidiClass.NSM),
            new(0x06DD, 0x06DD, BidiClass.AN),
            new(0x06DE, 0x06E4, BidiClass.NSM),
            new(0x06E5, 0x06E6, BidiClass.AL),
            new(0x06E7, 0x06E8, BidiClass.NSM),
            new(0x06E9, 0x06E9, BidiClass.ON),
            new(0x06EA, 0x06ED, BidiClass.NSM),
            new(0x06EE, 0x06EF, BidiClass.AL),
            new(0x06F0, 0x06F9, BidiClass.EN),
            new(0x06FA, 0x070D, BidiClass.AL),
            new(0x070F, 0x074A, BidiClass.AL),
            new(0x074D, 0x07A5, BidiClass.AL),
            new(0x07A6, 0x07B0, BidiClass.NSM),
            new(0x07B1, 0x07B1, BidiClass.AL),
            new(0x07C0, 0x07EA, BidiClass.R),
            new(0x07F4, 0x07F5, BidiClass.R),
            new(0x07FA, 0x07FA, BidiClass.R),
            new(0x0800, 0x0815, BidiClass.R),
            new(0x0830, 0x085E, BidiClass.R),

            // --- Whitespace / separators ------------------------------------------------------
            new(0x1680, 0x1680, BidiClass.WS),
            new(0x180B, 0x180E, BidiClass.BN),
            new(0x200B, 0x200D, BidiClass.BN),
            new(0x200E, 0x200E, BidiClass.L),   // LRM
            new(0x200F, 0x200F, BidiClass.R),   // RLM
            new(0x2000, 0x200A, BidiClass.WS),
            new(0x2028, 0x2028, BidiClass.WS),
            new(0x2029, 0x2029, BidiClass.B),

            // --- Explicit formatting ---------------------------------------------------------
            new(0x202A, 0x202A, BidiClass.LRE),
            new(0x202B, 0x202B, BidiClass.RLE),
            new(0x202C, 0x202C, BidiClass.PDF),
            new(0x202D, 0x202D, BidiClass.LRO),
            new(0x202E, 0x202E, BidiClass.RLO),
            new(0x202F, 0x202F, BidiClass.CS),
            new(0x2060, 0x2064, BidiClass.BN),
            new(0x2066, 0x2066, BidiClass.LRI),
            new(0x2067, 0x2067, BidiClass.RLI),
            new(0x2068, 0x2068, BidiClass.FSI),
            new(0x2069, 0x2069, BidiClass.PDI),
            new(0x206A, 0x206F, BidiClass.BN),

            // --- Numeric-ish ------------------------------------------------------------------
            new(0x2044, 0x2044, BidiClass.CS),
            new(0x2070, 0x2070, BidiClass.EN),
            new(0x2074, 0x2079, BidiClass.EN),
            new(0x207A, 0x207B, BidiClass.ES),
            new(0x2080, 0x2089, BidiClass.EN),
            new(0x208A, 0x208B, BidiClass.ES),
            new(0x20A0, 0x20C0, BidiClass.ET),
            new(0x2212, 0x2212, BidiClass.ES),
            new(0x2213, 0x2213, BidiClass.ET),

            new(0x205F, 0x205F, BidiClass.WS),
            new(0x3000, 0x3000, BidiClass.WS),

            // --- Hebrew presentation forms ----------------------------------------------------
            new(0xFB1D, 0xFB4F, BidiClass.R),
            // --- Arabic presentation forms ----------------------------------------------------
            new(0xFB50, 0xFDFF, BidiClass.AL),
            new(0xFE70, 0xFEFE, BidiClass.AL),
            new(0xFEFF, 0xFEFF, BidiClass.BN),

            new(0xFF0B, 0xFF0B, BidiClass.ES),
            new(0xFF0C, 0xFF0C, BidiClass.CS),
            new(0xFF0D, 0xFF0D, BidiClass.ES),
            new(0xFF0E, 0xFF0F, BidiClass.CS),
            new(0xFF10, 0xFF19, BidiClass.EN),
            new(0xFF1A, 0xFF1A, BidiClass.CS),
        };

        list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return list.ToArray();
    }
}
