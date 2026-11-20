using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Infrastructure.Api;

/// <summary>
/// Checks a callback the way the OCR service signs it: <c>X-OCR-Signature</c> is
/// <c>sha256=&lt;hex HMAC-SHA256(secret, "&lt;X-OCR-Timestamp&gt;.&lt;raw body&gt;")&gt;</c>, and the body is
/// the same job object <c>GET /v1/jobs/{id}</c> returns.
/// </summary>
internal sealed class OcrCallbackReader(IOptions<OcrApiOptions> options, TimeProvider time) : IOcrCallbackReader
{
    /// <summary>The service asks to drop callbacks whose timestamp is more than 5 minutes off, against replays.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private const string SignaturePrefix = "sha256=";

    public Result<OcrJob> Read(OcrCallback callback)
    {
        var secret = options.Value.CallbackSecret;
        if (string.IsNullOrEmpty(secret))
            return Rejected("Callbacks are not configured.");

        // Digits only, so the header text is exactly what the service signed.
        if (!long.TryParse(callback.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs(time.GetUtcNow().ToUnixTimeSeconds() - seconds) > MaxClockSkew.TotalSeconds)
            return Rejected("The timestamp is missing or too far from the current time.");

        if (!HasValidSignature(secret, callback.Timestamp!, callback.Body.Span, callback.Signature))
            return Rejected("The signature does not match.");

        try
        {
            var job = JsonSerializer.Deserialize<ApiJob>(callback.Body.Span, OcrApiClient.Json)
                ?? throw new FormatException("Empty callback body.");
            return job.ToDomain();
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return Error.Validation(OcrServiceErrorCodes.CallbackInvalid, "The callback body is not a job.");
        }
    }

    /// <summary>The signature the service sends for this body; computed over the raw bytes, never re-serialized JSON.</summary>
    internal static string Sign(string secret, string timestamp, ReadOnlySpan<byte> body)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret));
        hmac.AppendData(Encoding.UTF8.GetBytes(timestamp + "."));
        hmac.AppendData(body);
        return SignaturePrefix + Convert.ToHexStringLower(hmac.GetHashAndReset());
    }

    private static bool HasValidSignature(string secret, string timestamp, ReadOnlySpan<byte> body, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp, body));
        var actual = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static Error Rejected(string message) => Error.Unauthorized(OcrServiceErrorCodes.CallbackRejected, message);
}
