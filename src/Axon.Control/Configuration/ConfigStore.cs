using System.Text.Json;

namespace Axon.Control.Configuration;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;

    public ConfigStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<AxonOptions> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var options = await JsonSerializer.DeserializeAsync<AxonOptions>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new InvalidDataException("Axon configuration is empty.");

        ThrowIfInvalid(options);
        return options;
    }

    public async Task SaveAsync(
        AxonOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfInvalid(options);

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Configuration path has no parent directory.");
        var temporaryPath = _path + ".tmp";

        Directory.CreateDirectory(directory);

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    options,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ThrowIfInvalid(AxonOptions options)
    {
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }
}
