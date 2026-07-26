using System.Net;
using System.Security.Cryptography;
using Axon.Control.Installation;
using Axon.Control.Matrix;
using Axon.Control.Runtime;
using Axon.Control.Security;
using Axon.Control.Updates;

namespace Axon.Control;

public static class ProductInfo
{
    public const string Name = "Axon";
    public const string Version = "0.3.1";
    public const string ServerName = "axon.home.arpa";
}

public sealed record LoginRequest(string Username, string Password);
public sealed record CreateUserRequest(string Username, string Password, string? DisplayName, bool Admin);
public sealed record BatchUsersRequest(
    string Prefix,
    int Start,
    int Count,
    int Padding,
    string Password,
    bool Admin);
public sealed record UpdateUserRequest(
    string? Password,
    string? DisplayName,
    bool? Admin,
    bool? Deactivated,
    bool? Locked,
    bool LogoutDevices = true);
public sealed record ServiceActionRequest(string Service, string Action);
public sealed record StackActionRequest(string Action);
public sealed record CreateRoomRequest(string Name, string? Topic, IReadOnlyList<string> Invite);
public sealed record RoomMemberRequest(string UserId);
public sealed record BatchUserResult(string Username, bool Success, string Error);

public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 &&
            string.Equals(args[0], "verify-update", StringComparison.Ordinal))
        {
            return await VerifyUpdateAsync(args[1], args[2]);
        }
        if (args.Length > 0 && string.Equals(args[0], "render-runtime", StringComparison.Ordinal))
        {
            return await RenderRuntimeAsync(args[1..]);
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(8780));

        var bundleRoot = GetArgument(args, "--bundle-root")
            ?? builder.Configuration["AXON_BUNDLE_ROOT"]
            ?? AppContext.BaseDirectory;
        var dataRoot = GetArgument(args, "--data-root")
            ?? builder.Configuration["AXON_DATA_ROOT"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Axon")
                : OperatingSystem.IsLinux()
                    ? "/etc/axon"
                    : Path.Combine(bundleRoot, "runtime", "macos-poc"));

        builder.Services.AddSingleton(new OperatorSessions());
        builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
        builder.Services.AddSingleton(provider =>
            new DockerRuntime(provider.GetRequiredService<ICommandRunner>(), bundleRoot, dataRoot));
        builder.Services.AddSingleton(_ =>
            new SynapseAdminClient(SynapseClient.CreateLoopbackHttpClient()));
        builder.Services.AddSingleton(_ =>
            new SynapseClient(SynapseClient.CreateLoopbackHttpClient()));
        builder.Services.AddSingleton(_ =>
            new GithubReleaseClient(GithubReleaseClient.CreateHttpClient()));
        builder.Services.AddSingleton(provider =>
            new UpdateManager(
                UpdateManager.CreateHttpClient(),
                provider.GetRequiredService<GithubReleaseClient>(),
                bundleRoot,
                dataRoot));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; " +
                "img-src 'self' data:; connect-src 'self'; object-src 'none'; " +
                "base-uri 'none'; frame-ancestors 'none'; form-action 'self'";

            if (context.Request.Path.StartsWithSegments("/api") &&
                !HttpMethods.IsGet(context.Request.Method) &&
                !HttpMethods.IsHead(context.Request.Method) &&
                !HttpMethods.IsOptions(context.Request.Method) &&
                context.Request.Headers.TryGetValue("Origin", out var origins) &&
                origins.Any(origin =>
                    !string.Equals(origin, "http://127.0.0.1:8780", StringComparison.Ordinal) &&
                    !string.Equals(origin, "http://localhost:8780", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/session", (HttpContext context, OperatorSessions sessions) =>
        {
            return TrySession(context, sessions, out var session)
                ? Results.Ok(new { authenticated = true, userId = session.UserId, expiresAt = session.ExpiresAt })
                : Results.Ok(new { authenticated = false });
        });

        app.MapPost("/api/session", async (
            LoginRequest request,
            HttpContext context,
            SynapseAdminClient client,
            OperatorSessions sessions,
            CancellationToken cancellationToken) =>
        {
            var result = await client.LoginAsync(request.Username, request.Password, cancellationToken);
            if (!result.Success)
            {
                return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var key = sessions.Create(result.UserId!, result.AccessToken!);
            context.Response.Cookies.Append(
                OperatorSessions.CookieName,
                key,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = false,
                    Path = "/",
                    MaxAge = TimeSpan.FromHours(8)
                });
            return Results.Ok(new { userId = result.UserId });
        });

        app.MapDelete("/api/session", (HttpContext context, OperatorSessions sessions) =>
        {
            context.Request.Cookies.TryGetValue(OperatorSessions.CookieName, out var key);
            sessions.Remove(key);
            context.Response.Cookies.Delete(OperatorSessions.CookieName);
            return Results.NoContent();
        });

        app.MapGet("/api/status", async (
            HttpContext context,
            OperatorSessions sessions,
            SynapseClient synapse,
            DockerRuntime docker,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            var health = await synapse.CheckHealthAsync(cancellationToken);
            var status = await docker.GetStatusAsync(cancellationToken);
            var stats = await docker.GetStatsAsync(cancellationToken);
            return Results.Ok(new
            {
                serverName = ProductInfo.ServerName,
                controlUrl = "http://127.0.0.1:8780",
                synapse = health,
                docker = new
                {
                    healthy = status.ExitCode == 0 && !status.TimedOut,
                    output = status.StdOut,
                    stats = stats.ExitCode == 0 ? stats.StdOut : string.Empty,
                    error = status.ExitCode == 0 ? string.Empty : "Docker status failed."
                }
            });
        });

        app.MapGet("/api/users", async (
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            string? search,
            int? from,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var page = await client.ListUsersAsync(
                session.AccessToken,
                search,
                from ?? 0,
                limit ?? 100,
                cancellationToken);
            return page is null
                ? Results.Json(new { error = "Synapse user directory request failed." }, statusCode: 502)
                : Results.Ok(page);
        });

        app.MapPost("/api/users", async (
            CreateUserRequest request,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.CreateOrUpdateUserAsync(
                session.AccessToken,
                request.Username,
                new UserMutation(request.Password, request.DisplayName, request.Admin),
                requireNew: true,
                cancellationToken);
            return Operation(result);
        });

        app.MapPost("/api/users/batch", async (
            BatchUsersRequest request,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            if (request.Count is < 1 or > 200 || request.Start < 0 || request.Padding is < 0 or > 8)
            {
                return Results.BadRequest(new { error = "Batch count must be 1-200; start must be non-negative; padding must be 0-8." });
            }

            var results = new List<BatchUserResult>(request.Count);
            for (var offset = 0; offset < request.Count; offset++)
            {
                var suffix = (request.Start + offset).ToString(
                    request.Padding == 0 ? "0" : new string('0', request.Padding));
                var username = request.Prefix + suffix;
                var result = await client.CreateOrUpdateUserAsync(
                    session.AccessToken,
                    username,
                    new UserMutation(request.Password, null, request.Admin),
                    requireNew: true,
                    cancellationToken);
                results.Add(new(username, result.Success, result.Error));
            }
            return Results.Ok(new
            {
                requested = request.Count,
                created = results.Count(result => result.Success),
                results
            });
        });

        app.MapPut("/api/users/{username}", async (
            string username,
            UpdateUserRequest request,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.CreateOrUpdateUserAsync(
                session.AccessToken,
                username,
                new UserMutation(
                    request.Password,
                    request.DisplayName,
                    request.Admin,
                    request.Deactivated,
                    request.Locked,
                    request.LogoutDevices),
                requireNew: false,
                cancellationToken);
            return Operation(result);
        });

        app.MapGet("/api/rooms", async (
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            string? search,
            int? from,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var page = await client.ListRoomsAsync(
                session.AccessToken,
                search,
                from ?? 0,
                limit ?? 100,
                cancellationToken);
            return page is null
                ? Results.Json(new { error = "Synapse room directory request failed." }, statusCode: 502)
                : Results.Ok(page);
        });

        app.MapPost("/api/rooms", async (
            CreateRoomRequest request,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.CreateRoomAsync(
                session.AccessToken,
                new RoomCreation(request.Name, request.Topic, request.Invite),
                cancellationToken);
            return Operation(result);
        });

        app.MapGet("/api/rooms/{roomId}/members", async (
            string roomId,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var members = await client.ListRoomMembersAsync(
                session.AccessToken,
                roomId,
                cancellationToken);
            return members is null
                ? Results.Json(new { error = "Room member request failed." }, statusCode: 502)
                : Results.Ok(members);
        });

        app.MapPost("/api/rooms/{roomId}/members", async (
            string roomId,
            RoomMemberRequest request,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.TakeControlAndJoinAsync(
                session.AccessToken,
                session.UserId,
                roomId,
                request.UserId,
                cancellationToken);
            return Operation(result);
        });

        app.MapDelete("/api/rooms/{roomId}/members/{userId}", async (
            string roomId,
            string userId,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.TakeControlAndKickAsync(
                session.AccessToken,
                session.UserId,
                roomId,
                userId,
                cancellationToken);
            return Operation(result);
        });

        app.MapDelete("/api/rooms/{roomId}", async (
            string roomId,
            HttpContext context,
            OperatorSessions sessions,
            SynapseAdminClient client,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out var session)) return Results.Unauthorized();
            var result = await client.DeleteRoomAsync(
                session.AccessToken,
                roomId,
                cancellationToken);
            return Operation(result);
        });

        app.MapGet("/api/logs", async (
            HttpContext context,
            OperatorSessions sessions,
            DockerRuntime docker,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            var logs = await docker.GetLogsAsync(cancellationToken: cancellationToken);
            return logs.ExitCode == 0
                ? Results.Text(logs.StdOut, "text/plain")
                : Results.Json(new { error = "Docker logs failed." }, statusCode: 502);
        });

        app.MapPost("/api/services/action", async (
            ServiceActionRequest request,
            HttpContext context,
            OperatorSessions sessions,
            DockerRuntime docker,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            try
            {
                var result = await docker.ControlServiceAsync(
                    request.Service,
                    request.Action,
                    cancellationToken);
                return result.ExitCode == 0 && !result.TimedOut
                    ? Results.Ok(new { service = request.Service, action = request.Action })
                    : Results.Json(
                        new { error = $"{request.Action} of {request.Service} failed." },
                        statusCode: 502);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/stack/action", async (
            StackActionRequest request,
            HttpContext context,
            OperatorSessions sessions,
            DockerRuntime docker,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            var result = request.Action switch
            {
                "start" => await docker.UpAsync(cancellationToken),
                "stop" => await docker.StopAsync(cancellationToken),
                "restart" => await docker.RestartAsync(cancellationToken),
                _ => null
            };
            if (result is null)
            {
                return Results.BadRequest(new { error = "Unknown stack action." });
            }
            return result.ExitCode == 0 && !result.TimedOut
                ? Results.Ok(new { action = request.Action })
                : Results.Json(new { error = $"Stack {request.Action} failed." }, statusCode: 502);
        });

        app.MapGet("/api/update", async (
            HttpContext context,
            OperatorSessions sessions,
            GithubReleaseClient updates,
            bool? includePrereleases,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            var result = await updates.CheckAsync(
                ProductInfo.Version,
                UpdatePlatform.Detect(),
                includePrereleases ?? false,
                cancellationToken);
            return Results.Ok(result);
        });

        app.MapGet("/api/update/status", (
            HttpContext context,
            OperatorSessions sessions,
            UpdateManager updates) =>
        {
            return TrySession(context, sessions, out _)
                ? Results.Ok(updates.GetStatus())
                : Results.Unauthorized();
        });

        app.MapPost("/api/update/download", (
            HttpContext context,
            OperatorSessions sessions,
            UpdateManager updates,
            bool? includePrereleases) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            return Results.Accepted(value: updates.StartDownload(includePrereleases ?? false));
        });

        app.MapPost("/api/update/install", async (
            HttpContext context,
            OperatorSessions sessions,
            UpdateManager updates,
            CancellationToken cancellationToken) =>
        {
            if (!TrySession(context, sessions, out _)) return Results.Unauthorized();
            var result = await updates.StartInstallAsync(cancellationToken);
            if (!string.Equals(result.State, "installing", StringComparison.Ordinal))
            {
                return Results.Json(new { error = result.Message }, statusCode: 409);
            }

            if (OperatingSystem.IsWindows())
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    app.Lifetime.StopApplication();
                });
            }
            return Results.Accepted(value: result);
        });

        app.MapFallbackToFile("index.html");
        await app.RunAsync();
        return 0;
    }

    private static async Task<int> VerifyUpdateAsync(string archivePath, string signaturePath)
    {
        try
        {
            var verified = await ReleaseSignatureVerifier.VerifyAsync(
                archivePath,
                signaturePath);
            Console.WriteLine(verified
                ? "Axon release signature verified."
                : "Axon release signature verification failed.");
            return verified ? 0 : 3;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Console.Error.WriteLine($"Axon release signature verification failed: {exception.Message}");
            return 3;
        }
    }

    private static async Task<int> RenderRuntimeAsync(string[] args)
    {
        try
        {
            var command = RuntimeRenderCommand.Parse(args);
            await command.ExecuteAsync(new RuntimeRenderer(new SecretGenerator()));
            Console.WriteLine("Axon runtime configuration rendered.");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }
        return null;
    }

    private static bool TrySession(
        HttpContext context,
        OperatorSessions sessions,
        out OperatorSession session)
    {
        context.Request.Cookies.TryGetValue(OperatorSessions.CookieName, out var key);
        return sessions.TryGet(key, out session);
    }

    private static IResult Operation(AdminOperationResult result)
    {
        return result.Success
            ? Results.Ok(new { success = true })
            : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
    }

    private static IResult Operation(AdminValueResult result)
    {
        return result.Success
            ? Results.Ok(new { success = true, value = result.Value })
            : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
    }
}
