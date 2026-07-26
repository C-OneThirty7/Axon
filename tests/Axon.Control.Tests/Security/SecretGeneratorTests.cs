using Axon.Control.Security;

namespace Axon.Control.Tests.Security;

public sealed class SecretGeneratorTests
{
    [Fact]
    public void Generated_secrets_are_distinct_and_contain_256_bits()
    {
        var generator = new SecretGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first, second);
        Assert.Equal(32, DecodeBase64Url(first).Length);
        Assert.Equal(32, DecodeBase64Url(second).Length);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(padded);
    }
}
