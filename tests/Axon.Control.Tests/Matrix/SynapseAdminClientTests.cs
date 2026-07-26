using System.Net;
using System.Text;
using System.Text.Json;
using Axon.Control.Matrix;

namespace Axon.Control.Tests.Matrix;

public sealed class SynapseAdminClientTests
{
    [Fact]
    public async Task Administrator_login_is_verified_without_exposing_the_password()
    {
        var handler = new AdminHandler();
        var client = new SynapseAdminClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8008")
        });

        var result = await client.LoginAsync("operator", "example-password");

        Assert.True(result.Success);
        Assert.Equal("@operator:axon.home.arpa", result.UserId);
        Assert.Equal("admin-token", result.AccessToken);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("admin-token", handler.Requests[1].AuthorizationValue);
    }

    [Fact]
    public async Task Non_administrator_is_rejected()
    {
        var handler = new AdminHandler { IsAdmin = false };
        var client = new SynapseAdminClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8008")
        });

        var result = await client.LoginAsync("operator", "example-password");

        Assert.False(result.Success);
        Assert.Contains("not a server administrator", result.Error);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task Creates_a_new_standard_user_with_a_ten_character_stock_password()
    {
        var handler = new AdminHandler();
        var client = new SynapseAdminClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8008")
        });

        var result = await client.CreateOrUpdateUserAsync(
            "admin-token",
            "user002",
            new UserMutation(Password: "axon-12345", Admin: false),
            requireNew: true);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        var payload = JsonDocument.Parse(handler.Requests[1].Body!).RootElement;
        Assert.Equal("axon-12345", payload.GetProperty("password").GetString());
        Assert.False(payload.GetProperty("admin").GetBoolean());
    }

    [Fact]
    public async Task Existing_batch_username_is_never_overwritten()
    {
        var handler = new AdminHandler { UserExists = true };
        var client = new SynapseAdminClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8008")
        });

        var result = await client.CreateOrUpdateUserAsync(
            "admin-token",
            "user002",
            new UserMutation(Password: "axon-12345"),
            requireNew: true);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Lists_rooms_with_search_and_member_details()
    {
        var handler = new RoomHandler();
        var client = Client(handler);

        var rooms = await client.ListRoomsAsync("admin-token", "ops", 0, 100);
        var members = await client.ListRoomMembersAsync("admin-token", "!ops:axon.home.arpa");

        Assert.NotNull(rooms);
        Assert.Single(rooms.Rooms);
        Assert.Equal("Operations", rooms.Rooms[0].Name);
        Assert.Equal(2, rooms.Rooms[0].JoinedMembers);
        Assert.True(rooms.Rooms[0].Encrypted);
        Assert.Contains("search_term=ops", handler.Requests[0].Path);
        Assert.NotNull(members);
        Assert.Equal(["@member001:axon.home.arpa", "@member002:axon.home.arpa"], members.Members);
    }

    [Fact]
    public async Task Creates_private_encrypted_nonfederated_room_and_invites_local_users()
    {
        var handler = new RoomHandler();
        var client = Client(handler);

        var result = await client.CreateRoomAsync(
            "admin-token",
            new RoomCreation(
                "Operations",
                "Primary room",
                ["@member001:axon.home.arpa", "@member002:axon.home.arpa"]));

        Assert.True(result.Success);
        Assert.Equal("!created:axon.home.arpa", result.Value);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/_matrix/client/v3/createRoom", request.Path);
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("private_chat", body.GetProperty("preset").GetString());
        Assert.False(body.GetProperty("creation_content").GetProperty("m.federate").GetBoolean());
        Assert.Equal(
            "m.megolm.v1.aes-sha2",
            body.GetProperty("initial_state")[0].GetProperty("content").GetProperty("algorithm").GetString());
        Assert.Equal(2, body.GetProperty("invite").GetArrayLength());
    }

    [Fact]
    public async Task Joins_member_and_deletes_room_with_explicit_block_and_purge()
    {
        var handler = new RoomHandler();
        var client = Client(handler);

        var joined = await client.JoinRoomMemberAsync(
            "admin-token",
            "!ops:axon.home.arpa",
            "@user3:axon.home.arpa");
        var deleted = await client.DeleteRoomAsync("admin-token", "!ops:axon.home.arpa");

        Assert.True(joined.Success);
        Assert.True(deleted.Success);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("/_synapse/admin/v1/join/", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        var body = JsonDocument.Parse(handler.Requests[1].Body!).RootElement;
        Assert.True(body.GetProperty("block").GetBoolean());
        Assert.True(body.GetProperty("purge").GetBoolean());
        Assert.False(body.GetProperty("force_purge").GetBoolean());
    }

    [Fact]
    public async Task Removing_member_transparently_grants_operator_room_control_then_kicks()
    {
        var handler = new RoomHandler();
        var client = Client(handler);

        var result = await client.TakeControlAndKickAsync(
            "admin-token",
            "@operator:axon.home.arpa",
            "!ops:axon.home.arpa",
            "@member002:axon.home.arpa");

        Assert.True(result.Success);
        Assert.Collection(
            handler.Requests,
            request => Assert.Contains("/make_room_admin", request.Path),
            request => Assert.Contains("/_matrix/client/v3/join/", request.Path),
            request => Assert.Contains("/kick", request.Path));
    }

    private static SynapseAdminClient Client(HttpMessageHandler handler) => new(new HttpClient(handler)
    {
        BaseAddress = new Uri("http://127.0.0.1:8008")
    });

    private sealed record Recorded(
        HttpMethod Method,
        string Path,
        string? Body,
        string? AuthorizationScheme,
        string? AuthorizationValue);

    private sealed class AdminHandler : HttpMessageHandler
    {
        public bool IsAdmin { get; init; } = true;
        public bool UserExists { get; init; }
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method,
                request.RequestUri!.PathAndQuery,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            if (request.RequestUri.AbsolutePath == "/_matrix/client/v3/login")
            {
                return Json(HttpStatusCode.OK,
                    """{"user_id":"@operator:axon.home.arpa","access_token":"admin-token"}""");
            }
            if (request.Method == HttpMethod.Get &&
                Uri.UnescapeDataString(request.RequestUri.AbsolutePath).Contains("/@operator", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, $$"""{"name":"@operator:axon.home.arpa","admin":{{IsAdmin.ToString().ToLowerInvariant()}}}""");
            }
            if (request.Method == HttpMethod.Get)
            {
                return UserExists
                    ? Json(HttpStatusCode.OK, """{"name":"@user002:axon.home.arpa","admin":false}""")
                    : Json(HttpStatusCode.NotFound, """{"errcode":"M_NOT_FOUND"}""");
            }
            return Json(HttpStatusCode.Created, """{"name":"@user002:axon.home.arpa"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RoomHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method,
                request.RequestUri!.PathAndQuery,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            var path = request.RequestUri.AbsolutePath;
            if (path == "/_synapse/admin/v1/rooms")
            {
                return Json(HttpStatusCode.OK, """
                    {"rooms":[{"room_id":"!ops:axon.home.arpa","name":"Operations","canonical_alias":null,
                    "joined_members":2,"joined_local_members":2,"creator":"@operator:axon.home.arpa",
                    "encryption":"m.megolm.v1.aes-sha2","public":false,"join_rules":"invite"}],
                    "offset":0,"total_rooms":1}
                    """);
            }
            if (path.EndsWith("/members", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """{"members":["@member001:axon.home.arpa","@member002:axon.home.arpa"],"total":2}""");
            }
            if (path == "/_matrix/client/v3/createRoom")
            {
                return Json(HttpStatusCode.OK, """{"room_id":"!created:axon.home.arpa"}""");
            }
            if (path.Contains("/join/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"room_id":"!ops:axon.home.arpa"}""");
            }
            return Json(HttpStatusCode.OK, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
