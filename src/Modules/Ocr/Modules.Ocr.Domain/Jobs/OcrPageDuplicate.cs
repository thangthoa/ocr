namespace Modules.Ocr.Domain.Jobs;

/// <summary>Why the OCR service thinks two pages of a job are the same page (API doc §4.4).</summary>
public enum OcrDuplicateKind
{
    /// <summary>The two files are byte-for-byte the same. Known when the job is submitted.</summary>
    Identical = 1,

    /// <summary>The same page photographed or scanned twice (image fingerprint). Known when the job is submitted.</summary>
    Similar = 2,

    /// <summary>At least 95 % of the recognised text is the same (pages of 20 words or more). Known once the job is done.</summary>
    SameText = 3,
}

/// <summary>
/// Two pages of a job that look like the same page. The service only warns; removing one is the user's choice,
/// and the later page is the one usually removed.
/// </summary>
public sealed record OcrPageDuplicate
{
    public OcrPageDuplicate(int firstPage, int secondPage, OcrDuplicateKind kind)
    {
        if (firstPage < 1 || secondPage < 1 || firstPage == secondPage)
            throw new ArgumentOutOfRangeException(nameof(secondPage), "A duplicate pairs two different pages, numbered from 1.");

        FirstPage = Math.Min(firstPage, secondPage);
        SecondPage = Math.Max(firstPage, secondPage);
        Kind = kind;
    }

    public int FirstPage { get; }

    /// <summary>The later page of the pair.</summary>
    public int SecondPage { get; }

    public OcrDuplicateKind Kind { get; }
}
