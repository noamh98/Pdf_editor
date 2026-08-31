namespace PdfEditor.Core.Localization;

/// <summary>
/// Every string the user sees. The application ships in Hebrew only; keeping the text in one
/// place makes it reviewable and keeps grammatical gender and phrasing consistent.
/// </summary>
public static class Strings
{
    // ---- application ------------------------------------------------------------------------
    public const string AppName = "עורך PDF";
    public const string AppTagline = "עריכה מקומית של קובצי PDF, ללא אינטרנט";

    // ---- commands ---------------------------------------------------------------------------
    public const string Open = "פתיחה";
    public const string OpenFile = "פתיחת קובץ";
    public const string Save = "שמירה";
    public const string SaveAs = "שמירה בשם";
    public const string ExportFinalCopy = "ייצוא עותק סופי";
    public const string Print = "הדפסה";
    public const string Close = "סגירה";
    public const string Cancel = "ביטול";
    public const string Confirm = "אישור";
    public const string Undo = "ביטול פעולה";
    public const string Redo = "ביצוע מחדש";
    public const string Copy = "העתקה";
    public const string Paste = "הדבקה";
    public const string Duplicate = "שכפול";
    public const string Delete = "מחיקה";
    public const string SelectAll = "בחירת הכול";
    public const string Search = "חיפוש";
    public const string Settings = "הגדרות";
    public const string Help = "עזרה";
    public const string Merge = "מיזוג קבצים";
    public const string Split = "פיצול וחילוץ";
    public const string Rename = "שינוי שם";
    public const string Add = "הוספה";
    public const string Remove = "הסרה";
    public const string Apply = "החלה";
    public const string Reset = "איפוס";

    // ---- tools ------------------------------------------------------------------------------
    public const string ToolSelect = "בחירה";
    public const string ToolTextBox = "תיבת טקסט";
    public const string ToolRectangle = "מלבן";
    public const string ToolEllipse = "אליפסה";
    public const string ToolLine = "קו";
    public const string ToolArrow = "חץ";
    public const string ToolInk = "ציור חופשי";
    public const string ToolHighlight = "הדגשה";
    public const string ToolCheckMark = "סימן וי";
    public const string ToolCrossMark = "סימן איקס";
    public const string ToolSignature = "חתימה";

    // ---- view -------------------------------------------------------------------------------
    public const string ZoomIn = "הגדלה";
    public const string ZoomOut = "הקטנה";
    public const string FitWidth = "התאמה לרוחב";
    public const string FitPage = "התאמה לעמוד";
    public const string ActualSize = "גודל מקורי";
    public const string RotateView = "סיבוב תצוגה";
    public const string MoreActions = "פעולות נוספות";
    public const string ShortcutsTitle = "קיצורי מקלדת";
    public const string SearchResults = "תוצאות חיפוש";
    public const string SearchNoResults = "לא נמצאו תוצאות";
    public const string SearchNeedsOcr =
        "החיפוש עובד על טקסט שזוהה. הפעילו זיהוי טקסט על העמודים הרלוונטיים ואז חפשו שוב.";
    public const string SearchResultCount = "{0} תוצאות";
    public const string PageLabel = "עמוד {0}";
    public const string Thumbnails = "תצוגה מקדימה של עמודים";
    public const string Properties = "מאפיינים";
    public const string PageOf = "עמוד {0} מתוך {1}";
    public const string GoToPage = "מעבר לעמוד";

    // ---- document state ---------------------------------------------------------------------
    public const string Unsaved = "לא נשמר";
    public const string Saved = "נשמר";
    public const string SavingInProgress = "שומר…";
    public const string Loading = "טוען…";
    public const string Rendering = "מכין תצוגה…";
    public const string ReadOnlyFile = "הקובץ פתוח לקריאה בלבד";
    public const string ProtectedDocument = "המסמך מוגן והרשאות העריכה בו מוגבלות";

    // ---- empty / first run ------------------------------------------------------------------
    public const string EmptyStateTitle = "לא נפתח מסמך";
    public const string EmptyStateBody = "גררו לכאן קובץ PDF, או לחצו על פתיחת קובץ.";
    public const string RecentFiles = "קבצים אחרונים";
    public const string NoRecentFiles = "אין עדיין קבצים אחרונים";

    // ---- saving -----------------------------------------------------------------------------
    public const string ExportWarningTitle = "העותק הסופי אינו ניתן לעריכה חוזרת";
    public const string ExportWarningBody =
        "בעותק הסופי ההערות, הצורות, הציור והחתימות נצרבים לתוך תוכן העמוד. " +
        "לא ניתן יהיה לערוך אותם שוב. הקובץ המקורי לא ישתנה.";
    public const string OverwriteSourceTitle = "דריסת קובץ המקור";
    public const string OverwriteSourceBody = "הקובץ הנבחר הוא קובץ המקור. לדרוס אותו?";
    public const string SaveBeforeClosingTitle = "יש שינויים שלא נשמרו";
    public const string SaveBeforeClosingBody = "לשמור את השינויים לפני הסגירה?";
    public const string DontSave = "אל תשמור";

