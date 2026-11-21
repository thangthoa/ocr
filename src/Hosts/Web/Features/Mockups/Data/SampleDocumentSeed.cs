using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>Fictional documents shown next to real OCR jobs for demos. Names, numbers and amounts are made up.</summary>
internal static class SampleDocumentSeed
{
    private const string OcrService = "ocr-service";
    private const string Reviewer = "review01";

    public static IReadOnlyList<DocumentRecord> Create()
    {
        var pv413 = Meta("سند صرف رقم ٢٠١٩/٤١٣ — توريد أجهزة حاسوب", "2019/413", new(2019, 3, 12),
            DocumentDepartment.Finance, DocumentKind.PaymentVoucher, DocumentConfidentiality.Confidential, "أجهزة حاسوب", "مورد", "دفعة");
        var pv413Text = "سند صرف\nرقم السند: ٢٠١٩/٤١٣ — التاريخ: ١٢/٠٣/٢٠١٩\nيصرف للسيد: أحمد محمد علي\nمبلغ وقدره: ٤٥٬٠٠٠ درهم فقط لا غير\nوذلك مقابل: توريد أجهزة حاسوب لقسم المالية حسب أمر الشراء رقم ٣٨٨";
        var pv413Ocr = pv413Text.Replace("٢٠١٩/٤١٣", "٢٠١٩/٤١٢", StringComparison.Ordinal).Replace("أجهزة", "اجهزه", StringComparison.Ordinal);

        var hr187 = Meta("قرار إداري بشأن تعيين موظف", "HR/2016/187", new(2016, 9, 14),
            DocumentDepartment.HumanResources, DocumentKind.Decision, DocumentConfidentiality.Confidential, "تعيين", "موظف");
        var hr187Text = "قرار إداري رقم ١٨٧ لسنة ٢٠١٦\nبشأن تعيين موظف\nبعد الاطلاع على اللوائح المعمول بها، وبناءً على ما تقتضيه مصلحة العمل،\nتقرر تعيين السيد/ خالد عبدالله في وظيفة محاسب اعتبارًا من تاريخه.";

        return
        [
            Doc("DOC-2026-000101", "PV-2019-0413.pdf", 4, 2_400_000, Uploaded(12, 9, 10), "capture01", 97.2, DocumentStatus.Archived, pv413, pv413Text,
                Ocr(Uploaded(12, 9, 14), pv413Ocr, pv413 with { Reference = "2019/412" }),
                Edit("1.1", Uploaded(12, 11, 5), Reviewer, "Corrected the voucher number and two words.", pv413Text, pv413)),

            Doc("DOC-2026-000102", "PO-2019-0388.pdf", 6, 3_100_000, Uploaded(12, 9, 40), "capture01", 95.8, DocumentStatus.Archived,
                Meta("أمر شراء رقم ٣٨٨ — أجهزة حاسوب ومستلزمات", "PO-2019-0388", new(2019, 2, 3),
                    DocumentDepartment.Procurement, DocumentKind.PurchaseOrder, DocumentConfidentiality.Internal, "أجهزة حاسوب", "أمر شراء"),
                "أمر شراء\nرقم الأمر: ٣٨٨ — التاريخ: ٠٣/٠٢/٢٠١٩\nإلى: شركة المورد للتقنية\nيرجى توريد أجهزة حاسوب محمولة عدد ٢٠ جهازًا مع الضمان لمدة ثلاث سنوات\nمكان التسليم: مبنى الإدارة المالية"),

            Doc("DOC-2026-000103", "CT-2020-0017.pdf", 14, 9_800_000, Uploaded(18, 10, 2), "capture02", 82.4, DocumentStatus.NeedsReview,
                Meta("عقد صيانة أجهزة الحاسوب لعام ٢٠٢٠", "CT-2020-0017", new(2020, 1, 5),
                    DocumentDepartment.Legal, DocumentKind.Contract, DocumentConfidentiality.Confidential, "صيانة", "عقد سنوي"),
                "عقد صيانة\nإنه في يوم الأحد الموافق ٠٥/٠١/٢٠٢٠ تم الاتفاق بين كل من:\nالطرف الأول: الإدارة العامة\nالطرف الثاني: شركة الخدمات التقنية\nيلتزم الطرف الثاني بصيانة أجهزة الحاسوب وتوريد قطع الغيار عند الحاجة"),

            Doc("DOC-2026-000104", "HR-2016-0187.pdf", 3, 1_700_000, Uploaded(13, 8, 30), "capture02", 93.1, DocumentStatus.Archived, hr187, hr187Text,
                Ocr(Uploaded(13, 8, 33), hr187Text.Replace("خالد", "خاند", StringComparison.Ordinal), hr187),
                Edit("1.1", Uploaded(13, 10, 12), Reviewer, "Fixed the employee name.", hr187Text, hr187)),

            Doc("DOC-2026-000105", "HR-2016-0190.pdf", 2, 900_000, Uploaded(19, 7, 55), "capture02", 71.5, DocumentStatus.NeedsReview,
                Meta("خطاب طلب إجازة سنوية", "HR/2016/190", new(2016, 10, 2),
                    DocumentDepartment.HumanResources, DocumentKind.Correspondence, DocumentConfidentiality.Internal, "إجازة"),
                "السيد/ مدير إدارة الموارد البشرية المحترم\nتحية طيبة وبعد،\nأرجو التكرم بالموافقة على منحي إجازة سنوية لمدة أسبوعين اعتبارًا من ١٥/١٠/٢٠١٦.\nوتفضلوا بقبول فائق الاحترام."),

            Doc("DOC-2026-000106", "LG-2011-0032.pdf", 18, 12_600_000, Uploaded(14, 11, 20), "capture01", 96.0, DocumentStatus.Archived,
                Meta("عقد إيجار مبنى إداري", "LG-2011-0032", new(2011, 6, 20),
                    DocumentDepartment.Legal, DocumentKind.Contract, DocumentConfidentiality.Secret, "إيجار", "مبنى"),
                "عقد إيجار\nالمؤجر: شركة العقارات المتحدة\nالمستأجر: الإدارة العامة\nالعين المؤجرة: مبنى إداري من ثلاثة طوابق\nمدة العقد: خمس سنوات تبدأ من ٠١/٠٧/٢٠١١"),

            Doc("DOC-2026-000107", "PV-2019-0415.pdf", 3, 2_000_000, Uploaded(19, 8, 5), "capture01", null, DocumentStatus.Failed,
                Meta("سند صرف رقم ٢٠١٩/٤١٥ — مستلزمات مكتبية", "2019/415", new(2019, 3, 14),
                    DocumentDepartment.Finance, DocumentKind.PaymentVoucher, DocumentConfidentiality.Internal),
                string.Empty),

            Doc("DOC-2026-000108", "ADM-2021-0102.pdf", 2, 1_100_000, Uploaded(19, 9, 48), "capture01", null, DocumentStatus.Processing,
                Meta("مذكرة داخلية بشأن تحديث أجهزة الحاسوب", "ADM/2021/102", new(2021, 4, 11),
                    DocumentDepartment.Administration, DocumentKind.Correspondence, DocumentConfidentiality.Internal),
                string.Empty),

            Doc("DOC-2026-000109", "PV-2020-0021.pdf", 5, 2_900_000, Uploaded(15, 9, 0), "capture02", 98.1, DocumentStatus.Archived,
                Meta("سند صرف رقم ٢٠٢٠/٠٢١ — رسوم تدريب", "2020/021", new(2020, 1, 22),
                    DocumentDepartment.Finance, DocumentKind.PaymentVoucher, DocumentConfidentiality.Internal, "تدريب", "دفعة"),
                "سند صرف\nرقم السند: ٢٠٢٠/٠٢١ — التاريخ: ٢٢/٠١/٢٠٢٠\nيصرف لمعهد التدريب المهني\nمبلغ وقدره: ١٢٬٥٠٠ درهم\nوذلك مقابل: رسوم دورة تدريبية لموظفي قسم المالية"),

            Doc("DOC-2026-000110", "PO-2020-0102.pdf", 4, 2_200_000, Uploaded(18, 14, 30), "capture02", 79.9, DocumentStatus.NeedsReview,
                Meta("أمر شراء رقم ١٠٢ — أثاث مكتبي", "PO-2020-0102", new(2020, 5, 18),
                    DocumentDepartment.Procurement, DocumentKind.PurchaseOrder, DocumentConfidentiality.Internal, "أثاث", "أمر شراء"),
                "أمر شراء\nرقم الأمر: ١٠٢ — التاريخ: ١٨/٠٥/٢٠٢٠\nيرجى توريد أثاث مكتبي يشمل ٣٠ مكتبًا و٣٠ كرسيًا\nحسب المواصفات المرفقة"),

            Doc("DOC-2026-000111", "HR-2018-0044.pdf", 7, 4_300_000, Uploaded(16, 10, 45), "capture01", 94.6, DocumentStatus.Archived,
                Meta("قرار إداري بشأن ترقية موظفين", "HR/2018/44", new(2018, 2, 27),
                    DocumentDepartment.HumanResources, DocumentKind.Decision, DocumentConfidentiality.Confidential, "ترقية", "موظف"),
                "قرار إداري رقم ٤٤ لسنة ٢٠١٨\nبشأن ترقية عدد من الموظفين\nتقرر ترقية الموظفين الواردة أسماؤهم في الكشف المرفق إلى الدرجة التالية."),

            Doc("DOC-2026-000112", "ADM-2022-0310.pdf", 1, 600_000, Uploaded(17, 12, 10), "capture02", 99.0, DocumentStatus.Archived,
                Meta("تعميم بشأن ساعات العمل", "ADM/2022/310", new(2022, 11, 8),
                    DocumentDepartment.Administration, DocumentKind.Correspondence, DocumentConfidentiality.Internal, "ساعات العمل", "تعميم"),
                "تعميم\nيُعمم على جميع الموظفين بأن ساعات العمل الرسمية تبدأ من الساعة ٧:٣٠ صباحًا وحتى ٣:٣٠ مساءً اعتبارًا من الأسبوع القادم."),

            Doc("DOC-2026-000113", "CT-2023-0009.pdf", 22, 15_400_000, Uploaded(19, 10, 5), "capture01", null, DocumentStatus.Processing,
                Meta("عقد توريد خدمات النظافة", "CT-2023-0009", new(2023, 3, 1),
                    DocumentDepartment.Legal, DocumentKind.Contract, DocumentConfidentiality.Internal),
                string.Empty),

            Doc("DOC-2026-000114", "PV-2024-0150.pdf", 2, 1_200_000, Uploaded(19, 9, 20), "capture02", 76.3, DocumentStatus.NeedsReview,
                Meta("سند صرف رقم ٢٠٢٤/١٥٠ — صيانة مكيفات", "2024/150", new(2024, 7, 9),
                    DocumentDepartment.Finance, DocumentKind.PaymentVoucher, DocumentConfidentiality.Internal, "صيانة", "مكيفات"),
                "سند صرف\nرقم السند: ٢٠٢٤/١٥٠ — التاريخ: ٠٩/٠٧/٢٠٢٤\nيصرف لشركة التبريد الحديثة\nمبلغ وقدره: ٨٬٧٥٠ درهم\nوذلك مقابل: صيانة مكيفات مبنى الإدارة"),
        ];
    }

