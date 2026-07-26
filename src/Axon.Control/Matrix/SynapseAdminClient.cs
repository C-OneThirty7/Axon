using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axon.Control.Matrix;

public sealed record AdminSessionResult(
    bool Success,
    string Error,
    string? UserId = null,
    string? AccessToken = null);

public sealed record AxonUser(
    string Name,
    string? DisplayName,
    bool Admin,
    bool Deactivated,
    bool Locked,
    long CreationTimestamp,
    long? LastSeenTimestamp);

public sealed record UserPage(
    IReadOnlyList<AxonUser> Users,
    int Total,
    string? NextToken);

public sealed record UserMutation(
    string? Password = null,
    string? DisplayName = null,
    bool? Admin = null,
    bool? Deactivated = null,
    bool? Locked = null,
    bool LogoutDevices = true);

public sealed record AdminOperationResult(bool Success, string Error, int StatusCode = 200);

public sealed record AdminValueResult(
    bool Success,
    string Error,
    string? Value = null,
    int StatusCode = 200);

public sealed record AxonRoom(
    string RoomId,
    string Name,
    string? CanonicalAlias,
    int JoinedMembers,
    int JoinedLocalMembers,
    string Creator,
    bool Encrypted,
    bool Public,
    string JoinRules);

public sealed record RoomPage(
    IReadOnlyList<AxonRoom> Rooms,
    int Total,
    int Offset,
    string? NextToken);

public sealed record RoomMembers(
    IReadOnlyList<string> Members,
    int Total);

public sealed record RoomCreation(
    string Name,
    string? Topic,
    IReadOnlyList<string> Invite);

