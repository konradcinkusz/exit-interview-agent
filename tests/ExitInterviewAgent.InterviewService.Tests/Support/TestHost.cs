using System.Net.Http.Headers;
using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>A clock a test moves by hand. Timers and delays stay real; only "what time is it" is controlled.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
    public void Set(DateTimeOffset at) => _now = at;
}

/// <summary>
/// The real service in-process with its own store (a private InMemory database, or PostgreSQL when a connection string is
/// given), a controllable clock and optional service overrides. Tokens come from the T2 builders.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly ServiceFactory _root = new();
    private readonly WebApplicationFactory<Program> _app;

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    public IServiceProvider Services => _app.Services;

    public TestHost(
        Dictionary<string, string?>? settings = null,
        string? postgres = null,
        Action<IServiceCollection>? services = null,
        ILoggerProvider? logs = null)
    {
        foreach (var (key, value) in settings ?? [])
        {
            _root.Settings[key] = value;
        }
        if (postgres is not null)
        {
            _root.Settings["DATABASE_PROVIDER"] = "PostgreSQL";
            _root.Settings["ConnectionStrings:interviewdb"] = postgres;
        }
        var memoryName = "t5-" + Guid.NewGuid().ToString("N");
        _app = _root.WithWebHostBuilder(b =>
        {
            if (logs is not null)
            {
                b.UseSetting("Logging:LogLevel:Default", "Trace");
                b.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
                b.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Trace"); // appsettings quiets it; the canary test wants the SQL logs
                b.ConfigureLogging(l => l.AddProvider(logs));
            }
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clock);
                if (postgres is null)
                {
                    s.RemoveAll<IDbContextOptionsConfiguration<InterviewDbContext>>();
                    s.RemoveAll<DbContextOptions<InterviewDbContext>>();
                    s.AddDbContext<InterviewDbContext>(o => o.UseInMemoryDatabase(memoryName));
                }
                services?.Invoke(s);
            });
        });
    }

    public HttpClient Client(string? bearer = null)
    {
        var client = _app.CreateClient();
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        return client;
    }

    /// <summary>A handler that reaches this host without a socket: what the real CLI is given in place of its network handler.</summary>
    public HttpMessageHandler Handler() => _app.Server.CreateHandler();

    public string WebToken(string sub) => _root.NewWebToken(sub).Build();
    public string McpToken(string sub) => _root.NewMcpToken(sub).Build();
    public TokenBuilder NewMcpToken(string sub) => _root.NewMcpToken(sub);

    /// <summary>Runs <paramref name="work"/> in a fresh service scope (its own DbContext).</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Readiness stays unhealthy until the schema hosted service has run; poll rather than sleep.</summary>
    public async Task WaitReadyAsync()
    {
        var client = _app.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if ((await client.GetAsync("/health")).IsSuccessStatusCode)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("The schema did not become ready.");
    }

    public void Dispose()
    {
        _app.Dispose();
        _root.Dispose();
    }
}
