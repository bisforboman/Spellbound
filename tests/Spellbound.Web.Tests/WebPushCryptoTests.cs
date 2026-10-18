using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Web Push's encryption and server identification, checked against the standards' own examples.</summary>
public sealed class WebPushCryptoTests
{
    /// <summary>RFC 8291, appendix A: one complete encryption with every input given, and the exact bytes it must give.</summary>
    [Fact]
    public void Encryption_matches_the_RFC_8291_example()
    {
        var serverPrivate = WebPushCrypto.FromBase64Url("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var serverPublic = WebPushCrypto.FromBase64Url("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var browserPublic = WebPushCrypto.FromBase64Url("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var authSecret = WebPushCrypto.FromBase64Url("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = WebPushCrypto.FromBase64Url("DGv6ra1nlYgDCS1FRnbzlw");
        using var serverKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = serverPrivate,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..] },
        });

        var body = WebPushCrypto.Encrypt(browserPublic, authSecret, "When I grow up, I want to be a watermelon"u8.ToArray(), serverKey, salt);

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebPushCrypto.Base64Url(body));
    }

    [Fact]
    public void The_VAPID_token_is_signed_for_the_endpoints_origin_and_expires_within_a_day()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        var header = WebPushCrypto.VapidAuthorization(new Uri("https://push.example.net/send/abc?x=1"), key, "mailto:admin@example.com", now);

        var token = header["vapid t=".Length..header.IndexOf(',', StringComparison.Ordinal)];
        var parts = token.Split('.');
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), WebPushCrypto.FromBase64Url(parts[2]), HashAlgorithmName.SHA256));
        var claims = JsonDocument.Parse(WebPushCrypto.FromBase64Url(parts[1])).RootElement;
        Assert.Equal("https://push.example.net", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:admin@example.com", claims.GetProperty("sub").GetString());
        Assert.InRange(claims.GetProperty("exp").GetInt64() - now.ToUnixTimeSeconds(), 1, 24 * 3600);
        Assert.EndsWith($"k={WebPushCrypto.Base64Url(WebPushCrypto.PublicKey(key))}", header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_encryption_can_be_read_back_by_the_browser()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);

        var body = WebPushCrypto.Encrypt(WebPushCrypto.PublicKey(browser), auth, "Round 3 played"u8.ToArray());

        // Decrypt as a browser would (RFC 8291, the receiving side).
        var salt = body[..16];
        var serverPublic = body[21..86];
        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..] },
        });
        var shared = browser.DeriveRawSecretAgreement(server.PublicKey);
        var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(WebPushCrypto.PublicKey(browser)).Concat(serverPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, info);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var record = body[86..];
        var plain = new byte[record.Length - 16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, record[..^16], record[^16..], plain);
        }

        Assert.Equal("Round 3 played\u0002", Encoding.UTF8.GetString(plain));
    }
}
