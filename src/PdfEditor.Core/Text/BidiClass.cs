namespace PdfEditor.Core.Text;

/// <summary>
/// Bidirectional character types defined by Unicode Annex #9 (UAX#9), Table 4.
/// </summary>
public enum BidiClass
{
    // Strong
    L,   // Left-to-Right
    R,   // Right-to-Left
    AL,  // Right-to-Left Arabic

    // Weak
    EN,  // European Number
    ES,  // European Number Separator
    ET,  // European Number Terminator
    AN,  // Arabic Number
    CS,  // Common Number Separator
    NSM, // Nonspacing Mark
    BN,  // Boundary Neutral

    // Neutral
    B,   // Paragraph Separator
    S,   // Segment Separator
    WS,  // Whitespace
    ON,  // Other Neutral

    // Explicit formatting
    LRE, // Left-to-Right Embedding
    RLE, // Right-to-Left Embedding
    LRO, // Left-to-Right Override
    RLO, // Right-to-Left Override
    PDF, // Pop Directional Format
    LRI, // Left-to-Right Isolate
    RLI, // Right-to-Left Isolate
    FSI, // First Strong Isolate
    PDI  // Pop Directional Isolate
}
