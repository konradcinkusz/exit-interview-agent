using System.Net;
using ExitInterviewAgent.AppHost.Tests.Support;

namespace ExitInterviewAgent.AppHost.Tests;

/// <summary>The helpers the full-stack scenarios rely on. They need no Docker, so they run in every build (not skipped).</summary>
public sealed class SupportTests
{
    [Fact]
    public void A_set_cookie_header_gives_its_name_and_value_and_ignores_the_attributes()
    {
        var cookie = BffSession.ParseSetCookie("eia_access=abc.def; Path=/; HttpOnly; SameSite=Strict; Max-Age=900");

        Assert.NotNull(cookie);
        Assert.Equal("eia_access", cookie.Name);
        Assert.Equal("abc.def", cookie.Value);
        Assert.False(cookie.Expired);
    }

    [Fact]
    public void A_set_cookie_header_without_a_pair_is_not_a_cookie()
    {
        Assert.Null(BffSession.ParseSetCookie("HttpOnly; Path=/"));
        Assert.Null(BffSession.ParseSetCookie("=orphan-value"));
    }

    [Fact]
    public void A_cleared_cookie_is_removed_from_the_session_as_a_browser_would_remove_it()
    {
        var session = new BffSession();
        Store(session, "eia_access=token-1; Path=/; Max-Age=900");
        Store(session, "eia_consent=terms|privacy; Path=/; Max-Age=900");

        Assert.Equal("token-1", session.AccessToken);
        Store(session, "eia_access=; Path=/; Max-Age=0");

        Assert.Null(session.AccessToken);
        Assert.Equal("eia_consent=terms|privacy", session.CookieHeader);
    }

    [Fact]
    public void The_cookie_header_carries_every_cookie_the_session_holds()
    {
        var session = new BffSession();
        Store(session, "eia_access=a; Path=/");
        Store(session, "eia_refresh=r; Path=/");

        Assert.Equal("eia_access=a; eia_refresh=r", session.CookieHeader);
    }

    [Fact]
    public void The_full_stack_gate_names_the_missing_switch()
    {
        var reason = FullStackFactAttribute.ReasonToSkip(enabled: null, dockerReachable: true);

        Assert.NotNull(reason);
        Assert.StartsWith("NOT RUN:", reason);
        Assert.Contains("FULL_STACK_TESTS=1", reason);
    }

    [Fact]
    public void The_full_stack_gate_names_a_missing_docker_daemon()
    {
        var reason = FullStackFactAttribute.ReasonToSkip(enabled: "1", dockerReachable: false);

        Assert.NotNull(reason);
        Assert.StartsWith("NOT RUN:", reason);
        Assert.Contains("Docker", reason);
    }

    [Fact]
    public void The_full_stack_gate_opens_only_when_both_hold()
    {
        Assert.Null(FullStackFactAttribute.ReasonToSkip(enabled: "1", dockerReachable: true));
        Assert.NotNull(FullStackFactAttribute.ReasonToSkip(enabled: "true", dockerReachable: true));
    }

    private static void Store(BffSession session, string setCookie)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        session.Absorb(response);
    }
}
