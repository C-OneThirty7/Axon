namespace Axon.Control.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Product_identity_is_stable()
    {
        Assert.Equal("Axon", global::Axon.Control.ProductInfo.Name);
        Assert.Equal("0.2.0", global::Axon.Control.ProductInfo.Version);
        Assert.Equal("axon.home.arpa", global::Axon.Control.ProductInfo.ServerName);
    }
}