public sealed partial class SynapseAdminClient(HttpClient httpClient)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    public async Task<AdminSessionResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new(false, "Username and password are required.");
        }

        var loginPayload = new
        {
            type = "m.login.password",
            identifier = new { type = "m.id.user", user = username },
            password
        };

        using var timeout = LinkedTimeout(cancellationToken);
        try
        {
            using var loginResponse = await httpClient.PostAsJsonAsync(
                "/_matrix/client/v3/login",
                loginPayload,
                timeout.Token);
            if (!loginResponse.IsSuccessStatusCode)
            {
                return new(false, "Matrix administrator credentials were rejected.");
            }

            var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: timeout.Token);
            var accessToken = login.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;
            var userId = login.TryGetProperty("user_id", out var userElement)
                ? userElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
            {
                return new(false, "Synapse returned an invalid login response.");
            }

            using var request = Authorized(
                HttpMethod.Get,
                $"/_synapse/admin/v2/users/{Uri.EscapeDataString(userId)}",
                accessToken);
            using var adminResponse = await httpClient.SendAsync(request, timeout.Token);
            if (adminResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                return new(false, "This Matrix account is not a server administrator.");
            }
            if (!adminResponse.IsSuccessStatusCode)
            {
                return new(false, $"Synapse administrator check returned HTTP {(int)adminResponse.StatusCode}.");
            }

            var admin = await adminResponse.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: timeout.Token);
            if (!admin.TryGetProperty("admin", out var adminElement) || !adminElement.GetBoolean())
            {
                return new(false, "This Matrix account is not a server administrator.");
            }
            return new(true, string.Empty, userId, accessToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Synapse administrator login timed out.");
        }
        catch (HttpRequestException)
        {
            return new(false, "Synapse is unavailable.");
        }
        catch (JsonException)
        {
            return new(false, "Synapse returned an invalid response.");
        }
    }

    public async Task<UserPage?> ListUsersAsync(
        string accessToken,
        string? search,
        int from,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var path = $"/_synapse/admin/v3/users?from={Math.Max(0, from)}&limit={Math.Clamp(limit, 1, 200)}&guests=false&deactivated=false";
        if (!string.IsNullOrWhiteSpace(search))
        {
            path += $"&name={Uri.EscapeDataString(search.Trim())}";
        }

        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(HttpMethod.Get, path, accessToken);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: timeout.Token);
        var users = payload.GetProperty("users").EnumerateArray()
            .Select(ParseUser)
            .ToArray();
        var total = payload.TryGetProperty("total", out var totalElement)
            ? totalElement.GetInt32()
            : users.Length;
        var next = payload.TryGetProperty("next_token", out var nextElement)
            ? nextElement.GetString()
            : null;
        return new(users, total, next);
    }

    public async Task<AdminOperationResult> CreateOrUpdateUserAsync(
        string accessToken,
        string localpart,
        UserMutation mutation,
        bool requireNew,
        CancellationToken cancellationToken = default)
    {
        if (!LocalpartPattern().IsMatch(localpart))
        {
            return new(false, "Username must use 1-64 lowercase letters, numbers, dots, underscores, equals signs, or hyphens.", 400);
        }
        if (mutation.Password is { Length: > 0 and < 10 or > 256 })
        {
            return new(false, "Password must contain 10-256 characters.", 400);
        }

        var userId = $"@{localpart}:{ProductInfo.ServerName}";
        using var timeout = LinkedTimeout(cancellationToken);
        if (requireNew)
        {
            using var existingRequest = Authorized(
                HttpMethod.Get,
                $"/_synapse/admin/v2/users/{Uri.EscapeDataString(userId)}",
                accessToken);
            using var existing = await httpClient.SendAsync(existingRequest, timeout.Token);
            if (existing.IsSuccessStatusCode)
            {
                return new(false, $"{userId} already exists.", 409);
            }
            if (existing.StatusCode != HttpStatusCode.NotFound)
            {
                return new(false, $"Could not confirm username availability (HTTP {(int)existing.StatusCode}).", (int)existing.StatusCode);
            }
        }

        var body = new Dictionary<string, object?>();
        if (mutation.Password is not null) body["password"] = mutation.Password;
        if (mutation.DisplayName is not null) body["displayname"] = mutation.DisplayName;
        if (mutation.Admin is not null) body["admin"] = mutation.Admin;
        if (mutation.Deactivated is not null) body["deactivated"] = mutation.Deactivated;
        if (mutation.Locked is not null) body["locked"] = mutation.Locked;
        body["logout_devices"] = mutation.LogoutDevices;

        using var request = Authorized(
            HttpMethod.Put,
            $"/_synapse/admin/v2/users/{Uri.EscapeDataString(userId)}",
            accessToken);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        return response.IsSuccessStatusCode
            ? new(true, string.Empty, (int)response.StatusCode)
            : new(false, $"Synapse returned HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
    }

    public async Task<RoomPage?> ListRoomsAsync(
        string accessToken,
        string? search,
        int from,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var path = $"/_synapse/admin/v1/rooms?from={Math.Max(0, from)}&limit={Math.Clamp(limit, 1, 200)}&order_by=name&dir=f";
        if (!string.IsNullOrWhiteSpace(search))
        {
            path += $"&search_term={Uri.EscapeDataString(search.Trim())}";
        }

        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(HttpMethod.Get, path, accessToken);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: timeout.Token);
        var rooms = payload.GetProperty("rooms").EnumerateArray()
            .Select(ParseRoom)
            .ToArray();
        var total = payload.TryGetProperty("total_rooms", out var totalElement)
            ? totalElement.GetInt32()
            : rooms.Length;
        var offset = payload.TryGetProperty("offset", out var offsetElement)
            ? offsetElement.GetInt32()
            : Math.Max(0, from);
        var next = payload.TryGetProperty("next_batch", out var nextBatch)
            ? nextBatch.ToString()
            : payload.TryGetProperty("next_token", out var nextToken)
                ? nextToken.ToString()
                : null;
        return new(rooms, total, offset, next);
    }

    public async Task<RoomMembers?> ListRoomMembersAsync(
        string accessToken,
        string roomId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(
            HttpMethod.Get,
            $"/_synapse/admin/v1/rooms/{Uri.EscapeDataString(roomId)}/members",
            accessToken);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: timeout.Token);
        var members = payload.GetProperty("members").EnumerateArray()
            .Select(member => member.GetString() ?? string.Empty)
            .Where(member => member.Length > 0)
            .ToArray();
        var total = payload.TryGetProperty("total", out var totalElement)
            ? totalElement.GetInt32()
            : members.Length;
        return new(members, total);
    }

    public async Task<AdminValueResult> CreateRoomAsync(
        string accessToken,
        RoomCreation room,
        CancellationToken cancellationToken = default)
    {
        var name = room.Name.Trim();
        if (name.Length is < 1 or > 128)
        {
            return new(false, "Room name must contain 1-128 characters.", StatusCode: 400);
        }
        if (room.Topic is { Length: > 1024 })
        {
            return new(false, "Room topic must contain at most 1024 characters.", StatusCode: 400);
        }
        var invite = room.Invite
            .Select(NormalizeLocalUserId)
            .Where(userId => userId is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (invite.Length != room.Invite.Count)
        {
            return new(false, "Every invited account must be a valid local Axon user ID.", StatusCode: 400);
        }

        var body = new
        {
            name,
            topic = string.IsNullOrWhiteSpace(room.Topic) ? null : room.Topic.Trim(),
            preset = "private_chat",
            visibility = "private",
            invite,
            creation_content = new Dictionary<string, object> { ["m.federate"] = false },
            initial_state = new[]
            {
                new
                {
                    type = "m.room.encryption",
                    state_key = "",
                    content = new { algorithm = "m.megolm.v1.aes-sha2" }
                }
            }
        };

        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(HttpMethod.Post, "/_matrix/client/v3/createRoom", accessToken);
        request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return await FailedValueAsync(response, "Room creation failed.", timeout.Token);
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: timeout.Token);
        var roomId = payload.TryGetProperty("room_id", out var roomElement)
            ? roomElement.GetString()
            : null;
        return string.IsNullOrWhiteSpace(roomId)
            ? new(false, "Synapse returned no room ID.", StatusCode: 502)
            : new(true, string.Empty, roomId);
    }

    public async Task<AdminOperationResult> JoinRoomMemberAsync(
        string accessToken,
        string roomId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var localUserId = NormalizeLocalUserId(userId);
        if (localUserId is null)
        {
            return new(false, "Only valid local Axon users can be added to rooms.", 400);
        }

        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(
            HttpMethod.Post,
            $"/_synapse/admin/v1/join/{Uri.EscapeDataString(roomId)}",
            accessToken);
        request.Content = JsonContent.Create(new { user_id = localUserId });
        using var response = await httpClient.SendAsync(request, timeout.Token);
        return await OperationAsync(response, "Adding the room member failed.", timeout.Token);
    }

    public async Task<AdminOperationResult> TakeControlAndJoinAsync(
        string accessToken,
        string operatorUserId,
        string roomId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var control = await EnsureRoomControlAsync(
            accessToken,
            operatorUserId,
            roomId,
            cancellationToken);
        return control.Success
            ? await JoinRoomMemberAsync(accessToken, roomId, userId, cancellationToken)
            : control;
    }

    public async Task<AdminOperationResult> TakeControlAndKickAsync(
        string accessToken,
        string operatorUserId,
        string roomId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var localUserId = NormalizeLocalUserId(userId);
        if (localUserId is null)
        {
            return new(false, "Only valid local Axon users can be removed from rooms.", 400);
        }
        if (string.Equals(localUserId, operatorUserId, StringComparison.Ordinal))
        {
            return new(false, "The current operator cannot remove their own room control session.", 400);
        }

        var control = await EnsureRoomControlAsync(
            accessToken,
            operatorUserId,
            roomId,
            cancellationToken);
        if (!control.Success)
        {
            return control;
        }

        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(
            HttpMethod.Post,
            $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/kick",
            accessToken);
        request.Content = JsonContent.Create(new
        {
            user_id = localUserId,
            reason = "Removed by the Axon server administrator."
        });
        using var response = await httpClient.SendAsync(request, timeout.Token);
        return await OperationAsync(response, "Removing the room member failed.", timeout.Token);
    }

    public async Task<AdminValueResult> DeleteRoomAsync(
        string accessToken,
        string roomId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = LinkedTimeout(cancellationToken);
        using var request = Authorized(
            HttpMethod.Delete,
            $"/_synapse/admin/v2/rooms/{Uri.EscapeDataString(roomId)}",
            accessToken);
        request.Content = JsonContent.Create(new
        {
            block = true,
            purge = true,
            force_purge = false
        });
        using var response = await httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return await FailedValueAsync(response, "Room deletion failed.", timeout.Token);
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: timeout.Token);
        var deleteId = payload.TryGetProperty("delete_id", out var deleteElement)
            ? deleteElement.GetString()
            : null;
        return new(true, string.Empty, deleteId, (int)response.StatusCode);
    }

    private async Task<AdminOperationResult> EnsureRoomControlAsync(
        string accessToken,
        string operatorUserId,
        string roomId,
        CancellationToken cancellationToken)
    {
        using var timeout = LinkedTimeout(cancellationToken);
        using (var grant = Authorized(
            HttpMethod.Post,
            $"/_synapse/admin/v1/rooms/{Uri.EscapeDataString(roomId)}/make_room_admin",
            accessToken))
        {
            grant.Content = JsonContent.Create(new { user_id = operatorUserId });
            using var grantResponse = await httpClient.SendAsync(grant, timeout.Token);
            var grantResult = await OperationAsync(
                grantResponse,
                "Axon could not grant the operator room-level control.",
                timeout.Token);
            if (!grantResult.Success) return grantResult;
        }

        using var join = Authorized(
            HttpMethod.Post,
            $"/_matrix/client/v3/join/{Uri.EscapeDataString(roomId)}",
            accessToken);
        join.Content = JsonContent.Create(new { });
        using var joinResponse = await httpClient.SendAsync(join, timeout.Token);
        return await OperationAsync(
            joinResponse,
            "Axon granted room control but the operator could not join the room.",
            timeout.Token);
    }

    private static async Task<AdminOperationResult> OperationAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return new(true, string.Empty, (int)response.StatusCode);
        }
        var error = await ReadMatrixErrorAsync(response, fallback, cancellationToken);
        return new(false, error, (int)response.StatusCode);
    }

    private static async Task<AdminValueResult> FailedValueAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var error = await ReadMatrixErrorAsync(response, fallback, cancellationToken);
        return new(false, error, StatusCode: (int)response.StatusCode);
    }

    private static async Task<string> ReadMatrixErrorAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            if (payload.TryGetProperty("error", out var error) &&
                !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }
        }
        catch (JsonException)
        {
        }
        return $"{fallback} Synapse returned HTTP {(int)response.StatusCode}.";
    }

    private static string? NormalizeLocalUserId(string userId)
    {
        var candidate = userId.Trim();
        if (LocalpartPattern().IsMatch(candidate))
        {
            return $"@{candidate}:{ProductInfo.ServerName}";
        }
        return LocalUserIdPattern().IsMatch(candidate) ? candidate : null;
    }

    private static AxonUser ParseUser(JsonElement user) => new(
        user.GetProperty("name").GetString() ?? string.Empty,
        user.TryGetProperty("displayname", out var display) && display.ValueKind != JsonValueKind.Null
            ? display.GetString()
            : null,
        user.TryGetProperty("admin", out var admin) && admin.GetBoolean(),
        user.TryGetProperty("deactivated", out var deactivated) && deactivated.GetBoolean(),
        user.TryGetProperty("locked", out var locked) && locked.GetBoolean(),
        user.TryGetProperty("creation_ts", out var created) ? created.GetInt64() : 0,
        user.TryGetProperty("last_seen_ts", out var lastSeen) && lastSeen.ValueKind != JsonValueKind.Null
            ? lastSeen.GetInt64()
            : null);

    private static AxonRoom ParseRoom(JsonElement room) => new(
        room.GetProperty("room_id").GetString() ?? string.Empty,
        room.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.Null
            ? name.GetString() ?? "Unnamed room"
            : "Unnamed room",
        room.TryGetProperty("canonical_alias", out var alias) && alias.ValueKind != JsonValueKind.Null
            ? alias.GetString()
            : null,
        room.TryGetProperty("joined_members", out var joined) ? joined.GetInt32() : 0,
        room.TryGetProperty("joined_local_members", out var local) ? local.GetInt32() : 0,
        room.TryGetProperty("creator", out var creator) ? creator.GetString() ?? string.Empty : string.Empty,
        room.TryGetProperty("encryption", out var encryption) && encryption.ValueKind != JsonValueKind.Null,
        room.TryGetProperty("public", out var isPublic) && isPublic.GetBoolean(),
        room.TryGetProperty("join_rules", out var rules) && rules.ValueKind != JsonValueKind.Null
            ? rules.GetString() ?? "unknown"
            : "unknown");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static CancellationTokenSource LinkedTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        return timeout;
    }

    [GeneratedRegex("^[a-z0-9._=-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalpartPattern();

    [GeneratedRegex("^@[a-z0-9._=-]{1,64}:axon\\.home\\.arpa$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalUserIdPattern();
}