    private static DateTime Uploaded(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static DocumentMetadata Meta(string title, string reference, DateOnly date, DocumentDepartment department,
        DocumentKind kind, DocumentConfidentiality confidentiality, params string[] keywords) =>
        new(title, reference, date, department, kind, confidentiality, keywords);

    private static DocumentVersion Ocr(DateTime at, string text, DocumentMetadata metadata) =>
        new("1.0", at, OcrService, DocumentVersionChange.OcrResult, null, null, PageTexts(text), metadata);

    private static DocumentVersion Edit(string number, DateTime at, string author, string note, string text, DocumentMetadata metadata) =>
        new(number, at, author, DocumentVersionChange.Edited, null, note, PageTexts(text), metadata);

    /// <summary>Samples only have text on their first page.</summary>
    private static List<string> PageTexts(string text) => text.Length == 0 ? [] : [text];

    /// <summary>
    /// Builds a document. Without explicit versions, a document that has text gets a single
    /// OCR version a few minutes after upload; archived ones also get a review edit.
    /// </summary>
    private static DocumentRecord Doc(string id, string fileName, int pages, long size, DateTime uploaded, string uploadedBy,
        double? confidence, DocumentStatus status, DocumentMetadata metadata, string text, params DocumentVersion[] versions)
    {
        if (versions.Length == 0 && text.Length > 0)
        {
            var ocr = Ocr(uploaded.AddMinutes(4), text, metadata);
            versions = status == DocumentStatus.Archived
                ? [ocr, Edit("1.1", uploaded.AddHours(2), Reviewer, "Checked metadata.", text, metadata)]
                : [ocr];
        }

        // Archived samples were approved by the reviewer just after the last review edit.
        var approval = status == DocumentStatus.Archived && versions.Length > 0
            ? new DocumentApproval(Reviewer, versions[^1].CreatedAtUtc.AddMinutes(10))
            : null;
        var pageConfidences = confidence is { } value ? SimulatedConfidence.SpreadOverPages(id, pages, value) : null;
        return new DocumentRecord(id, IsSample: true, fileName, pages, size, uploaded, uploadedBy, confidence, status, metadata,
            PageTexts(text), versions, Approval: approval, PageConfidences: pageConfidences,
            Quality: Check(confidence, pageConfidences, versions));
    }

    /// <summary>
    /// The automatic check as it ran when OCR finished, with the default settings and the OCR metadata.
    /// Samples only store text for page 1, so every page counts as having text.
    /// </summary>
    private static QualityCheckResult? Check(double? confidence, IReadOnlyList<double?>? pageConfidences, DocumentVersion[] versions)
    {
        if (confidence is null || pageConfidences is null || versions.Length == 0)
            return null;

        var pages = pageConfidences.Select((value, index) => new PageQuality(index + 1, value, HasText: true, Warnings: 0)).ToList();
        var ocr = versions[0];
        return QualityCheck.Run(pages, confidence, ocr.Metadata, QualitySettings.Default, ocr.CreatedAtUtc);
    }
}
