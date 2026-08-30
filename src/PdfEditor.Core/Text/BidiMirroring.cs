namespace PdfEditor.Core.Text;

/// <summary>
/// Rule L4 mirroring plus the paired-bracket data needed by rule N0.
/// Covers the bracket and mathematical characters that occur in Hebrew/English documents.
/// </summary>
public static class BidiMirroring
{
    private static readonly Dictionary<char, char> Pairs = new()
    {
        ['('] = ')',
        [')'] = '(',
        ['['] = ']',
        [']'] = '[',
        ['{'] = '}',
        ['}'] = '{',
        ['<'] = '>',
        ['>'] = '<',
        ['«'] = '»',
        ['»'] = '«', // « »
        ['‹'] = '›',
        ['›'] = '‹', // ‹ ›
        ['‘'] = '’',
        ['’'] = '‘',
        ['“'] = '”',
        ['”'] = '“',
        ['⁅'] = '⁆',
        ['⁆'] = '⁅',
        ['⁽'] = '⁾',
        ['⁾'] = '⁽',
        ['₍'] = '₎',
        ['₎'] = '₍',
        ['≤'] = '≥',
        ['≥'] = '≤',
        ['≪'] = '≫',
        ['≫'] = '≪',
        ['⌈'] = '⌉',
        ['⌉'] = '⌈',
        ['⌊'] = '⌋',
        ['⌋'] = '⌊',
        ['〈'] = '〉',
        ['〉'] = '〈',
        ['〈'] = '〉',
        ['〉'] = '〈',
        ['《'] = '》',
        ['》'] = '《',
        ['（'] = '）',
        ['）'] = '（',
        ['［'] = '］',
        ['］'] = '［',
        ['｛'] = '｝',
        ['｝'] = '｛',
    };

    private static readonly HashSet<char> Opening = ['(',
        '[',
        '{',
        '⁅',
        '⁽',
        '₍',
        '⌈',
        '⌊',
        '〈',
        '〈',
        '《',
        '（',
        '［',
        '｛'];

    private static readonly HashSet<char> Closing = [')',
        ']',
        '}',
        '⁆',
        '⁾',
        '₎',
        '⌉',
        '⌋',
        '〉',
        '〉',
        '》',
        '）',
        '］',
        '｝'];

    public static char Mirror(char c) => Pairs.TryGetValue(c, out var m) ? m : c;

    public static bool HasMirror(char c) => Pairs.ContainsKey(c);

    public static bool IsOpeningBracket(char c) => Opening.Contains(c);

    public static bool IsClosingBracket(char c) => Closing.Contains(c);

    /// <summary>
    /// Canonical equivalence for bracket matching (BD16): U+2329 and U+3008 are treated as one.
    /// </summary>
    public static char CanonicalBracket(char c) => c switch
    {
        '〈' => '〈',
        '〉' => '〉',
        _ => c
    };
}
