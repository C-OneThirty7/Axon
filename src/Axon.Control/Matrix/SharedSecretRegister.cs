using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axon.Control.Matrix;

public interface IRegistrationSecretProvider
{
    ValueTask<string> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record RegistrationResult(bool Success, string? UserId, string Error)
{
    public static RegistrationResult Failed(string error) => new(false, null, error);
    public static RegistrationResult Succeeded(string userId) => new(true, userId, string.Empty);
}

public sealed partial class SharedSecretRegister(
    HttpClient httpClient,
    IRegistrationSecretProvider secretProvider)
{
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(15);

    public async Task<RegistrationResult> RegisterAsync(
        string username,
        string password,
        bool admin,
        CancellationToken cancellationToken = default)
    {
        if (!UsernamePattern().IsMatch(username))
        {
            return RegistrationResult.Failed(
                "Username must contain 1-64 lowercase letters, numbers, dots, underscores, equals signs, or hyphens.");
        }

        if (password.Length is < 12 or > 256)
        {
            return RegistrationResult.Failed("Password must contain 12-256 characters.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RegistrationTimeout);
        try
        {
            var sharedSecret = await secretProvider.GetAsync(timeout.Token);
            using var nonceResponse = await httpClient.GetAsync(
                "/_synapse/admin/v1/register",
                timeout.Token);
            if (!nonceResponse.IsSuccessStatusCode)
            {
                return RegistrationResult.Failed(
                    $"Synapse registration nonce returned HTTP {(int)nonceResponse.StatusCode}.");
            }

            var noncePayload = await nonceResponse.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: timeout.Token);
            if (!noncePayload.TryGetProperty("nonce", out var nonceElement) ||
                nonceElement.GetString() is not { Length: > 0 } nonce)
            {
                return RegistrationResult.Failed("Synapse registration nonce response was invalid.");
            }

            var payload = new
            {
                nonce,
                username,
                password,
                admin,
                mac = ComputeMac(sharedSecret, nonce, username, password, admin),
                inhibit_login = true
            };
            using var response = await httpClient.PostAsJsonAsync(
                "/_synapse/admin/v1/register",
                payload,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return RegistrationResult.Failed(
                    $"Synapse registration returned HTTP {(int)response.StatusCode}.");
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: timeout.Token);
            return result.TryGetProperty("user_id", out var userIdElement) &&
                userIdElement.GetString() is { Length: > 0 } userId
                ? RegistrationResult.Succeeded(userId)
                : RegistrationResult.Failed("Synapse registration response was invalid.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RegistrationResult.Failed("Synapse registration timed out.");
        }
        catch (HttpRequestException)
        {
            return RegistrationResult.Failed("Synapse registration request failed.");
        }
        catch (JsonException)
        {
            return RegistrationResult.Failed("Synapse registration response was invalid.");
        }
    }

    private static string ComputeMac(
        string sharedSecret,
        string nonce,
        string username,
        string password,
        bool admin)
    {
        var message = string.Join(
            '\0',
            nonce,
            username,
            password,
            admin ? "admin" : "notadmin");
        return Convert.ToHexStringLower(HMACSHA1.HashData(
            Encoding.UTF8.GetBytes(sharedSecret),
            Encoding.UTF8.GetBytes(message)));
    }

    [GeneratedRegex("^[a-z0-9._=-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
