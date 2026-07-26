using Axon.Control.Configuration;

namespace Axon.Control.Tests.Configuration;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"axon-config-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Save_and_load_round_trip_without_leaving_a_temporary_file()
    {
        var path = Path.Combine(_directory, "axon.json");
        var store = new ConfigStore(path);
        var expected = AxonOptions.Default with { RetentionMinutes = 1440 };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Save_rejects_invalid_configuration_before_creating_files()
    {
        var path = Path.Combine(_directory, "axon.json");
        var store = new ConfigStore(path);
        var invalid = AxonOptions.Default with { BindIp = "8.8.8.8" };

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(invalid));

        Assert.Contains("private IPv4", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
