using Axon.Control.Security;

namespace Axon.Control.Tests.Security;

public sealed class OperatorSessionsTests
{
    [Fact]
    public void Session_keys_are_random_removable_and_do_not_equal_the_access_token()
    {
        var sessions = new OperatorSessions();

        var first = sessions.Create("@admin:axon.home.arpa", "secret-token");
        var second = sessions.Create("@admin:axon.home.arpa", "secret-token");

        Assert.NotEqual(first, second);
        Assert.NotEqual("secret-token", first);
        Assert.True(sessions.TryGet(first, out var session));
        Assert.Equal("secret-token", session.AccessToken);
        sessions.Remove(first);
        Assert.False(sessions.TryGet(first, out _));
    }
}
