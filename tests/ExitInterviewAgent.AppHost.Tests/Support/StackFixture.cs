using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExitInterviewAgent.AppHost.Tests.Support;

/// <summary>
/// One AppHost for the whole test class: started on the first full-stack test, shared by every scenario, stopped at the end.
/// Each scenario uses its own account, so the order of the scenarios does not matter. Nothing starts when the tests are skipped.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    /// <summary>Budget for the whole stack to become healthy (image pulls included). A CI runner pulls three images cold.</summary>
    public static readonly TimeSpan StartBudget = TimeSpan.FromMinutes(12);

    /// <summary>Where the logs of every resource go when a step fails. CI points this at a folder it uploads; a developer gets a temp folder.</summary>
    public static readonly string LogDirectory =
        Environment.GetEnvironmentVariable("FULL_STACK_LOG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Path.GetTempPath(), "exit-interview-agent-full-stack");

    /// <summary>The AppHost resources whose logs are written out on failure.</summary>
    public static readonly string[] Resources = ["postgres", "pgadmin", "authservice", "interview-service", "web"];

    private Task<Stack>? _starting;

    /// <summary>Starts the stack on first use. Called only from tests that have passed the <see cref="FullStackFactAttribute"/> gate.</summary>
    public Task<Stack> StackAsync() => _starting ??= StartAsync();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_starting is null) return;
        try
        {
            var stack = await _starting;
            await stack.DisposeAsync();
        }
        catch (Exception)
        {
            // A failed start has nothing to stop; its error was reported by the test that asked for the stack.
        }
    }

    private static async Task<Stack> StartAsync()
    {
        // The settings the scenarios depend on are fixed here, not left to a developer's user-secrets: the mock model (no key),
        // a credit gate ON (scenario d), and identity on. Development is needed for the dev credit grant (ADR-0079).
        string[] args =
        [
            "--Parameters:interviews-provider=mock",
            "--Parameters:interviews-model=scripted",
            "--Parameters:interviews-require-credit=true",
            "--Identity:Enabled=true",
        ];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ExitInterviewAgent_AppHost>(args,
            (_, settings) => settings.EnvironmentName = "Development");
        var app = await builder.BuildAsync();
        using var budget = new CancellationTokenSource(StartBudget);
        try
        {
            await app.StartAsync(budget.Token);
            var capture = new CancellationTokenSource();
            Stack.CaptureLiveLogs(app, capture.Token);
            var stack = new Stack(app, capture);
            foreach (var name in new[] { "postgres", "authservice", "interview-service", "web" })
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(name, budget.Token);
            }
            await app.ResourceNotifications.WaitForResourceAsync("pgadmin", KnownResourceStates.Running, budget.Token);
            return stack;
        }
        catch (Exception e)
        {
            var report = await Stack.DescribeAsync(app);
            await app.DisposeAsync();
            throw new InvalidOperationException($"The full stack did not start within {StartBudget}: {e.Message}\n{report}", e);
        }
    }
}

/// <summary>The running AppHost and the addresses the scenarios call. Only the resources the tests need are exposed.</summary>
public sealed class Stack(DistributedApplication app, CancellationTokenSource? capture = null) : IAsyncDisposable
{
    public DistributedApplication App { get; } = app;

    /// <summary>The web app's origin. The BFF's Origin check compares it with the Host header, so requests carry this exact value.</summary>
    public Uri Web => App.GetEndpoint("web");

    public Uri Authority => App.GetEndpoint("authservice");

    public Uri InterviewService => App.GetEndpoint("interview-service");

