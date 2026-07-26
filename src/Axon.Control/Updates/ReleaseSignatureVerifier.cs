using System.Security.Cryptography;

namespace Axon.Control.Updates;

public static class ReleaseSignatureVerifier
{
    private const string PublicKey =
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVAmOnkqBCljf/HToTUhn2Mqf6NI1
        Ky1GkaPxhO/nIa6mg8E6FT75YXPAltEGd1br3J5eMEzZM/+prp0Wy2wr1A==
        -----END PUBLIC KEY-----
        """;

    public static async Task<bool> VerifyAsync(
        string archivePath,
        string signaturePath,
        CancellationToken cancellationToken = default)
    {
        await using var archive = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var signature = await File.ReadAllBytesAsync(signaturePath, cancellationToken);
        if (signature.Length is < 64 or > 256)
        {
            return false;
        }
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(PublicKey);
        return verifier.VerifyData(
            archive,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
    }
}
