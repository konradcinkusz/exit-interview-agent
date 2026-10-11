using System.Security.Cryptography;
using ExitInterviewAgent.AppHost;
using Microsoft.Extensions.Configuration;

// P1: the composition root. One command brings the system up:
//   dotnet run --project src/ExitInterviewAgent.AppHost
// Development only. Production is described by flyio/*.fly.toml, not by this file.
var builder = DistributedApplication.CreateBuilder(args);

// Dev parameters and secrets. Source order for each: user-secrets (Parameters:<name>, written with `dotnet user-secrets`)
// -> the default below, or a throwaway value generated for this run. Nothing here is a real credential; the full list, with
// the user-secrets commands, is in the AppHost section of secrets.env.example and in the pull request that added it.
string DevSecret(string name, Func<string> generate)
{
    var configured = builder.Configuration[$"Parameters:{name}"];
    return string.IsNullOrWhiteSpace(configured) ? generate() : configured.Trim();
}
string DevSetting(string name, string fallback)
{
    var configured = builder.Configuration[$"Parameters:{name}"];
    return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
}

// Externally-contracted or documented ports do not float (P1). pgAdmin is for the developer's browser only; the web app's port
// is fixed because the payment checkout returns the payer to it (Billing:SuccessUrl), and that address must be known in advance.
const int PgAdminPort = 5050;
const int WebPort = 3000;
var webOrigin = $"http://localhost:{WebPort}";

