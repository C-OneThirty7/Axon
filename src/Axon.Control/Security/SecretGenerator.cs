using System.Security.Cryptography;

namespace Axon.Control.Security;

public sealed class SecretGenerator
{
    private const int SecretBytes = 32;

    public string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
