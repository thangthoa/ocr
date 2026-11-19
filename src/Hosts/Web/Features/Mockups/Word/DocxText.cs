using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OppOcr.Web.Features.Mockups.Word;

/// <summary>
/// Minimal DOCX reading and writing without an Office library: enough to start an edit from plain
/// text and to read the text back (for search) after an edit. Pages are separated by page breaks.
/// </summary>
internal static class DocxText
{
    private const long MaxDocumentXmlBytes = 64L * 1024 * 1024;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string ContentTypes =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""";

    private const string Relationships =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""";

    /// <summary>One paragraph per line, a page break between pages; Arabic text is laid out right to left.</summary>
    public static byte[] Create(IReadOnlyList<string> pageTexts)
    {
        ArgumentNullException.ThrowIfNull(pageTexts);
        var body = new XElement(W + "body");
        for (var page = 0; page < pageTexts.Count; page++)
        {
            if (page > 0)
                body.Add(new XElement(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page")))));
            foreach (var line in pageTexts[page].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                body.Add(Paragraph(line));
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body));

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypes);
            WriteEntry(zip, "_rels/.rels", Relationships);
            var entry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
            using var stream = entry.Open();
            document.Save(stream);
        }

        return buffer.ToArray();
    }

    /// <summary>Text of the main document part, one entry per page (page breaks split pages).</summary>
    public static IReadOnlyList<string> ExtractPages(Stream docx)
    {
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("The file is not a Word document.");
        if (entry.Length > MaxDocumentXmlBytes)
            throw new InvalidDataException("The Word document is too large to read.");

        // No DTDs and no external resolution: the file comes from outside this app.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        var root = XDocument.Load(reader).Root?.Element(W + "body");

        var pages = new List<StringBuilder> { new() };
        if (root is not null)
            Walk(root, pages);

        // A page break sits in its own paragraph, so a page would otherwise start with an empty line.
        var result = pages.Select(p => p.ToString().Trim('\n').TrimEnd()).ToList();
        while (result.Count > 1 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void Walk(XElement element, List<StringBuilder> pages)
    {
        if (element.Name == W + "t")
        {
            pages[^1].Append(element.Value);
            return;
        }

        if (element.Name == W + "tab")
        {
            pages[^1].Append('\t');
            return;
        }

        if (element.Name == W + "br")
        {
            if ((string?)element.Attribute(W + "type") == "page")
                pages.Add(new StringBuilder());
            else
                pages[^1].Append('\n');
            return;
        }

        if (element.Name == W + "p" && element.Element(W + "pPr")?.Element(W + "pageBreakBefore") is not null && pages[^1].Length > 0)
            pages.Add(new StringBuilder());

        foreach (var child in element.Elements())
            Walk(child, pages);

        if (element.Name == W + "p")
            pages[^1].Append('\n');
    }

    private static XElement Paragraph(string text)
    {
        var rightToLeft = text.Any(IsArabic);
        var properties = new XElement(W + "pPr");
        var runProperties = new XElement(W + "rPr");
        if (rightToLeft)
        {
            properties.Add(new XElement(W + "bidi"));
            runProperties.Add(new XElement(W + "rtl"));
        }

        return new XElement(W + "p", properties,
            new XElement(W + "r", runProperties,
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }

    private static bool IsArabic(char c) => c is (>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
