using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The cryptography of Web Push, with .NET's own primitives: the message encryption of RFC 8291 (aes128gcm, RFC 8188),
/// so only the player's browser can read a notification, and the VAPID token of RFC 8292 that identifies this server to
/// the browser's push service.
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    /// <summary>Encrypts a message for one browser subscription, with a fresh key and salt.</summary>
    public static byte[] Encrypt(byte[] browserPublicKey, byte[] authSecret, byte[] plaintext)
    {
        using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(browserPublicKey, authSecret, plaintext, serverKey, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>
    /// Encrypts with a given key pair and salt (RFC 8291 section 3.4): the body is the aes128gcm header (salt, record
    /// size, the server's public key) followed by one encrypted record.
    /// </summary>
    public static byte[] Encrypt(byte[] browserPublicKey, byte[] authSecret, byte[] plaintext, ECDiffieHellman serverKey, byte[] salt)
    {
        var serverPublicKey = PublicKey(serverKey);
        using var browserKey = ECDiffieHellman.Create(PublicParameters(browserPublicKey));
        var sharedSecret = serverKey.DeriveRawSecretAgreement(browserKey.PublicKey);

        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), browserPublicKey, serverPublicKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var record = Concat(plaintext, [2]); // 2: the last (and only) record, no padding
        var ciphertext = new byte[record.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(contentKey, tag.Length))
        {
            aes.Encrypt(nonce, record, ciphertext, tag);
        }

        var header = new byte[16 + 4 + 1];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)serverPublicKey.Length;
        return Concat(header, serverPublicKey, ciphertext, tag);
    }

    /// <summary>
    /// The VAPID Authorization header value for a push service (RFC 8292): a short-lived ES256 token for the
    /// endpoint's origin, and the server's public key.
    /// </summary>
    public static string VapidAuthorization(Uri endpoint, ECDsa vapidKey, string subject, DateTimeOffset now)
    {
        var header = Base64Url("""{"typ":"JWT","alg":"ES256"}"""u8.ToArray());
        var audience = endpoint.GetLeftPart(UriPartial.Authority);
        var claims = Base64Url(Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { aud = audience, exp = (now + TimeSpan.FromHours(12)).ToUnixTimeSeconds(), sub = subject })));
        var signature = vapidKey.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256); // r‖s, as JWT wants
        return $"vapid t={header}.{claims}.{Base64Url(signature)}, k={Base64Url(PublicKey(vapidKey))}";
    }

    /// <summary>An uncompressed P-256 public key: 0x04, then X and Y.</summary>
    public static byte[] PublicKey(ECAlgorithm key)
    {
        var point = key.ExportParameters(false).Q;
        return Concat([4], point.X!, point.Y!);
    }

    /// <summary>Whether these are a browser's push keys: a P-256 public key and a 16-byte auth secret, base64url.</summary>
    public static bool IsBrowserKey(string p256dh, string auth)
    {
        try
        {
            using var key = ECDiffieHellman.Create(PublicParameters(FromBase64Url(p256dh)));
            return FromBase64Url(auth).Length == 16;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - (padded.Length % 4)) % 4));
    }

    private static ECParameters PublicParameters(byte[] uncompressed) =>
        uncompressed.Length == 65 && uncompressed[0] == 4
            ? new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..] } }
            : throw new CryptographicException("Not an uncompressed P-256 public key.");

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
