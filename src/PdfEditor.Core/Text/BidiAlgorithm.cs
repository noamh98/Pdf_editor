namespace PdfEditor.Core.Text;

/// <summary>Requested base direction for a paragraph.</summary>
public enum BidiParagraphDirection
{
    /// <summary>Determine the base direction from the first strong character (UAX#9 rules P2/P3).</summary>
    Auto = 0,
    LeftToRight = 1,
    RightToLeft = 2
}

/// <summary>Result of running the bidirectional algorithm over one paragraph.</summary>
public sealed class BidiResult
{
    internal BidiResult(string text, byte paragraphLevel, byte[] levels, int[] visualToLogical)
    {
        Text = text;
        ParagraphLevel = paragraphLevel;
        Levels = levels;
        VisualToLogical = visualToLogical;
    }

    /// <summary>The original logical-order text.</summary>
    public string Text { get; }

    /// <summary>Resolved paragraph embedding level (0 = LTR, 1 = RTL).</summary>
    public byte ParagraphLevel { get; }

    /// <summary>Resolved embedding level for every UTF-16 unit of <see cref="Text"/>.</summary>
    public byte[] Levels { get; }

    /// <summary>
    /// For each visual position, the index of the logical character displayed there.
    /// </summary>
    public int[] VisualToLogical { get; }

    public bool IsRightToLeftParagraph => (ParagraphLevel & 1) == 1;
}

/// <summary>
/// Implementation of the Unicode Bidirectional Algorithm (UAX#9) used to convert logical-order
/// text into the visual order that must be written into a PDF content stream.
/// </summary>
/// <remarks>
/// PDF text-showing operators place glyphs strictly left to right in the order supplied, so any
/// Hebrew (or mixed Hebrew/Latin/numeric) string has to be reordered by the application before it
/// is drawn. Rules implemented: P2-P3, X1-X10, W1-W7, N0-N2, I1-I2, L1-L4.
/// </remarks>
public static class BidiAlgorithm
{
    private const int MaxDepth = 125;

    public static BidiResult Analyze(string text, BidiParagraphDirection direction = BidiParagraphDirection.Auto)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return new BidiResult(text, (byte)(direction == BidiParagraphDirection.RightToLeft ? 1 : 0), [], []);

        var initialTypes = new BidiClass[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
            initialTypes[i] = BidiClassifier.Classify(cp);
            if (cp > 0xFFFF && i + 1 < text.Length)
            {
                initialTypes[i + 1] = initialTypes[i];
                i++;
            }
        }

        var state = new BidiState(text, initialTypes);
        byte paragraphLevel = direction switch
        {
            BidiParagraphDirection.LeftToRight => 0,
            BidiParagraphDirection.RightToLeft => 1,
            _ => state.DetermineParagraphEmbeddingLevel(0, initialTypes.Length)
        };