    // ---- recovery ---------------------------------------------------------------------------
    // ---- the properties panel ----------------------------------------------------------------
    public const string ColorLabel = "צבע";
    public const string LineWidthLabel = "עובי קו";
    public const string OpacityLabel = "שקיפות";
    public const string FontSizeLabel = "גודל גופן";
    public const string BoldLabel = "מודגש";
    public const string AlignmentLabel = "יישור";

    /// <summary>
    /// Alignment is named by where the line starts, not by a side of the page: in Hebrew the start
    /// is the right, in a Latin run it is the left, and the annotation follows its own text.
    /// </summary>
    public const string AlignStart = "התחלה";
    public const string AlignCenter = "מרכז";
    public const string AlignEnd = "סוף";
    public const string AlignStartHint = "יישור לתחילת השורה — בעברית זהו הצד הימני";
    public const string AlignCenterHint = "יישור למרכז";
    public const string AlignEndHint = "יישור לסוף השורה — בעברית זהו הצד השמאלי";

    public const string RecoveryTitle = "נמצאה עבודה שלא נשמרה";
    public const string RecoveryBody =
        "בהפעלה הקודמת נותרו שינויים שלא נשמרו. אפשר לשחזר אותם עכשיו או למחוק אותם.";
    public const string RecoverAction = "שחזור";
    public const string DiscardRecovery = "מחיקת השחזור";
    public const string Recovered = "העבודה שלא נשמרה שוחזרה. יש לשמור את הקובץ כדי לשמר אותה.";
    public const string RecoveryEmpty = "לא נמצאו הערות לשחזור.";
    public const string RecoveryStale =
        "קובץ המקור השתנה מאז השחזור האחרון, לכן ייתכן שההערות לא יתאימו למיקומן המקורי.";

    // ---- printing ---------------------------------------------------------------------------
    public const string PrintPreview = "תצוגה מקדימה להדפסה";
    public const string SeparateSheets = "הדפס כל עמוד תוכן על גיליון נפרד";
    public const string SeparateSheetsHint =
        "מוסיף עמוד ריק בין עמודי תוכן, כדי שמדפסת שכופה הדפסה דו-צדדית תדפיס עמוד אחד לכל גיליון. " +
        "קובץ המקור אינו משתנה.";
    public const string SeparateSheetsLimitation =
        "התוצאה תלויה באופן שבו מנהל ההתקן או שרת ההדפסה מפרשים את רצף העמודים. " +
        "האפליקציה מייצרת את הרצף הנכון, אך אינה יכולה לעקוף מדיניות ארגונית.";
    public const string PrintSummary = "{0} עמודי תוכן, {1} עמודים ריקים, כ-{2} גיליונות";
    public const string Printer = "מדפסת";
    public const string Copies = "עותקים";
    public const string PrintRange = "טווח עמודים";
    public const string PrintAll = "כל העמודים";
    public const string PrintSelection = "עמודים נבחרים";
    public const string AssumeDuplex = "הערכת מספר הגיליונות מניחה הדפסה דו-צדדית";

    // ---- page operations --------------------------------------------------------------------
    public const string PageRangeLabel = "טווח עמודים, לדוגמה 1-3,5,8-10";
    public const string RotateLeft = "סיבוב שמאלה";
    public const string RotateRight = "סיבוב ימינה";
    public const string DeletePages = "מחיקת עמודים";
    public const string ExtractPages = "חילוץ עמודים";
    public const string SplitEveryPage = "פיצול כל עמוד לקובץ נפרד";
    public const string ReorderHint = "גררו עמוד כדי לשנות את סדרו";
    public const string PageOperations = "פעולות על עמודים";
    public const string PageOperationOutputIsNew =
        "הפעולה כותבת קובץ חדש. קובץ המקור אינו משתנה.";
    public const string PageOperationSummary = "{0} עמודים ייכללו בקובץ החדש";
    public const string PageOperationDone = "נוצר קובץ חדש: {0}";
    public const string RotatePages = "סיבוב עמודים";

