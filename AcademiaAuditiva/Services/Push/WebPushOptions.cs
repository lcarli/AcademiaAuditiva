using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace AcademiaAuditiva.Services.Push;

public sealed class WebPushOptions
{
    public const string Section = "WebPush";
    public bool Enabled { get; set; }
    public string Subject { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";

    public bool IsValid()
    {
        if (!Enabled) return true;
        if (!Uri.TryCreate(Subject, UriKind.Absolute, out var subject)
            || (subject.Scheme != "mailto" && subject.Scheme != "https")) return false;
        try
        {
            var key = WebEncoders.Base64UrlDecode(PublicKey);
            var secret = WebEncoders.Base64UrlDecode(PrivateKey);
            if (key.Length != 65 || key[0] != 4 || secret.Length != 32) return false;
            using var signer = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key[1..33], Y = key[33..65] },
                D = secret
            });
            using var verifier = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key[1..33], Y = key[33..65] }
            });
            byte[] data = [1, 2, 3];
            return verifier.VerifyData(data, signer.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