        state.Run(paragraphLevel);
        byte[] levels = state.GetLevels(paragraphLevel);
        int[] visual = ComputeVisualOrder(levels, state.InitialTypes, paragraphLevel);
        return new BidiResult(text, paragraphLevel, levels, visual);
    }

    /// <summary>
    /// Converts logical-order text into visual order, applying mirroring (rule L4).
    /// This is the string that must be handed to a PDF text-showing operator.
    /// </summary>
    public static string ToVisual(string text, BidiParagraphDirection direction = BidiParagraphDirection.Auto)
    {
        var r = Analyze(text, direction);
        return ToVisual(r);
    }

    public static string ToVisual(BidiResult result)
    {
        var text = result.Text;
        if (text.Length == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (int logical in result.VisualToLogical)
        {
            char c = text[logical];
            if ((result.Levels[logical] & 1) == 1)
                c = BidiMirroring.Mirror(c);
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>True when the paragraph's resolved base direction is right-to-left.</summary>
    public static bool IsRightToLeft(string text) => Analyze(text).IsRightToLeftParagraph;

    private static int[] ComputeVisualOrder(byte[] levels, BidiClass[] initialTypes, byte paragraphLevel)
    {
        int n = levels.Length;
        // L1: reset trailing whitespace / separators to the paragraph level.
        var lineLevels = (byte[])levels.Clone();
        for (int i = 0; i < n; i++)
        {
            var t = initialTypes[i];
            if (t == BidiClass.B || t == BidiClass.S)
            {
                lineLevels[i] = paragraphLevel;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (IsWhitespaceOrIsolateFormat(initialTypes[j])) lineLevels[j] = paragraphLevel;
                    else break;
                }
            }
        }
        for (int j = n - 1; j >= 0; j--)
        {
            if (IsWhitespaceOrIsolateFormat(initialTypes[j])) lineLevels[j] = paragraphLevel;
            else break;
        }

        // L2: reverse contiguous runs, from the highest level down to the lowest odd level.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        byte highest = 0;
        byte lowestOdd = MaxDepth + 2;
        foreach (byte lv in lineLevels)
        {
            if (lv > highest) highest = lv;
            if ((lv & 1) == 1 && lv < lowestOdd) lowestOdd = lv;
        }

        for (int level = highest; level >= lowestOdd; level--)
        {
            for (int i = 0; i < n; i++)
            {
                if (lineLevels[i] < level) continue;
                int start = i;
                while (i + 1 < n && lineLevels[i + 1] >= level) i++;
                Array.Reverse(order, start, i - start + 1);
            }
        }
        return order;
    }

    private static bool IsWhitespaceOrIsolateFormat(BidiClass t) => t is BidiClass.WS
        or BidiClass.LRE or BidiClass.RLE or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF
        or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI or BidiClass.BN;

    // ---------------------------------------------------------------------------------------
    private sealed class BidiState
    {
        public readonly BidiClass[] InitialTypes;
        private readonly BidiClass[] _types;
        private readonly byte[] _levels;
        private readonly int[] _matchingPdi;
        private readonly int[] _matchingIsolate;
        private readonly int _length;

        private readonly string _source;

        public BidiState(string source, BidiClass[] initialTypes)
        {
            _source = source;
            InitialTypes = initialTypes;
            _length = initialTypes.Length;
            _types = (BidiClass[])initialTypes.Clone();
            _levels = new byte[_length];
            _matchingPdi = new int[_length];
            _matchingIsolate = new int[_length];
            DetermineMatchingIsolates();
        }

        // BD9 / BD10
        private void DetermineMatchingIsolates()
        {
            Array.Fill(_matchingPdi, -1);
            Array.Fill(_matchingIsolate, -1);
            for (int i = 0; i < _length; i++)
            {
                if (!IsIsolateInitiator(_types[i])) continue;
                int depth = 1;
                int j = i + 1;
                for (; j < _length; j++)
                {
                    var t = _types[j];
                    if (IsIsolateInitiator(t)) depth++;
                    else if (t == BidiClass.PDI)
                    {
                        if (--depth == 0)
                        {
                            _matchingPdi[i] = j;
                            _matchingIsolate[j] = i;
                            break;
                        }
                    }
                }
                if (_matchingPdi[i] < 0) _matchingPdi[i] = _length;
            }
        }

        private static bool IsIsolateInitiator(BidiClass t) => t is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI;

        // P2 / P3
        public byte DetermineParagraphEmbeddingLevel(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                var t = _types[i];
                if (t == BidiClass.L) return 0;
                if (t == BidiClass.R || t == BidiClass.AL) return 1;
                if (IsIsolateInitiator(t))
                {
                    int m = _matchingPdi[i];
                    i = m >= end ? end : m;
                }
            }
            return 0;
        }

        public void Run(byte paragraphLevel)
        {
            DetermineExplicitEmbeddingLevels(paragraphLevel);
            foreach (var seq in DetermineIsolatingRunSequences(paragraphLevel))
            {
                seq.ResolveWeakTypes();
                seq.ResolveNeutralTypes();
                seq.ResolveImplicitLevels();
                seq.ApplyLevelsAndTypes();
            }
        }

        // X1 - X8
        private void DetermineExplicitEmbeddingLevels(byte paragraphLevel)
        {
            Span<byte> stackLevel = stackalloc byte[MaxDepth + 2];
            Span<BidiClass> stackOverride = stackalloc BidiClass[MaxDepth + 2];
            Span<bool> stackIsolate = stackalloc bool[MaxDepth + 2];
            int sp = 0;
            stackLevel[0] = paragraphLevel;
            stackOverride[0] = BidiClass.ON;
            stackIsolate[0] = false;

            int overflowIsolate = 0, overflowEmbedding = 0, validIsolate = 0;

            for (int i = 0; i < _length; i++)
            {
                var t = _types[i];
                switch (t)
                {
                    case BidiClass.RLE:
                    case BidiClass.LRE:
                    case BidiClass.RLO:
                    case BidiClass.LRO:
                    case BidiClass.RLI:
                    case BidiClass.LRI:
                    case BidiClass.FSI:
                        {
                            bool isolate = t is BidiClass.RLI or BidiClass.LRI or BidiClass.FSI;
                            if (isolate) _levels[i] = stackLevel[sp];
                            var effective = t;
                            if (t == BidiClass.FSI)
                            {
                                int m = _matchingPdi[i];
                                effective = DetermineParagraphEmbeddingLevel(i + 1, Math.Min(m, _length)) == 1
                                    ? BidiClass.RLI : BidiClass.LRI;
                            }
                            bool rtl = effective is BidiClass.RLE or BidiClass.RLO or BidiClass.RLI;
                            byte newLevel = rtl
                                ? (byte)((stackLevel[sp] + 1) | 1)
                                : (byte)((stackLevel[sp] + 2) & ~1);
                            if (newLevel <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                            {
                                if (isolate) validIsolate++;
                                sp++;
                                stackLevel[sp] = newLevel;
                                stackOverride[sp] = effective switch
                                {
                                    BidiClass.LRO => BidiClass.L,
                                    BidiClass.RLO => BidiClass.R,
                                    _ => BidiClass.ON
                                };
                                stackIsolate[sp] = isolate;
                                if (!isolate) _levels[i] = newLevel;
                            }
                            else
                            {
                                if (isolate) overflowIsolate++;
                                else if (overflowIsolate == 0) overflowEmbedding++;
                            }
                            break;
                        }
                    case BidiClass.PDI:
                        if (overflowIsolate > 0) overflowIsolate--;
                        else if (validIsolate > 0)
                        {
                            overflowEmbedding = 0;
                            while (!stackIsolate[sp]) sp--;
                            sp--;
                            validIsolate--;
                        }
                        _levels[i] = stackLevel[sp];
                        if (stackOverride[sp] != BidiClass.ON) _types[i] = stackOverride[sp];
                        break;
                    case BidiClass.PDF:
                        _levels[i] = stackLevel[sp];
                        if (overflowIsolate > 0) { /* ignored */ }
                        else if (overflowEmbedding > 0) overflowEmbedding--;
                        else if (!stackIsolate[sp] && sp > 0) sp--;
                        break;
                    case BidiClass.B:
                        sp = 0;
                        overflowIsolate = overflowEmbedding = validIsolate = 0;
                        _levels[i] = paragraphLevel;
                        break;
                    default:
                        _levels[i] = stackLevel[sp];
                        if (stackOverride[sp] != BidiClass.ON) _types[i] = stackOverride[sp];
                        break;
                }
            }
        }

        private static bool IsRemovedByX9(BidiClass t) => t is BidiClass.LRE or BidiClass.RLE
            or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF or BidiClass.BN;

        // X10
        private List<IsolatingRunSequence> DetermineIsolatingRunSequences(byte paragraphLevel)
        {
            var levelRuns = new List<List<int>>();
            var runOfIndex = new int[_length];
            Array.Fill(runOfIndex, -1);

            List<int>? current = null;
            byte currentLevel = 0;
            for (int i = 0; i < _length; i++)
            {
                if (IsRemovedByX9(InitialTypes[i])) continue;
                if (current == null || _levels[i] != currentLevel)
                {
                    current = [];
                    currentLevel = _levels[i];
                    levelRuns.Add(current);
                }
                current.Add(i);
                runOfIndex[i] = levelRuns.Count - 1;
            }

            var used = new bool[levelRuns.Count];
            var sequences = new List<IsolatingRunSequence>();
            for (int r = 0; r < levelRuns.Count; r++)
            {
                if (used[r]) continue;
                int firstIndex = levelRuns[r][0];
                if (InitialTypes[firstIndex] == BidiClass.PDI && _matchingIsolate[firstIndex] >= 0)
                    continue; // continuation of another sequence

                var indices = new List<int>();
                int run = r;
                while (true)
                {
                    used[run] = true;
                    indices.AddRange(levelRuns[run]);
                    int last = levelRuns[run][^1];
                    var lastType = InitialTypes[last];
                    if (IsIsolateInitiator(lastType) && _matchingPdi[last] < _length)
                    {
                        int next = runOfIndex[_matchingPdi[last]];
                        if (next < 0 || used[next]) break;
                        run = next;
                        continue;
                    }
                    break;
                }
                sequences.Add(new IsolatingRunSequence(this, indices, paragraphLevel));
            }
            return sequences;
        }

        public byte[] GetLevels(byte paragraphLevel)
        {
            // X9: formatting/BN characters take the level of the run they belong to so that
            // reordering stays contiguous.
            byte prev = paragraphLevel;
            for (int i = 0; i < _length; i++)
            {
                if (IsRemovedByX9(InitialTypes[i])) _levels[i] = prev;
                else prev = _levels[i];
            }
            return _levels;
        }

        internal BidiClass[] Types => _types;
        internal byte[] Levels => _levels;
        internal int Length => _length;

        // -----------------------------------------------------------------------------------
        private sealed class IsolatingRunSequence
        {
            private readonly BidiState _owner;
            private readonly int[] _indices;
            private readonly BidiClass[] _types;
            private readonly byte[] _resolvedLevels;
            private readonly byte _level;
            private readonly BidiClass _sos;
            private readonly BidiClass _eos;

            public IsolatingRunSequence(BidiState owner, List<int> indices, byte paragraphLevel)
            {
                _owner = owner;
                _indices = indices.ToArray();
                _types = new BidiClass[_indices.Length];
                for (int i = 0; i < _indices.Length; i++) _types[i] = owner._types[_indices[i]];
                _level = owner._levels[_indices[0]];
                _resolvedLevels = new byte[_indices.Length];
                Array.Fill(_resolvedLevels, _level);

                int first = _indices[0];
                int prev = first - 1;
                while (prev >= 0 && IsRemovedByX9(owner.InitialTypes[prev])) prev--;
                byte prevLevel = prev >= 0 ? owner._levels[prev] : paragraphLevel;
                _sos = TypeForLevel(Math.Max(prevLevel, _level));

                int last = _indices[^1];
                byte succLevel;
                if (IsIsolateInitiator(owner.InitialTypes[last]) && owner._matchingPdi[last] >= owner._length)
                {
                    succLevel = paragraphLevel;
                }
                else
                {
                    int next = last + 1;
                    while (next < owner._length && IsRemovedByX9(owner.InitialTypes[next])) next++;
                    succLevel = next < owner._length ? owner._levels[next] : paragraphLevel;
                }
                _eos = TypeForLevel(Math.Max(succLevel, _level));
            }

            private static BidiClass TypeForLevel(int level) => (level & 1) == 0 ? BidiClass.L : BidiClass.R;

            // W1 - W7
            public void ResolveWeakTypes()
            {
                // W1: NSM takes the type of the previous character (sos at sequence start),
                //     isolate initiators and PDI make it ON.
                var prevType = _sos;
                for (int i = 0; i < _types.Length; i++)
                {
                    var t = _types[i];
                    if (t == BidiClass.NSM)
                        _types[i] = IsIsolateInitiator(prevType) || prevType == BidiClass.PDI ? BidiClass.ON : prevType;
                    prevType = t;
                }

                // W2: EN -> AN when the last strong type is AL.
                var lastStrong = _sos;
                for (int i = 0; i < _types.Length; i++)
                {
                    var t = _types[i];
                    if (t is BidiClass.L or BidiClass.R or BidiClass.AL) lastStrong = t;
                    else if (t == BidiClass.EN && lastStrong == BidiClass.AL) _types[i] = BidiClass.AN;
                }

                // W3: AL -> R.
                for (int i = 0; i < _types.Length; i++)
                    if (_types[i] == BidiClass.AL) _types[i] = BidiClass.R;

                // W4: single ES between EN, or single CS between two numbers of the same type.
                for (int i = 1; i < _types.Length - 1; i++)
                {
                    if (_types[i] is not (BidiClass.ES or BidiClass.CS)) continue;
                    var prev = _types[i - 1];
                    var next = _types[i + 1];
                    if (prev == BidiClass.EN && next == BidiClass.EN) _types[i] = BidiClass.EN;
                    else if (_types[i] == BidiClass.CS && prev == BidiClass.AN && next == BidiClass.AN)
                        _types[i] = BidiClass.AN;
                }

                // W5: a sequence of ET adjacent to EN becomes EN.
                for (int i = 0; i < _types.Length; i++)
                {
                    if (_types[i] != BidiClass.ET) continue;
                    int start = i;
                    int end = i;
                    while (end + 1 < _types.Length && _types[end + 1] == BidiClass.ET) end++;
                    var before = start > 0 ? _types[start - 1] : _sos;
                    var after = end + 1 < _types.Length ? _types[end + 1] : _eos;
                    if (before == BidiClass.EN || after == BidiClass.EN)
                        for (int k = start; k <= end; k++) _types[k] = BidiClass.EN;
                    i = end;
                }

                // W6: remaining separators and terminators become ON.
                for (int i = 0; i < _types.Length; i++)
                    if (_types[i] is BidiClass.ET or BidiClass.ES or BidiClass.CS) _types[i] = BidiClass.ON;

                // W7: EN -> L when the last strong type is L.
                lastStrong = _sos;
                for (int i = 0; i < _types.Length; i++)
                {
                    var t = _types[i];
                    if (t is BidiClass.L or BidiClass.R) lastStrong = t;
                    else if (t == BidiClass.EN && lastStrong == BidiClass.L) _types[i] = BidiClass.L;
                }
            }

            // N0 - N2
            public void ResolveNeutralTypes()
            {
                ResolveBracketPairs();

                for (int i = 0; i < _types.Length; i++)
                {
                    if (!IsNeutralOrIsolate(_types[i])) continue;
                    int start = i;
                    int end = i;
                    while (end + 1 < _types.Length && IsNeutralOrIsolate(_types[end + 1])) end++;

                    var before = start > 0 ? StrongOf(_types[start - 1]) : _sos;
                    var after = end + 1 < _types.Length ? StrongOf(_types[end + 1]) : _eos;

                    BidiClass resolved = before == after ? before : TypeForLevel(_level);
                    for (int k = start; k <= end; k++) _types[k] = resolved;
                    i = end;
                }
            }

            private static BidiClass StrongOf(BidiClass t) => t switch
            {
                BidiClass.L => BidiClass.L,
                BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R,
                _ => t
            };

            private static bool IsNeutralOrIsolate(BidiClass t) => t is BidiClass.B or BidiClass.S
                or BidiClass.WS or BidiClass.ON or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;

            // N0: paired brackets take the embedding direction when it appears inside them.
            private void ResolveBracketPairs()
            {
                var stack = new Stack<(char Bracket, int Position)>();
                var pairs = new List<(int Open, int Close)>();
                for (int i = 0; i < _types.Length; i++)
                {
                    if (_types[i] != BidiClass.ON) continue;
                    char c = CharAt(i);
                    if (BidiMirroring.IsOpeningBracket(c))
                    {
                        if (stack.Count >= 63) { stack.Clear(); break; }
                        stack.Push((BidiMirroring.CanonicalBracket(c), i));
                    }
                    else if (BidiMirroring.IsClosingBracket(c))
                    {
                        char canonical = BidiMirroring.CanonicalBracket(BidiMirroring.Mirror(c));
                        var buffer = new List<(char, int)>();
                        bool found = false;
                        while (stack.Count > 0)
                        {
                            var top = stack.Pop();
                            if (top.Bracket == canonical) { pairs.Add((top.Position, i)); found = true; break; }
                            buffer.Add(top);
                        }
                        if (!found) foreach (var b in Enumerable.Reverse(buffer)) stack.Push(b);
                    }
                }
                pairs.Sort((a, b) => a.Open.CompareTo(b.Open));

                var e = TypeForLevel(_level);
                var o = e == BidiClass.L ? BidiClass.R : BidiClass.L;
                foreach (var (open, close) in pairs)
                {
                    bool foundE = false, foundO = false;
                    for (int i = open + 1; i < close; i++)
                    {
                        var s = StrongOf(_types[i]);
                        if (s == e) { foundE = true; break; }
                        if (s == o) foundO = true;
                    }
                    BidiClass? newType = null;
                    if (foundE) newType = e;
                    else if (foundO)
                    {
                        var prior = _sos;
                        for (int i = open - 1; i >= 0; i--)
                        {
                            var s = StrongOf(_types[i]);
                            if (s is BidiClass.L or BidiClass.R) { prior = s; break; }
                        }
                        newType = prior == o ? o : e;
                    }
                    if (newType is { } nt)
                    {
                        _types[open] = nt;
                        _types[close] = nt;
                        // NSMs following a paired bracket take the bracket's type.
                        for (int i = open + 1; i < _types.Length && _owner.InitialTypes[_indices[i]] == BidiClass.NSM; i++)
                            _types[i] = nt;
                        for (int i = close + 1; i < _types.Length && _owner.InitialTypes[_indices[i]] == BidiClass.NSM; i++)
                            _types[i] = nt;
                    }
                }
            }

            private char CharAt(int seqIndex) => _owner.SourceChar(_indices[seqIndex]);

            // I1 / I2
            public void ResolveImplicitLevels()
            {
                if ((_level & 1) == 0)
                {
                    for (int i = 0; i < _types.Length; i++)
                    {
                        _resolvedLevels[i] = _types[i] switch
                        {
                            BidiClass.R => (byte)(_level + 1),
                            BidiClass.AN or BidiClass.EN => (byte)(_level + 2),
                            _ => _level
                        };
                    }
                }
                else
                {
                    for (int i = 0; i < _types.Length; i++)
                    {
                        _resolvedLevels[i] = _types[i] switch
                        {
                            BidiClass.L or BidiClass.AN or BidiClass.EN => (byte)(_level + 1),
                            _ => _level
                        };
                    }
                }
            }

            public void ApplyLevelsAndTypes()
            {
                for (int i = 0; i < _indices.Length; i++)
                {
                    _owner._types[_indices[i]] = _types[i];
                    _owner._levels[_indices[i]] = _resolvedLevels[i];
                }
            }
        }

        internal char SourceChar(int index) => _source[index];
    }
}
