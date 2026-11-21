using System.Globalization;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.History;

/// <summary>
/// Names, metadata and text of the simulated documents, derived from their index so nothing has to be stored. The
/// Arabic wording follows the sample documents; references are unique.
/// </summary>
internal static class HistoryText
{
    public const string IdPrefix = "DOC-";

    public const int IdDigits = 7;

    public const int FirstYear = 2008;

    public const int Years = 17;

    private const string ArabicDigits = "٠١٢٣٤٥٦٧٨٩";

    private static readonly KindText[] Kinds =
    [
        // PaymentVoucher
        new("سند صرف",
            ["توريد أجهزة حاسوب", "صيانة المكيفات", "مستلزمات مكتبية", "أتعاب استشارية", "تذاكر سفر", "إيجار مبنى", "خدمات النظافة", "اشتراك الإنترنت"],
            ["دفعة", "مورد", "فاتورة", "سداد", "مالية"]),
        // PurchaseOrder
        new("أمر شراء",
            ["أجهزة حاسوب ومستلزمات", "أثاث مكتبي", "قطع غيار", "أدوات سلامة", "طابعات وأحبار", "معدات شبكات"],
            ["أمر شراء", "توريد", "مناقصة", "عرض سعر"]),
        // Contract
        new("عقد",
            ["صيانة أجهزة الحاسوب", "خدمات النظافة", "تأجير مبنى", "توريد مواد", "خدمات أمنية", "استشارات قانونية"],
            ["عقد", "صيانة", "تجديد", "ضمان", "اتفاقية"]),
        // Correspondence
        new("كتاب",
            ["طلب معلومات", "تمديد مهلة", "اجتماع اللجنة", "تحديث البيانات", "شكوى مراجع", "تعميم داخلي"],
            ["مراسلة", "كتاب", "تعميم", "رد"]),
        // Decision
        new("قرار إداري",
            ["تعيين موظف", "ترقية موظف", "تشكيل لجنة", "نقل موظف", "منح إجازة", "اعتماد سياسة"],
            ["قرار", "تعيين", "ترقية", "لجنة", "موظف"]),
    ];

    private static readonly string[] Offices = ["الإدارة المالية", "إدارة الموارد البشرية", "الإدارة القانونية", "إدارة المشتريات", "الشؤون الإدارية"];

    public static string Id(int index) => IdPrefix + (index + 1).ToString(new string('0', IdDigits), CultureInfo.InvariantCulture);

