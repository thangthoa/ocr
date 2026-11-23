using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Api;

/// <summary>HTTP adapter for the OCR service. Base address, key and timeout are set at registration.</summary>
internal sealed partial class OcrApiClient(
    HttpClient http,
    OcrSourceFileStore sourceFiles,
    IOptions<OcrApiOptions> options,
    ILogger<OcrApiClient> logger) : IOcrApiClient
{
    public const string ApiKeyHeader = "X-API-Key";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<OcrJob>> SubmitAsync(Stream content, string fileName, OcrDocumentType documentType, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(documentType.ToWire()), "doc_type");

        // Uploading from the office network to the service is very slow; the service downloads the same
        // file from this application's public address many times faster (ADR 0006). It does so before
        // answering, so the link is only needed while this request runs.
        using var link = settings.AppUrl is null ? null : await PublishAsync(content, fileName, settings, cancellationToken);
        if (link is null)
        {
            // StreamContent disposes its stream; the caller owns `content`, so hand over a wrapper.
            var file = new StreamContent(new NonClosingStream(content));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
        }
        else
        {
            form.Add(new StringContent(AppLink(settings, $"{OcrServicePaths.SourceFiles}/{link.Token}/{Uri.EscapeDataString(fileName)}")), "file_url");
        }

        if (settings.CallbacksEnabled)
            form.Add(new StringContent(AppLink(settings, OcrServicePaths.Callback)), "callback_url");

        return await SendForJobAsync(() => new HttpRequestMessage(HttpMethod.Post, "v1/jobs") { Content = form }, cancellationToken);
    }

    public Task<Result<OcrJob>> GetAsync(OcrJobId jobId, CancellationToken cancellationToken) =>
        SendForJobAsync(() => new HttpRequestMessage(HttpMethod.Get, JobPath(jobId)), cancellationToken);

    public async Task<Result<PagedResult<OcrJob>>> ListAsync(int page, int pageSize, OcrJobStatus? status, CancellationToken cancellationToken)
    {
        // GET /v1/jobs: newest first, limit 1–200 + offset, optional status; jobs come without their text.
        var query = $"v1/jobs?limit={pageSize}&offset={(page - 1) * pageSize}" + (status is null ? string.Empty : $"&status={status.Value.ToWire()}");
        try
        {
            using var response = await http.GetAsync(query, cancellationToken);
            // Older service versions had no list endpoint (404/405); fall back to the in-memory list then.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                return Error.Validation(OcrServiceErrorCodes.ListNotSupported, "The OCR service cannot list jobs yet.");
            if (!response.IsSuccessStatusCode)
                return await ToErrorAsync(response, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var (jobs, total) = ParseJobPage(json);
            return new PagedResult<OcrJob>(jobs, page, pageSize, total ?? ((page - 1) * pageSize) + jobs.Count);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            LogUnavailable(ex);
            return Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "The OCR service could not be reached.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            LogUnexpectedResponse(ex);
            return Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "The OCR service returned an unexpected response.");
        }
    }

    public async Task<Result> CancelAsync(OcrJobId jobId, CancellationToken cancellationToken)
    {
        var result = await SendForJobAsync(() => new HttpRequestMessage(HttpMethod.Delete, JobPath(jobId)), cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public Task<Result<FileContent>> GetPageImageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken) =>
        SendForFileAsync($"{JobPath(jobId)}/pages/{pageNumber}/image", $"page-{pageNumber}", cancellationToken);

    public Task<Result<FileContent>> DownloadAsync(OcrJobId jobId, OcrResultFormat format, CancellationToken cancellationToken) =>
        SendForFileAsync($"{JobPath(jobId)}/download?format={format.ToWire()}", $"result.{format.FileExtension()}", cancellationToken);

    // Page changes (API doc §3.8) take only uploaded files: the service offers no file_url there (checked against
    // its openapi.json, 2026-11-21), so the files go up directly even when AppUrl is set.
    public Task<Result<OcrJob>> InsertPagesAsync(OcrJobId jobId, IReadOnlyList<OcrUpload> files, int? position, CancellationToken cancellationToken) =>
        SendForJobAsync(() =>
        {
            var form = FilesForm(files);
            if (position is { } value)
                form.Add(new StringContent(value.ToString(CultureInfo.InvariantCulture)), "position");
            return new HttpRequestMessage(HttpMethod.Post, $"{JobPath(jobId)}/pages") { Content = form };
        }, cancellationToken);

    public Task<Result<OcrJob>> ReplacePageAsync(OcrJobId jobId, int pageNumber, OcrUpload file, CancellationToken cancellationToken) =>
        SendForJobAsync(() => new HttpRequestMessage(HttpMethod.Put, PagePath(jobId, pageNumber)) { Content = FilesForm([file]) }, cancellationToken);

    public Task<Result<OcrJob>> DeletePageAsync(OcrJobId jobId, int pageNumber, CancellationToken cancellationToken) =>
        SendForJobAsync(() => new HttpRequestMessage(HttpMethod.Delete, PagePath(jobId, pageNumber)), cancellationToken);

    /// <summary>Calls the unauthenticated health endpoint (API doc §3.7).</summary>
    internal async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync("v1/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            return false;
        }
    }

    internal static (IReadOnlyList<OcrJob> Jobs, int? Total) ParseJobPage(string json)
    {
        var page = JsonSerializer.Deserialize<ApiJobPage>(json, Json) ?? throw new FormatException("Empty job list.");
        var jobs = page.Jobs ?? throw new FormatException("Job list has no 'jobs'.");
        return ([.. jobs.Select(j => j.ToDomain())], page.Total);
    }

    private static string JobPath(OcrJobId jobId) => $"v1/jobs/{Uri.EscapeDataString(jobId.Value)}";

    private static string PagePath(OcrJobId jobId, int pageNumber) =>
        string.Create(CultureInfo.InvariantCulture, $"{JobPath(jobId)}/pages/{pageNumber}");

    /// <summary>One <c>file</c> field per file, in order: the service keeps that order for the pages.</summary>
    private static MultipartFormDataContent FilesForm(IReadOnlyList<OcrUpload> files)
    {
        var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var content = new ReadOnlyMemoryContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "file", file.FileName);
        }

        return form;
    }

    private static string AppLink(OcrApiOptions settings, string path) => settings.AppUrl!.AbsoluteUri.TrimEnd('/') + "/" + path;

    private async Task<OcrSourceFileStore.Lease> PublishAsync(Stream content, string fileName, OcrApiOptions settings, CancellationToken cancellationToken)
    {
        byte[] bytes;
        if (content.CanSeek)
        {
            bytes = new byte[content.Length - content.Position];
            await content.ReadExactlyAsync(bytes, cancellationToken);
        }
        else
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        return sourceFiles.Add(bytes, fileName, TimeSpan.FromMinutes(settings.FileLinkLifetimeMinutes));
    }

    private async Task<Result<OcrJob>> SendForJobAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        try
        {
            using var request = createRequest();
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return await ToErrorAsync(response, cancellationToken);

            var job = await response.Content.ReadFromJsonAsync<ApiJob>(Json, cancellationToken)
                ?? throw new FormatException("Empty job response.");
            return job.ToDomain();
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            LogUnavailable(ex);
            return Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "The OCR service could not be reached.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            LogUnexpectedResponse(ex);
            return Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "The OCR service returned an unexpected response.");
        }
    }

    private async Task<Result<FileContent>> SendForFileAsync(string path, string fallbackFileName, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ToErrorAsync(response, cancellationToken);
                response.Dispose();
                return error;
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? fallbackFileName;
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // The response owns the connection; disposing the returned stream releases it.
            return new FileContent(new ResponseStream(stream, response), contentType, fileName);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            response?.Dispose();
            LogUnavailable(ex);
            return Error.Failure(OcrServiceErrorCodes.ServiceUnavailable, "The OCR service could not be reached.");
        }
    }

    private static async Task<Error> ToErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiError? apiError = null;
        try
        {
            apiError = (await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(Json, cancellationToken))?.Error;
        }
        catch (JsonException)
        {
            // Not the documented error shape (e.g. a proxy error page); fall back to the status code.
        }

        var code = apiError?.Code ?? (response.StatusCode == HttpStatusCode.UnprocessableEntity ? "invalid_request" : OcrServiceErrorCodes.ServiceUnavailable);
        var message = apiError?.Message ?? $"HTTP {(int)response.StatusCode}";

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => Error.NotFound(code, message),
            HttpStatusCode.Conflict => Error.Conflict(code, message),
            HttpStatusCode.TooManyRequests => Error.Failure(code, message),
            >= HttpStatusCode.InternalServerError => Error.Failure(code, message),
            _ => Error.Validation(code, message),
        };
    }

    // Timeouts surface as TaskCanceledException; only the caller's own cancellation is not transient.
    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr API: service unavailable")]
    private partial void LogUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ocr API: unexpected response")]
    private partial void LogUnexpectedResponse(Exception exception);

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
