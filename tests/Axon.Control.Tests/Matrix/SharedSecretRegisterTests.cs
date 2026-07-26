using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axon.Control.Matrix;

namespace Axon.Control.Tests.Matrix;

public sealed class SharedSecretRegisterTests
{
    [Fact]
    public async Task Registers_with_the_exact_nonce_HMAC_and_inhibits_login()
    {
        const string sharedSecret = "registration-secret";
        var handler = new RegistrationHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8008") };
        var register = new SharedSecretRegister(client, new FixedSecretProvider(sharedSecret));

        var result = await register.RegisterAsync("user.one", "long-password-123", admin: false);

        Assert.True(result.Success);
        Assert.Equal("@user.one:axon.home.arpa", result.UserId);
        Assert.NotNull(handler.Posted);
        var posted = handler.Posted.Value;
        Assert.Equal("fixed-nonce", posted.GetProperty("nonce").GetString());
        Assert.Equal("user.one", posted.GetProperty("username").GetString());
        Assert.Equal("long-password-123", posted.GetProperty("password").GetString());
        Assert.False(posted.GetProperty("admin").GetBoolean());
        Assert.True(posted.GetProperty("inhibit_login").GetBoolean());
        Assert.Equal(
            ComputeMac(sharedSecret, "fixed-nonce", "user.one", "long-password-123", admin: false),
            posted.GetProperty("mac").GetString());
    }

    [Theory]
    [InlineData("UPPER", "long-password-123")]
    [InlineData("bad space", "long-password-123")]
    [InlineData("valid", "short")]
    public async Task Rejects_invalid_credentials_before_HTTP(string username, string password)
    {
        var handler = new RegistrationHandler();
        var register = new SharedSecretRegister(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8008") },
            new FixedSecretProvider("secret"));

        var result = await register.RegisterAsync(username, password, admin: false);

        Assert.False(result.Success);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task HTTP_failures_are_sanitized()
    {
        const string password = "long-password-123";
        const string secret = "registration-secret";
        var handler = new RegistrationHandler { FailPost = true };
        var register = new SharedSecretRegister(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8008") },
            new FixedSecretProvider(secret));

        var result = await register.RegisterAsync("user1", password, admin: false);

        Assert.False(result.Success);
        Assert.DoesNotContain(password, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Error, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", result.Error, StringComparison.Ordinal);
    }

    private static string ComputeMac(
        string secret,
        string nonce,
        string username,
        string password,
        bool admin)
    {
        var message = string.Join('\0', nonce, username, password, admin ? "admin" : "notadmin");
        return Convert.ToHexStringLower(HMACSHA1.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(message)));
    }

    private sealed class FixedSecretProvider(string secret) : IRegistrationSecretProvider
    {
        public ValueTask<string> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(secret);
    }

    private sealed class RegistrationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public JsonElement? Posted { get; private set; }
        public bool FailPost { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, "{\"nonce\":\"fixed-nonce\"}");
            }

            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            Posted = JsonDocument.Parse(content).RootElement.Clone();
            return FailPost
                ? Json(HttpStatusCode.InternalServerError, "{\"error\":\"sensitive server response\"}")
                : Json(HttpStatusCode.OK, "{\"user_id\":\"@user.one:axon.home.arpa\"}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