    /// <summary>The index of a history id (<c>DOC-0001234</c>); false for any other id.</summary>
    public static bool TryIndex(string? id, out int index)
    {
        index = -1;
        if (id is null || id.Length != IdPrefix.Length + IdDigits || !id.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!int.TryParse(id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            return false;
        index = number - 1;
        return true;
    }

    public static int Year(int index) => FirstYear + (int)(Shuffled(index) % Years);

    public static int Sequence(int index) => (int)(Shuffled(index) / Years) + 1;

    /// <summary>
    /// The index shuffled one-to-one over 2^21 values (an odd multiplier is a bijection modulo a power of two), so
    /// documents scanned one after the other get unrelated years and numbers that are still never the same.
    /// </summary>
    private static uint Shuffled(int index) => unchecked(((uint)index * 0x9E3779B1u) + 0x7F4A7C15u) & 0x1F_FFFF;

    public static DateOnly DocumentDate(int index) => new DateOnly(Year(index), 1, 1).AddDays(HistoryRandom.Below(index, 31, 365));

    /// <summary>The reference prefix: by department, and by type where a department sends two kinds of finance papers.</summary>
    public static string Code(DocumentDepartment department, DocumentKind kind) => (department, kind) switch
    {
        (DocumentDepartment.Finance, DocumentKind.PaymentVoucher) => "PV",
        (_, DocumentKind.PurchaseOrder) => "PO",
        (_, DocumentKind.Contract) => "CT",
        (DocumentDepartment.HumanResources, _) => "HR",
        (DocumentDepartment.Legal, _) => "LG",
        _ => "ADM",
    };

    public static string Reference(int index, DocumentDepartment department, DocumentKind kind)
    {
        Span<char> buffer = stackalloc char[24];
        return new string(buffer[..WriteReference(buffer, index, department, kind)]);
    }

    public static string FileName(int index, DocumentDepartment department, DocumentKind kind) => Reference(index, department, kind) + ".pdf";

    public static string Title(int index, DocumentKind kind)
    {
        Span<char> buffer = stackalloc char[160];
        return new string(buffer[..WriteTitle(buffer, index, kind)]);
    }

    public static IReadOnlyList<string> Keywords(int index, DocumentKind kind)
    {
        var words = Kinds[(int)kind].Keywords;
        var (first, second) = KeywordIndexes(index, words.Length);
        return [words[first], words[second]];
    }

    public static DocumentMetadata Metadata(int index, in HistoryEntry entry) =>
        new(Title(index, entry.Kind), Reference(index, entry.Department, entry.Kind), DocumentDate(index), entry.Department, entry.Kind,
            entry.Confidentiality, Keywords(index, entry.Kind));

    /// <summary>The reviewed text of each page; the page without text (if any) is empty.</summary>
    public static List<string> PageTexts(int index, in HistoryEntry entry, DocumentMetadata metadata)
    {
        var kind = Kinds[(int)entry.Kind];
        var subject = kind.Subjects[SubjectIndex(index, kind.Subjects.Length)];
        var number = Arabic(Sequence(index)) + "/" + Arabic(Year(index));
        var date = metadata.DocumentDate is { } day
            ? Arabic(day.Day, 2) + "/" + Arabic(day.Month, 2) + "/" + Arabic(day.Year)
            : string.Empty;
        var pages = new List<string>(entry.Pages);
        for (var page = 1; page <= entry.Pages; page++)
        {
            if (page == entry.EmptyPage)
            {
                pages.Add(string.Empty);
                continue;
            }

            pages.Add(page == 1
                ? string.Join('\n', kind.Header, $"الرقم: {number} — التاريخ: {date}", metadata.Title, Body(index, entry.Kind, subject), Offices[(int)entry.Department])
                : string.Join('\n', $"تابع {kind.Header} رقم {number} — الصفحة {Arabic(page)}", Continuation(index, page)));
        }

        return pages;
    }

    /// <summary>The text as OCR read it before a reviewer corrected it: a few letters misread on the first page.</summary>
    public static List<string> OcrTexts(IReadOnlyList<string> reviewed)
    {
        var pages = reviewed.ToList();
        if (pages.Count > 0)
            pages[0] = pages[0].Replace('أ', 'ا').Replace('ة', 'ه');
        return pages;
    }

    public static int WriteId(Span<char> buffer, int index)
    {
        IdPrefix.AsSpan().CopyTo(buffer);
        (index + 1).TryFormat(buffer[IdPrefix.Length..], out var written, "0000000", CultureInfo.InvariantCulture);
        return IdPrefix.Length + written;
    }

    public static int WriteReference(Span<char> buffer, int index, DocumentDepartment department, DocumentKind kind)
    {
        var code = Code(department, kind);
        code.AsSpan().CopyTo(buffer);
        var length = code.Length;
        buffer[length++] = '-';
        Year(index).TryFormat(buffer[length..], out var written, provider: CultureInfo.InvariantCulture);
        length += written;
        buffer[length++] = '-';
        Sequence(index).TryFormat(buffer[length..], out written, "00000", CultureInfo.InvariantCulture);
        return length + written;
    }

    /// <summary>Writes the title into <paramref name="buffer"/> (used by the search, so nothing is allocated per document).</summary>
    public static int WriteTitle(Span<char> buffer, int index, DocumentKind kind)
    {
        var text = Kinds[(int)kind];
        var subject = text.Subjects[SubjectIndex(index, text.Subjects.Length)];
        var writer = new SpanWriter(buffer);
        switch (kind)
        {
            case DocumentKind.PaymentVoucher:
                writer.Append("سند صرف رقم ");
                writer.AppendArabic(Year(index));
                writer.Append('/');
                writer.AppendArabic(Sequence(index));
                writer.Append(" — ");
                writer.Append(subject);
                break;
            case DocumentKind.PurchaseOrder:
                writer.Append("أمر شراء رقم ");
                writer.AppendArabic(Sequence(index));
                writer.Append(" — ");
                writer.Append(subject);
                break;
            case DocumentKind.Contract:
                writer.Append("عقد ");
                writer.Append(subject);
                writer.Append(" لعام ");
                writer.AppendArabic(Year(index));
                break;
            case DocumentKind.Correspondence:
                writer.Append("كتاب رقم ");
                writer.AppendArabic(Sequence(index));
                writer.Append('/');
                writer.AppendArabic(Year(index));
                writer.Append(" بشأن ");
                writer.Append(subject);
                break;
            default:
                writer.Append("قرار إداري رقم ");
                writer.AppendArabic(Sequence(index));
                writer.Append(" لسنة ");
                writer.AppendArabic(Year(index));
                writer.Append(" بشأن ");
                writer.Append(subject);
                break;
        }

        return writer.Length;
    }

    /// <summary>For the keyword search: which words of a document type contain the term.</summary>
    public static bool[][] KeywordMatches(string term) =>
        Kinds.Select(k => k.Keywords.Select(w => w.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray()).ToArray();

    public static bool HasKeyword(int index, DocumentKind kind, bool[][] matches)
    {
        var words = matches[(int)kind];
        var (first, second) = KeywordIndexes(index, words.Length);
        return words[first] || words[second];
    }

    private static string Body(int index, DocumentKind kind, string subject) => kind switch
    {
        DocumentKind.PaymentVoucher =>
            $"يصرف مبلغ وقدره {Arabic(1_000 + (HistoryRandom.Below(index, 41, 240) * 250))} درهم فقط لا غير وذلك مقابل {subject}.",
        DocumentKind.PurchaseOrder => $"يرجى توريد {subject} حسب الشروط والمواصفات المرفقة، على أن يتم التسليم خلال ثلاثين يومًا.",
        DocumentKind.Contract => $"اتفق الطرفان على {subject} لمدة {Arabic(6 + HistoryRandom.Below(index, 42, 31))} شهرًا اعتبارًا من تاريخ التوقيع.",
        DocumentKind.Correspondence => $"نود إفادتكم بخصوص {subject}، ونرجو التكرم بالاطلاع واتخاذ ما يلزم.",
        _ => $"بعد الاطلاع على اللوائح المعمول بها، وبناءً على ما تقتضيه مصلحة العمل، تقرر {subject} اعتبارًا من تاريخه.",
    };

    private static string Continuation(int index, int page) => ((index + page) % 3) switch
    {
        0 => "البنود والشروط التفصيلية كما وردت في المرفقات، ويعد هذا الملحق جزءًا لا يتجزأ من المستند.",
        1 => "جدول الكميات والأسعار المعتمدة، مع توقيع المسؤول المختص وختم الإدارة.",
        _ => "الملاحظات والتوصيات، وقائمة المستندات المرفقة بالملف.",
    };

    private static int SubjectIndex(int index, int count) => HistoryRandom.Below(index, 32, count);

    private static (int First, int Second) KeywordIndexes(int index, int count)
    {
        var first = HistoryRandom.Below(index, 33, count);
        var second = (first + 1 + HistoryRandom.Below(index, 34, count - 1)) % count;
        return (first, second);
    }

    private static string Arabic(int value, int minDigits = 1)
    {
        Span<char> buffer = stackalloc char[12];
        var writer = new SpanWriter(buffer);
        writer.AppendArabic(value, minDigits);
        return new string(buffer[..writer.Length]);
    }

    private sealed record KindText(string Header, string[] Subjects, string[] Keywords);

    /// <summary>Writes into a stack buffer. A struct, so its calls are not chained (each would work on a copy).</summary>
    private ref struct SpanWriter(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;

        public int Length { get; private set; }

        public void Append(string value)
        {
            value.AsSpan().CopyTo(_buffer[Length..]);
            Length += value.Length;
        }

        public void Append(char value) => _buffer[Length++] = value;

        public void AppendArabic(int value, int minDigits = 1)
        {
            Span<char> digits = stackalloc char[11];
            value.TryFormat(digits, out var written, provider: CultureInfo.InvariantCulture);
            for (var pad = written; pad < minDigits; pad++)
                _buffer[Length++] = ArabicDigits[0];
            foreach (var digit in digits[..written])
                _buffer[Length++] = ArabicDigits[digit - '0'];
        }
    }
}
