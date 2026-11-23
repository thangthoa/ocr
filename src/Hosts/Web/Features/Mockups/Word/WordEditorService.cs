using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DownloadOcrResult;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Word;

/// <param name="ApiScriptUrl">The document server's api.js, loaded by the browser.</param>
/// <param name="Config">Editor configuration including its signed <c>token</c>.</param>
public sealed record WordEditorSession(DocumentRecord Document, WordVersion Version, string ApiScriptUrl, IReadOnlyDictionary<string, object?> Config);

public enum WordCallbackOutcome
{
    Accepted,
    Forbidden,
}

public static class WordErrors
{
    public const string NotEditable = "documents.not_editable";
    public const string EditorDisabled = "documents.editor_disabled";
    public const string ServerNotReady = "documents.editor_not_ready";
    public const string NoWordFile = "documents.no_word_file";
    public const string PreviewFailed = "documents.preview_failed";

    /// <summary>The OCR service is still making its Word file (<see cref="OcrJob.WordReady"/>).</summary>
    public const string WordPending = "documents.word_pending";
}

/// <summary>
/// Opens documents in OnlyOffice Docs and keeps what it saves (ADR 0004). The first Word version is
/// the OCR service's layout-keeping DOCX, or one built from the text when there is none (samples, service down).
/// </summary>
public sealed partial class WordEditorService(
    IOptions<WordEditorOptions> options,
    WordLinks links,
    IWordFileStore files,
    DocumentCatalog catalog,
    DocumentStore store,
    DownloadOcrResultHandler downloadOcr,
    AuditLog audit,
    DocumentServerClient server,
    TimeProvider time,
    ILogger<WordEditorService> logger)
{
    private const string ApiScriptPath = "web-apps/apps/api/documents/api.js";

    // OnlyOffice callback statuses: 2 = ready to save (all editors closed), 6 = saved with the Save button.
    private const int StatusReadyToSave = 2;
    private const int StatusForceSave = 6;
    private const int StatusSaveError = 3;
    private const int StatusForceSaveError = 7;

    public bool Enabled => options.Value.Enabled;

    public Task<IReadOnlyList<WordVersion>> VersionsAsync(string documentId, CancellationToken cancellationToken) =>
        Enabled ? files.ListAsync(documentId, cancellationToken) : Task.FromResult<IReadOnlyList<WordVersion>>([]);

    public async Task<Result<WordEditorSession>> OpenAsync(string documentId, string userName, CultureInfo culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!Enabled)
            return Error.Validation(WordErrors.EditorDisabled, "The Word editor is not configured.");

        // After a deploy the document server needs a minute or two; an editor opened earlier just breaks.
        if (!await server.IsReadyAsync(cancellationToken))
            return Error.Failure(WordErrors.ServerNotReady, "The document server is not ready.");

        var lookup = await catalog.GetAsync(documentId, cancellationToken);
        if (lookup.IsFailure)
            return lookup.Error!;
        var document = lookup.Value;
        if (!document.CanEdit)
            return Error.Conflict(WordErrors.NotEditable, "Only documents in review or approved can be edited.");

        // Editing the previous file now would save it over the new pages once it is saved.
        if (IsWordPending(document))
            return Error.Conflict(WordErrors.WordPending, "The OCR service is still making the Word file.");

        var latest = await CurrentVersionAsync(document, cancellationToken);
        var settings = options.Value;

        // The key identifies this exact file content to the document server; a new version needs a new key.
        var config = new Dictionary<string, object?>
        {
            ["document"] = new Dictionary<string, object?>
            {
                ["fileType"] = "docx",
                ["key"] = string.Create(CultureInfo.InvariantCulture, $"{document.Id}-{latest.Number}-{latest.Sha256[..16]}"),
                ["title"] = Path.GetFileNameWithoutExtension(document.FileName) + ".docx",
                ["url"] = links.FileUrl(document.Id, latest.Number),
                ["permissions"] = new Dictionary<string, object?> { ["edit"] = true, ["download"] = true, ["print"] = true },
            },
            ["documentType"] = "word",
            ["editorConfig"] = new Dictionary<string, object?>
            {
                ["callbackUrl"] = links.CallbackUrl(document.Id),
                ["lang"] = culture.TwoLetterISOLanguageName,
                ["mode"] = "edit",
                ["user"] = new Dictionary<string, object?> { ["id"] = userName, ["name"] = userName },
                ["customization"] = new Dictionary<string, object?> { ["forcesave"] = true },
            },
            ["width"] = "100%",
            ["height"] = "100%",
        };
        config["token"] = OnlyOfficeJwt.Sign(config, settings.JwtSecret!);

        var apiScript = settings.PublicUrl!.AbsoluteUri.TrimEnd('/') + "/" + ApiScriptPath;
        return new WordEditorSession(document, latest, apiScript, config);
    }

    /// <summary>
    /// The latest Word version. The OCR service's Word file becomes a new version when the document has none yet,
    /// and again once the pages changed (the text is then the OCR text of the new pages). Only call it for a
    /// document that has a recognized text (see <see cref="HasWordFile"/>) and whose Word file is not pending
    /// (see <see cref="IsWordPending"/>).
    /// </summary>
    public async Task<WordVersion> CurrentVersionAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var versions = await files.ListAsync(document.Id, cancellationToken);
        if (versions.Count == 0)
            return await AddOcrVersionAsync(document, cancellationToken);

        if (document.Versions.LastOrDefault(v => v.Change == DocumentVersionChange.PagesChanged) is not { WordVersion: null } pagesChanged)
            return versions[^1];

        var version = await AddOcrVersionAsync(document, cancellationToken);
        store.AttachWordVersion(document.Id, pagesChanged.Number, version.Number);
        return version;
    }

    /// <summary>A Word file exists, or can be made, once OCR has produced the text.</summary>
    public static bool HasWordFile(DocumentRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Status is DocumentStatus.NeedsReview or DocumentStatus.Archived;
    }

    /// <summary>
    /// The OCR job is done but its Word file is not complete yet: the images of printed pages are added after the
    /// text, and the file is rebuilt after a page is deleted. Taking it now would keep an incomplete file.
    /// </summary>
    public static bool IsWordPending(DocumentRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Job is { Status: OcrJobStatus.Done, WordReady: false };
    }

    /// <summary>Handles a document server callback. Saves the edited file for statuses 2 and 6.</summary>
    public async Task<WordCallbackOutcome> HandleCallbackAsync(string documentId, string? signature, JsonElement body, string? authorization, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Enabled || !links.IsValidCallbackLink(documentId, signature) || !TryReadVerifiedCallback(body, authorization, settings.JwtSecret!, out var callback))
            return WordCallbackOutcome.Forbidden;

        var status = callback.TryGetProperty("status", out var s) && s.TryGetInt32(out var value) ? value : 0;
        if (status is StatusSaveError or StatusForceSaveError)
        {
            LogSaveError(logger, documentId, status);
            return WordCallbackOutcome.Accepted;
        }

        if (status is not (StatusReadyToSave or StatusForceSave))
            return WordCallbackOutcome.Accepted;

        if (!callback.TryGetProperty("url", out var urlElement) || urlElement.GetString() is not { } url)
            return WordCallbackOutcome.Accepted;

        var author = callback.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0
            ? users[0].GetString() ?? LoginPageUser
            : LoginPageUser;

        var content = await server.DownloadAsync(url, cancellationToken);
        var (version, created) = await files.SaveAsync(documentId, new MemoryStream(content, writable: false), author, WordVersionSource.Edited, cancellationToken);
        if (!created)
            return WordCallbackOutcome.Accepted;

        IReadOnlyList<string> pages;
        try
        {
            pages = DocxText.ExtractPages(new MemoryStream(content, writable: false));
        }
        catch (InvalidDataException ex)
        {
            LogUnreadable(logger, ex, documentId, version.Number);
            pages = [];
        }

        store.RecordWordEdit(documentId, pages, author, version.Number);
        audit.Record(author, AuditAction.EditedInWord, documentId, store.FileNameOf(documentId), version: version.Number.ToString(CultureInfo.InvariantCulture));
        return WordCallbackOutcome.Accepted;
    }

    private static string LoginPageUser => Pages.LoginPage.DemoUserName;

    /// <summary>
    /// The OCR service's Word file, or one made from the text when the service has none. It is the variant that keeps
    /// the page layout of the scan (<see cref="OcrResultFormat.DocxLayout"/>): the preview shows it page by page next
    /// to the scan, and it is what the editor opens and the archive hands out.
    /// </summary>
    private async Task<WordVersion> AddOcrVersionAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        if (document.Job is { Status: OcrJobStatus.Done })
        {
            var ocr = await downloadOcr.HandleAsync(new DownloadOcrResultQuery(document.Id, OcrResultFormat.DocxLayout), cancellationToken);
            if (ocr.IsSuccess)
            {
                await using var stream = ocr.Value.Content;
                return (await files.SaveAsync(document.Id, stream, DocumentStore.OcrAuthor, WordVersionSource.Ocr, cancellationToken)).Version;
            }

            LogOcrDocxUnavailable(logger, document.Id, ocr.Error!.Code);
        }

        var generated = DocxText.Create(document.PageTexts.Count > 0 ? document.PageTexts : [string.Empty]);
        return (await files.SaveAsync(document.Id, new MemoryStream(generated, writable: false), DocumentStore.OcrAuthor, WordVersionSource.Generated, cancellationToken)).Version;
    }

    /// <summary>The server sends its token in the body, or in the Authorization header wrapped as <c>{ payload }</c>.</summary>
    private bool TryReadVerifiedCallback(JsonElement body, string? authorization, string secret, out JsonElement callback)
    {
        callback = default;
        var now = time.GetUtcNow();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("token", out var token) && token.GetString() is { } bodyToken)
            return OnlyOfficeJwt.TryValidate(bodyToken, secret, now, out callback);

        const string bearer = "Bearer ";
        if (authorization is not null && authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            && OnlyOfficeJwt.TryValidate(authorization[bearer.Length..].Trim(), secret, now, out var wrapped))
        {
            callback = wrapped.TryGetProperty("payload", out var payload) ? payload : wrapped;
            return true;
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word editor: the document server could not save {DocumentId} (status {Status})")]
    private static partial void LogSaveError(ILogger logger, string documentId, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word editor: version {Version} of {DocumentId} was saved but its text could not be read")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string documentId, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Word editor: no DOCX from the OCR service for {DocumentId} ({Code}); starting from the text")]
    private static partial void LogOcrDocxUnavailable(ILogger logger, string documentId, string code);
}
