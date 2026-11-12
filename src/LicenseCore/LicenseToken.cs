using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JacquardLicensing;

// Token format JSL1.base64url(UTF8 JSON).base64url(ECDSA P-256/SHA-256 DER signature).
// The signature covers the exact bytes of the JSON, not a reserialized document.
public sealed record LicenseClaims(int Version, string Product, string Device, string LicenseId, long IssuedAt, long NotBefore, long ExpiresAt, string? CustomerName = null);

public static class LicenseToken
{
    public const string Product = "JacquardStudio";
    public const int MaximumLeaseSeconds = 7 * 24 * 60 * 60;
    private const int MaxTokenLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Encode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Decode(string text)
    {
        if (text.Length == 0 || text.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))) throw new FormatException("Invalid base64url.");
        return Convert.FromBase64String(text.Replace('-','+').Replace('_','/') + new string('=', (4-text.Length%4)%4));
    }
    private static bool IsP256(ECDsa key) => key.KeySize==256 && key.ExportParameters(false).Curve.Oid.Value=="1.2.840.10045.3.1.7";
    public static bool IsDevice(string? value) => value is {Length:64} && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool ValidClaims(LicenseClaims c) => (c.CustomerName is null || c.CustomerName.Length <= 120 && !c.CustomerName.Any(char.IsControl)) && c.Version == 1 && c.Product == Product && IsDevice(c.Device) && Guid.TryParseExact(c.LicenseId,"D",out var licenseId) && licenseId != Guid.Empty && c.IssuedAt > 0 && c.NotBefore >= c.IssuedAt - 300 && c.ExpiresAt > c.IssuedAt && c.ExpiresAt - c.IssuedAt <= MaximumLeaseSeconds && c.NotBefore < c.ExpiresAt;
    public static string Issue(ECDsa privateKey, string device, Guid licenseId, DateTimeOffset now, TimeSpan validity, string? customerName = null)
    {
        if (!IsP256(privateKey) || !IsDevice(device) || licenseId == Guid.Empty || validity <= TimeSpan.Zero || validity > TimeSpan.FromSeconds(MaximumLeaseSeconds)) throw new ArgumentException("Invalid license parameters.");
        long issued=now.ToUnixTimeSeconds();var claims=new LicenseClaims(1,Product,device,licenseId.ToString("D"),issued,issued-60,issued+(long)validity.TotalSeconds,customerName?.Trim());
        if(!ValidClaims(claims))throw new ArgumentException("Invalid license times.");
        var payload=JsonSerializer.SerializeToUtf8Bytes(claims,JsonOptions);var signature=privateKey.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);
        return "JSL1."+Encode(payload)+"."+Encode(signature);
    }
    public static bool TryVerify(ECDsa publicKey, string? token, string device, DateTimeOffset now, out LicenseClaims? claims)
    {
        claims=null;
        if (!IsP256(publicKey) || !IsDevice(device) || token is null || token.Length>MaxTokenLength) return false;
        try
        {
            var parts=token.Split('.');if(parts.Length!=3 || parts[0]!="JSL1")return false;var payload=Decode(parts[1]);var signature=Decode(parts[2]);
            if(payload.Length>2048 || signature.Length>80 || !publicKey.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence))return false;
            var c=JsonSerializer.Deserialize<LicenseClaims>(payload,JsonOptions);long timestamp=now.ToUnixTimeSeconds();
            if(c is null || !ValidClaims(c) || c.Device!=device || timestamp<c.NotBefore || timestamp>=c.ExpiresAt) return false;
            claims=c;return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException) {return false;}
    }
    public static string DeviceDigest(string cpuId, string diskId)
    {
        static string Normalize(string input) => input.Trim().ToUpperInvariant();
        static bool Invalid(string value) => value.Length is < 4 or > 256 || value.All(c=>c is '0' or ' ' or '-') || value is "UNKNOWN" or "NONE" or "NOT SPECIFIED" or "TO BE FILLED BY O.E.M." or "DEFAULT STRING";
        if(cpuId is null || diskId is null)throw new ArgumentException("Hardware identifier is missing.");
        string cpu=Normalize(cpuId),disk=Normalize(diskId);if(Invalid(cpu) || Invalid(disk))throw new ArgumentException("Hardware identifier is unavailable or invalid.");
        // Length prefixes avoid ambiguous concatenations; no raw hardware IDs are included in the request.
        string input=$"JACQUARD-DEVICE-1\n{cpu.Length}:{cpu}\n{disk.Length}:{disk}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
