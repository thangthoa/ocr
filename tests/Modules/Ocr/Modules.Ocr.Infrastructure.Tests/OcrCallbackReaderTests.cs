using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using Modules.Ocr.Infrastructure.Api;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Tests;

/// <summary>Callbacks signed the way the OCR service's guide describes (HMAC-SHA256 over "timestamp.body").</summary>
public sealed class OcrCallbackReaderTests
{
    private const string Secret = "callback-secret-for-tests";

    // The example from the service's guide, with page text filled in.
    private const string DoneJob = """
        {
          "job_id": "job_3f9c2a1b7d4e5f60",
          "status": "done",
          "file_name": "hop_dong.jpg",
          "doc_type": "printed",
          "pages_total": 1,
          "created_at": 1763371200.1, "started_at": 1763371201.5, "finished_at": 1763371312.9,
          "result": {
            "pages": [{"page": 1, "type": "printed", "text": "لما كان", "confidence": 0.93, "warnings": [], "cached": false}],
            "full_text": "لما كان",
            "confidence": 0.93
          },
          "error": null
        }
        """;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1763371312);

    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public void Signature_matches_the_services_reference_code()
    {
        var timestamp = Timestamp(Now);

        // Verbatim algorithm from the service's guide (string concatenation, lower-case hex).
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var expected = "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{DoneJob}"))).ToLowerInvariant();

        Assert.Equal(expected, OcrCallbackReader.Sign(Secret, timestamp, Encoding.UTF8.GetBytes(DoneJob)));
    }

    [Fact]
    public void Signed_job_is_read_with_its_pages()
    {
        var result = Reader().Read(Signed(DoneJob, Now));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var job = result.Value;
        Assert.Equal("job_3f9c2a1b7d4e5f60", job.Id.Value);
        Assert.Equal(OcrJobStatus.Done, job.Status);
        Assert.Equal("لما كان", Assert.Single(job.Pages).Text);
    }

    [Fact]
    public void Failed_job_keeps_the_services_error_code()
    {
        const string failed = """{"job_id":"job_a1","status":"failed","file_name":"a.pdf","pages_total":0,"result":null,"error":{"code":"bad_image","message":"no pages"}}""";

        var result = Reader().Read(Signed(failed, Now));

        Assert.Equal(OcrJobStatus.Failed, result.Value.Status);
        Assert.Equal("bad_image", result.Value.ErrorCode);
    }

    [Fact]
    public void Upper_case_signature_is_accepted()
    {
        var callback = Signed(DoneJob, Now);

        var result = Reader().Read(callback with { Signature = callback.Signature!.ToUpperInvariant() });

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(-299)]
    [InlineData(299)]
    public void Timestamp_within_five_minutes_is_accepted(int offsetSeconds)
    {
        Assert.True(Reader().Read(Signed(DoneJob, Now.AddSeconds(offsetSeconds))).IsSuccess);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void Timestamp_more_than_five_minutes_off_is_rejected(int offsetSeconds)
    {
        AssertRejected(Reader().Read(Signed(DoneJob, Now.AddSeconds(offsetSeconds))));
    }

    [Fact]
    public void Changed_body_is_rejected()
    {
        var callback = Signed(DoneJob, Now);

        AssertRejected(Reader().Read(callback with { Body = Encoding.UTF8.GetBytes(DoneJob.Replace("done", "failed", StringComparison.Ordinal)) }));
    }

    [Fact]
    public void Other_secret_is_rejected()
    {
        AssertRejected(Reader().Read(Signed(DoneJob, Now, secret: "someone-else")));
    }

    [Theory]
    [InlineData(null, "sig")]
    [InlineData("", "sig")]
    [InlineData("+1763371312", "sig")]
    [InlineData("1763371312.0", "sig")]
    [InlineData("ts", null)]
    [InlineData("ts", "")]
    public void Missing_or_malformed_headers_are_rejected(string? timestamp, string? signature)
    {
        var valid = Signed(DoneJob, Now);
        var callback = valid with
        {
            Timestamp = timestamp == "ts" ? valid.Timestamp : timestamp,
            Signature = signature == "sig" ? valid.Signature : signature,
        };

        AssertRejected(Reader().Read(callback));
    }

    [Fact]
    public void Nothing_is_accepted_while_no_secret_is_configured()
    {
        AssertRejected(Reader(secret: null).Read(Signed(DoneJob, Now)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"job_id":"job_a1","status":"exploded"}""")]
    [InlineData("""{"status":"done"}""")]
    [InlineData("null")]
    public void Signed_body_that_is_not_a_job_is_invalid(string body)
    {
        var result = Reader().Read(Signed(body, Now));

        Assert.Equal(OcrServiceErrorCodes.CallbackInvalid, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    private static void AssertRejected(Result<OcrJob> result)
    {
        Assert.Equal(OcrServiceErrorCodes.CallbackRejected, result.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    private static OcrCallback Signed(string body, DateTimeOffset at, string secret = Secret)
    {
        var timestamp = Timestamp(at);
        var bytes = Encoding.UTF8.GetBytes(body);
        return new OcrCallback(bytes, timestamp, OcrCallbackReader.Sign(secret, timestamp, bytes));
    }

    private static string Timestamp(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private OcrCallbackReader Reader(string? secret = Secret) =>
        new(Options.Create(new OcrApiOptions { CallbackSecret = secret }), _time);
}
