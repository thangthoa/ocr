using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OppOcr.Web.Features.Mockups.Word;

/// <summary>
/// HS256 JSON Web Tokens as OnlyOffice Docs uses them: the editor configuration is signed so the
/// browser cannot change it, and the document server signs its save callbacks.
/// </summary>
internal static class OnlyOfficeJwt
{
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}""");

    public static string Sign(object payload, string secret)
    {
        var head = Base64Url(Header);
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{head}.{body}.{Base64Url(Mac($"{head}.{body}", secret))}";
    }

    /// <summary>Checks the signature (and the expiry when the token has one) and returns the payload.</summary>
    public static bool TryValidate(string token, string secret, DateTimeOffset now, out JsonElement payload)
    {
        payload = default;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var signature = FromBase64Url(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(signature, Mac($"{parts[0]}.{parts[1]}", secret)))
                return false;
            if (!JsonDocument.Parse(FromBase64Url(parts[0])).RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "HS256")
                return false;

            payload = JsonDocument.Parse(FromBase64Url(parts[1])).RootElement.Clone();
            return !(payload.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) && seconds < now.ToUnixTimeSeconds());
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    private static byte[] Mac(string data, string secret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}

/// <summary>
/// Links handed to the document server, which has no session: each carries an HMAC so it cannot be
/// guessed or pointed at another document. File links also expire.
/// </summary>
public sealed class WordLinks(IOptions<WordEditorOptions> options, TimeProvider time)
{
    private const string FilePurpose = "file";
    private const string CallbackPurpose = "callback";

    public string FileUrl(string documentId, int version)
    {
        var expires = time.GetUtcNow().AddMinutes(options.Value.LinkLifetimeMinutes).ToUnixTimeSeconds();
        var signature = Sign(documentId, FilePurpose, version, expires);
        return Absolute(string.Create(CultureInfo.InvariantCulture,
            $"documents/{Uri.EscapeDataString(documentId)}/word/file?v={version}&exp={expires}&sig={signature}"));
    }

    /// <summary>No expiry: the server calls back when the last editor closes, which can be hours later. Its body is JWT-signed too.</summary>
    public string CallbackUrl(string documentId) =>
        Absolute($"documents/{Uri.EscapeDataString(documentId)}/word/callback?sig={Sign(documentId, CallbackPurpose, 0, 0)}");

    public bool IsValidFileLink(string documentId, int version, long expires, string? signature) =>
        expires >= time.GetUtcNow().ToUnixTimeSeconds() && Matches(Sign(documentId, FilePurpose, version, expires), signature);

    public bool IsValidCallbackLink(string documentId, string? signature) =>
        Matches(Sign(documentId, CallbackPurpose, 0, 0), signature);

    private string Absolute(string path) => options.Value.AppUrl!.AbsoluteUri.TrimEnd('/') + "/" + path;

    private string Sign(string documentId, string purpose, int version, long expires)
    {
        var data = string.Create(CultureInfo.InvariantCulture, $"{documentId}|{purpose}|{version}|{expires}");
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.JwtSecret!), Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(mac);
    }

    private static bool Matches(string expected, string? actual) =>
        actual is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
}