    /// <summary>A client with no cookie jar and no redirects: the test keeps cookies itself (BffSession), as a browser would.</summary>
    public HttpClient NewClient(Uri baseAddress) =>
        new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(2) };

    public async Task<string> InterviewDbConnectionAsync() => await App.GetConnectionStringAsync("interviewdb")
        ?? throw new InvalidOperationException("The AppHost gave no connection string for interviewdb.");

    /// <summary>Runs a scenario step; on failure the message carries each resource's last log lines and the files are written.</summary>
    public async Task DiagnoseOnFailureAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception e)
        {
            var report = await DescribeAsync(App);
            var probes = await ProbeAuthorityAsync();
            throw new InvalidOperationException($"{e.Message}\n{probes}{report}", e);
        }
    }

    /// <summary>
    /// What the test host sees of authservice, probed from inside the test host (ADR-0081): the endpoint the AppHost resolves, and a GET
    /// to the same health path on that endpoint and on the two loopback literals, each with its time. Written into the failure message,
    /// so the job log shows which address answers at test time and which one does not.
    /// </summary>
    public async Task<string> ProbeAuthorityAsync()
    {
        var text = new StringBuilder($"Endpoints the test host resolves: authservice={Authority} web={Web}\n");
        foreach (var target in new[] { Authority, new Uri("http://127.0.0.1:5100/"), new Uri("http://[::1]:5100/") })
        {
            var url = new Uri(target, "health/ready");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var started = DateTime.UtcNow;
            try
            {
                using var response = await client.GetAsync(url);
                text.AppendLine($"probe {url}: HTTP {(int)response.StatusCode} in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            }
            catch (Exception e)
            {
                text.AppendLine($"probe {url}: {e.GetType().Name} ({e.Message}) after {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            }
        }
        return text.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        capture?.Cancel();
        await App.DisposeAsync();
    }

    /// <summary>
    /// The last log lines of every resource, also written to <see cref="StackFixture.LogDirectory"/>. Each resource is read on its
    /// own, and a failure to read one is written in its place, so a missing log never hides the others.
    /// </summary>
    public static async Task<string> DescribeAsync(DistributedApplication app)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        var text = new StringBuilder("Resource logs (last lines):");
        Directory.CreateDirectory(StackFixture.LogDirectory);
        foreach (var name in StackFixture.Resources)
        {
            var file = Path.Combine(StackFixture.LogDirectory, $"{name}.log");
            var lines = new List<string>();
            if (File.Exists(file))
            {
                lines.AddRange(File.ReadAllLines(file));
            }
            else
            {
                try
                {
                    await foreach (var batch in logs.GetAllAsync(name))
                    {
                        lines.AddRange(batch.Select(l => l.Content));
                    }
                }
                catch (Exception e)
                {
                    lines.Add($"(could not read the log: {e.GetType().Name})");
                }
            }
            text.AppendLine().AppendLine($"--- {name} ({lines.Count} lines) ---");
            foreach (var line in lines.TakeLast(60)) text.AppendLine(line);
        }
        return text.ToString();
    }

    /// <summary>
    /// Every resource's log lines, from the moment the stack starts, appended to <c>&lt;LogDirectory&gt;/&lt;resource&gt;.log</c>.
    /// The backlog read alone came back empty in CI, so the live stream is what the failure report and the artifact use.
    /// </summary>
    public static void CaptureLiveLogs(DistributedApplication app, CancellationToken stop)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        Directory.CreateDirectory(StackFixture.LogDirectory);
        foreach (var name in StackFixture.Resources)
        {
            var file = Path.Combine(StackFixture.LogDirectory, $"{name}.log");
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var batch in logs.WatchAsync(name).WithCancellation(stop))
                    {
                        await File.AppendAllLinesAsync(file, batch.Select(l => l.Content), stop);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The stack is stopping; the lines already written stay in the file.
                }
            }, stop);
        }
    }

    /// <summary>The interview database, with a connection that is closed when the caller is done.</summary>
    public async Task<NpgsqlConnection> OpenInterviewDbAsync()
    {
        var connection = new NpgsqlConnection(await InterviewDbConnectionAsync());
        await connection.OpenAsync();
        return connection;
    }
}