    // ---- OCR --------------------------------------------------------------------------------
    public const string Ocr = "זיהוי טקסט";
    public const string OcrRunOnPage = "זיהוי טקסט בעמוד הנוכחי";
    public const string OcrRunOnDocument = "זיהוי טקסט במסמך כולו";
    public const string OcrLanguageHebrew = "עברית";
    public const string OcrLanguageEnglish = "אנגלית";
    public const string OcrLanguageBoth = "עברית ואנגלית";
    public const string OcrInProgress = "מזהה טקסט… עמוד {0} מתוך {1}";
    public const string OcrNoResults = "לא זוהה טקסט בעמוד זה";
    public const string OcrAccuracyNotice =
        "זיהוי הטקסט מתבצע במחשב זה בלבד. הדיוק תלוי באיכות הסריקה ואינו מובטח.";
    public const string OcrNotAvailable = "רכיבי זיהוי הטקסט אינם זמינים בהתקנה זו";
    public const string ClearOcrCache = "נקה מטמון זיהוי טקסט";

    // ---- signatures -------------------------------------------------------------------------
    public const string Signatures = "חתימות";
    public const string SignatureLibrary = "ספריית חתימות";
    public const string DrawSignature = "ציור חתימה";
    public const string ImportSignature = "ייבוא תמונת חתימה";
    public const string RememberSignature = "זכור את החתימה במחשב זה";
    public const string SignatureName = "שם החתימה";
    public const string SignatureDisclaimer =
        "זוהי חתימה גרפית ואינה חתימה דיגיטלית מאומתת.";
    public const string SignatureStorageNotice =
        "החתימות נשמרות במחשב זה בלבד, תחת חשבון המשתמש שלכם, ואינן נשלחות לשום מקום.";
    public const string DeleteSignatureConfirm = "למחוק את החתימה לצמיתות?";
    public const string DeleteAllSignatures = "מחיקת כל החתימות";

    // ---- privacy / settings -----------------------------------------------------------------
    public const string PrivacyTitle = "פרטיות";
    public const string PrivacyBody =
        "האפליקציה פועלת אופליין. המסמכים, החתימות ותוצאות זיהוי הטקסט נשארים במחשב זה ואינם נשלחים לשום שירות.";
    public const string ClearRecentFiles = "נקה רשימת קבצים אחרונים";
    public const string ClearRecoveryFiles = "נקה קובצי שחזור";
    public const string ClearTempFiles = "נקה קבצים זמניים";
    public const string ThemeSystem = "לפי מערכת ההפעלה";
    public const string ThemeLight = "בהיר";
    public const string ThemeDark = "כהה";
    public const string ThemeLabel = "ערכת נושא";
    public const string ReducedMotionLabel = "הפחתת אנימציות";

    // ---- errors -----------------------------------------------------------------------------
    public const string ErrorTitle = "אירעה שגיאה";
    public const string ErrorFileNotFound = "הקובץ לא נמצא. ייתכן שהוא הועבר או נמחק.";
    public const string ErrorAccessDenied = "אין הרשאה לפתוח את הקובץ.";
    public const string ErrorNotAPdf = "הקובץ אינו קובץ PDF תקין.";
    public const string ErrorCorrupted = "הקובץ פגום ולא ניתן לקרוא אותו.";
    public const string ErrorPasswordRequired = "הקובץ מוגן בסיסמה. גרסה זו אינה תומכת בפתיחת קבצים מוצפנים.";
    public const string ErrorUnsupportedEncryption = "סוג ההצפנה של הקובץ אינו נתמך.";
    public const string ErrorUnknown = "לא ניתן היה להשלים את הפעולה.";
    public const string ErrorDiskFull = "אין מספיק מקום פנוי בדיסק כדי לשמור את הקובץ.";
    public const string ErrorTargetReadOnly = "לא ניתן לכתוב לקובץ היעד. ייתכן שהוא מסומן לקריאה בלבד או פתוח בתוכנה אחרת.";
    public const string OperationCancelled = "הפעולה בוטלה";

    // ---- page range errors ------------------------------------------------------------------
    public const string RangeErrorEmpty = "יש להזין טווח עמודים.";
    public const string RangeErrorInvalidCharacter = "הטווח מכיל תווים שאינם ספרות, מקף או פסיק: {0}";
    public const string RangeErrorMalformed = "הטווח אינו תקין: {0}";
    public const string RangeErrorNotANumber = "לא ניתן לקרוא מספר עמוד מתוך: {0}";
    public const string RangeErrorZeroOrNegative = "מספר עמוד חייב להיות 1 או יותר: {0}";
    public const string RangeErrorReversed = "בטווח, מספר העמוד הראשון חייב להיות קטן או שווה לאחרון: {0}";
    public const string RangeErrorOutOfBounds = "הטווח חורג ממספר העמודים במסמך: {0}";

    // ---- accessibility ----------------------------------------------------------------------
    public const string A11yPageCanvas = "אזור המסמך";
    public const string A11yThumbnailList = "רשימת עמודים";
    public const string A11yToolbar = "סרגל כלים";
    public const string A11yPropertiesPanel = "לוח מאפיינים";
    public const string A11yStatusBar = "שורת מצב";
}