// One Postgres server, one logical database per service (P3). Dev-only: both databases share the
// server's superuser; a deployment uses one role per database (flyio/SECRETS.md).
// Same major as flyio/postgres.fly.toml (postgres:17-alpine), so development and deployment do not drift.
// pgAdmin is part of this development stack only (never on Fly). Its server entry is registered automatically by Aspire.
var pgAdminPassword = builder.AddParameter("pgadmin-password", DevSecret("pgadmin-password", () => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()), secret: true);
var postgres = builder.AddPostgres("postgres").WithImageTag("17-alpine").WithDataVolume("exit-interview-agent-pgdata")
    .WithPgAdmin(pgAdmin => pgAdmin
        .WithHostPort(PgAdminPort)
        .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", pgAdminPassword));
var interviewDb = postgres.AddDatabase("interviewdb");

// The interview model (ADR-0079). With an Anthropic key the service talks to the provider; without one it runs the scripted mock,
// so the stack starts and the interview flow works offline. /health and the startup banner say which one is running.
var anthropicKey = builder.Configuration["Parameters:anthropic-api-key"];
var hasKey = !string.IsNullOrWhiteSpace(anthropicKey);
var interviewsProvider = builder.AddParameter("interviews-provider", DevSetting("interviews-provider", hasKey ? "anthropic" : "mock"));
// The mock ignores the model, but the resolver requires one: "scripted" is the placeholder, not a model. A real provider needs its model set (no default in code).
var interviewsModel = builder.AddParameter("interviews-model", DevSetting("interviews-model", hasKey ? "" : "scripted"));
var requireCredit = builder.AddParameter("interviews-require-credit", DevSetting("interviews-require-credit", "false"));

// Development payments (ADR-0079): the fake provider, no money, a dev price and currency, and a generated webhook secret.
var billingPrice = builder.AddParameter("billing-price-minor-units", DevSetting("billing-price-minor-units", "1000"));
var billingCurrency = builder.AddParameter("billing-currency", DevSetting("billing-currency", "pln"));
var fakeWebhookSecret = builder.AddParameter("billing-fake-webhook-secret", DevSecret("billing-fake-webhook-secret", () => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()), secret: true);

var interviewService = builder.AddProject<Projects.ExitInterviewAgent_InterviewService>("interview-service")
    .WithReference(interviewDb)
    .WaitFor(interviewDb)
    .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
    .WithEnvironment("Jwt__Issuer", "ExitInterviewAgent")
    .WithEnvironment("Jwt__Audience", "ExitInterviewAgent")
    // Development: authservice does not yet issue email_verified (L4), so the verified-email gate is off here. Also in appsettings.Development.json.
    .WithEnvironment("Interviews__RequireVerifiedEmail", "false")
    .WithEnvironment("Interviews__Provider", interviewsProvider)
    .WithEnvironment("Interviews__Model", interviewsModel)
    .WithEnvironment("Interviews__RequireCredit", requireCredit)  // false: starts are free. true: a start needs a credit (grant one with POST /api/v1/dev/grant-credits).
    .WithEnvironment("Billing__Provider", "fake")
    .WithEnvironment("Billing__PriceMinorUnits", billingPrice)
    .WithEnvironment("Billing__Currency", billingCurrency)
    .WithEnvironment("Billing__SuccessUrl", $"{webOrigin}/interview")
    .WithEnvironment("Billing__CancelUrl", $"{webOrigin}/interview")
    .WithEnvironment("Billing__FakeWebhookSecret", fakeWebhookSecret)
    .WithHttpHealthCheck("/health");
if (hasKey)
{
    // The NAME of the variable, never the key. Set only with a key: the provider resolver requires a named key variable to be
    // set, so a name without a key refuses even the offline mock (every start answered 503 interviews_disabled, ADR-0081).
    interviewService.WithEnvironment("Interviews__ApiKeyEnv", "ANTHROPIC_API_KEY");
    interviewService.WithEnvironment("ANTHROPIC_API_KEY", builder.AddParameter("anthropic-api-key", anthropicKey!.Trim(), secret: true));
}
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:anthropic-workspace-id"]))
{
    // Not a secret: an id that the provider's console shows. Read only by the anthropic provider.
    interviewService.WithEnvironment("ANTHROPIC_WORKSPACE_ID", builder.AddParameter("anthropic-workspace-id", builder.Configuration["Parameters:anthropic-workspace-id"]!.Trim()));
}

// Relative to the AppHost project, not the working directory. The script is `dev` for a developer; the full-stack test sets
// Web:Script=start after a production build, because a first `next dev` compile is slow and not what a CI run should measure (ADR-0081).
var webScript = string.IsNullOrWhiteSpace(builder.Configuration["Web:Script"]) ? "dev" : builder.Configuration["Web:Script"]!.Trim();
var web = builder.AddJavaScriptApp("web", Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../web/app")), webScript)
    .WithPnpm()
    .WithHttpEndpoint(port: WebPort, env: "PORT")
    .WithReference(interviewService)
    .WaitFor(interviewService)
    .WithEnvironment("AUTH_ISSUER", "ExitInterviewAgent")
    .WithEnvironment("AUTH_AUDIENCE", "ExitInterviewAgent")
    .WithEnvironment("SESSION_COOKIE_SECURE", "false") // plain-http localhost only
    .WithEnvironment("DEV_HINTS", "1") // the sign-in page shows the DevSeed test account; this resource only, never a deployment
    .WithHttpHealthCheck("/healthz");

// Identity (P5, shared-service-reuse): authservice runs as a SEPARATE instance of the published,
// version-pinned image: its own database, its own signing key, never this repository's code, never
// ":latest". Optional (P8): Identity:Enabled=false skips it (for example where ghcr.io blobs are
// unreachable) and the stack runs with protected endpoints answering 401.
if (builder.Configuration.GetValue("Identity:Enabled", true))
{
    const string AuthserviceImage = "ghcr.io/konradcinkusz/authservice";
    const string AuthserviceTag = "v0.3.4";      // pinned; bump deliberately via an ADR note
    const int AuthservicePort = 5100;            // externally contracted (it is the JWKS/issuer address): does not float

    var authDb = postgres.AddDatabase("authdb");

    // DEV-ONLY secrets: see DevSecret above. They are held by the authservice container and nothing else.
    var signingKey = builder.AddParameter("authservice-jwt-private-key", DevSecret("authservice-jwt-private-key", () =>
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }), secret: true);

    var authservice = builder.AddContainer("authservice", AuthserviceImage, AuthserviceTag)
        .WithHttpEndpoint(port: AuthservicePort, targetPort: 8080, name: "http")
        .WithReference(authDb)
        .WaitFor(authDb)
        .WithEnvironment("ConnectionStrings__DefaultConnection", authDb.Resource.ConnectionStringExpression)
        .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
        .WithEnvironment("Database__SchemaMode", "Migrate")
        .WithEnvironment("Database__MigrationsAssembly", "AuthService.Migrations.PostgreSQL")
        .WithEnvironment("Jwt__PrivateKeyPem", signingKey)
        .WithEnvironment("Jwt__Issuer", "ExitInterviewAgent")
        .WithEnvironment("Jwt__Audience", "ExitInterviewAgent")
        .WithHttpHealthCheck("/health/ready");

    // MCP (Claude connector) path, ADR-0012. authservice only becomes an OAuth authorization server when a client is
    // configured, and it insists on https for its issuer and for every resource, so this needs two public https
    // URLs (a tunnel or a dev domain in front of ports 5100 and 5200). Neither can be invented here: without both,
    // the MCP path is simply off, the rest of the stack is unchanged and /health says mcp-auth is not configured (P8).
    //   Mcp:AuthPublicBaseUrl  https origin that reaches authservice (= its Jwt:PublicBaseUrl, the MCP token issuer)
    //   Mcp:ResourceUrl        https URL of the MCP endpoint on interview-service, with a path, for example <origin>/mcp
    var mcpIssuer = builder.Configuration["Mcp:AuthPublicBaseUrl"]?.Trim().TrimEnd('/');
    var mcpResource = builder.Configuration["Mcp:ResourceUrl"]?.Trim().TrimEnd('/');
    if (!string.IsNullOrEmpty(mcpIssuer) && !string.IsNullOrEmpty(mcpResource))
    {
        var clientSecret = builder.AddParameter("authservice-mcp-client-secret",
            DevSecret("authservice-mcp-client-secret", () => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()),
            secret: true);
        var encryptionKey = builder.AddParameter("authservice-encryption-key",
            DevSecret("authservice-encryption-key", () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            secret: true);

        authservice
            .WithEnvironment("Jwt__PublicBaseUrl", mcpIssuer)
            .WithEnvironment("App__Name", "Exit Interview Agent")
            .WithEnvironment("Network__TrustAllProxies", "true") // dev only: TLS ends at the tunnel in front of port 5100
            .WithEnvironment("AuthorizationServer__EncryptionKey", encryptionKey)
            // The one client: Claude (web, desktop and mobile share this callback). Confidential: the id and the
            // secret are entered under "Advanced settings" when the connector is added. The id is a public label.
            .WithEnvironment("AuthorizationServer__Clients__0__ClientId", "claude-exit-interview-dev")
            .WithEnvironment("AuthorizationServer__Clients__0__DisplayName", "Claude")
            .WithEnvironment("AuthorizationServer__Clients__0__ClientSecret", clientSecret)
            .WithEnvironment("AuthorizationServer__Clients__0__RedirectUris__0", "https://claude.ai/api/mcp/auth_callback")
            .WithEnvironment("AuthorizationServer__Clients__0__AllowedScopes__0", "interview:submit")
            .WithEnvironment("AuthorizationServer__Clients__0__AllowedScopes__1", "offline_access")
            .WithEnvironment("AuthorizationServer__Clients__0__AllowedResources__0", mcpResource)
            .WithEnvironment("AuthorizationServer__Scopes__0__Name", "interview:submit")
            .WithEnvironment("AuthorizationServer__Scopes__0__Description", "Submit a structured interview record on your behalf");
        interviewService
            .WithEnvironment("Mcp__Issuer", mcpIssuer)
            .WithEnvironment("Mcp__Resource", mcpResource);
        // The web "Connect your AI client" page shows this address (public value, not a secret), via /api/config.
        web.WithEnvironment("MCP_RESOURCE_URL", mcpResource);
    }

    // The endpoint reference, not a literal 5100: the test host gives authservice a dynamic host port (ADR-0081), and a literal
    // address left the web app calling a port nothing listened on. In a developer's run the reference resolves to 5100 as before.
    var authority = authservice.GetEndpoint("http");
    interviewService
        .WithEnvironment("Jwt__Authority", authority)
        .WaitFor(authservice);
    web.WithEnvironment("AUTH_SERVICE_URL", authority).WaitFor(authservice);

    // Dashboard command (the "..." menu of postgres, authdb and interviewdb): a test user with interview credits, so a fresh local
    // stack can be used at once. It goes through the public APIs (DevSeed), is idempotent, and exists only in this development
    // AppHost; nothing of it is deployed (the Fly files do not use the AppHost).
    var interviewEndpoint = interviewService.GetEndpoint("http");
    var authorityEndpoint = authservice.GetEndpoint("http");
    async Task<ExecuteCommandResult> SeedTestUserAsync(ExecuteCommandContext context)
    {
        using var identity = new HttpClient { BaseAddress = new Uri(authorityEndpoint.Url), Timeout = TimeSpan.FromSeconds(30) };
        using var interviews = new HttpClient { BaseAddress = new Uri(interviewEndpoint.Url), Timeout = TimeSpan.FromSeconds(30) };
        var outcome = await DevSeed.SeedAsync(identity, interviews, DevSeed.Email, DevSeed.Password, DevSeed.TargetCredits, context.CancellationToken);
        return new ExecuteCommandResult { Success = outcome.Success, Message = outcome.Message };
    }
    var seedOptions = new CommandOptions
    {
        Description = $"Creates {DevSeed.Email} (password {DevSeed.Password}) with {DevSeed.TargetCredits} interview credits. Safe to run again. Needs authservice and interview-service to be running.",
        IconName = "PersonAdd",
    };
    postgres.WithCommand("seed-test-user", "Seed test user (with credits)", SeedTestUserAsync, seedOptions);
    authDb.WithCommand("seed-test-user", "Seed test user (with credits)", SeedTestUserAsync, seedOptions);
    interviewDb.WithCommand("seed-test-user", "Seed test user (with credits)", SeedTestUserAsync, seedOptions);
}

builder.Build().Run();
