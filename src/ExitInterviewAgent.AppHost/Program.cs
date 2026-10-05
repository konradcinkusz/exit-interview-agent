using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

// P1: the composition root. One command brings the system up:
//   dotnet run --project src/ExitInterviewAgent.AppHost
// Development only. Production is described by flyio/*.fly.toml, not by this file.
var builder = DistributedApplication.CreateBuilder(args);

// One Postgres server, one logical database per service (P3). Dev-only: both databases share the
// server's superuser; a deployment uses one role per database (flyio/SECRETS.md).
// Same major as flyio/postgres.fly.toml (postgres:17-alpine), so development and deployment do not drift.
var postgres = builder.AddPostgres("postgres").WithImageTag("17-alpine").WithDataVolume("exit-interview-agent-pgdata");
var interviewDb = postgres.AddDatabase("interviewdb");

var interviewService = builder.AddProject<Projects.ExitInterviewAgent_InterviewService>("interview-service")
    .WithReference(interviewDb)
    .WaitFor(interviewDb)
    .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
    .WithEnvironment("Jwt__Issuer", "ExitInterviewAgent")
    .WithEnvironment("Jwt__Audience", "ExitInterviewAgent")
    .WithHttpHealthCheck("/health");

var web = builder.AddJavaScriptApp("web", "../../web/app", "dev")
    .WithPnpm()
    .WithHttpEndpoint(env: "PORT")
    .WithReference(interviewService)
    .WaitFor(interviewService)
    .WithEnvironment("AUTH_ISSUER", "ExitInterviewAgent")
    .WithEnvironment("AUTH_AUDIENCE", "ExitInterviewAgent")
    .WithEnvironment("SESSION_COOKIE_SECURE", "false") // plain-http localhost only
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

    // DEV-ONLY secrets. Source order for each: user-secrets (written by scripts/setup.*) -> a throwaway value
    // generated for this run. They are held by the authservice container and nothing else.
    string DevSecret(string name, Func<string> generate)
    {
        var configured = builder.Configuration[$"Parameters:{name}"];
        return string.IsNullOrWhiteSpace(configured) ? generate() : configured;
    }
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

    var authority = $"http://localhost:{AuthservicePort}";
    interviewService
        .WithEnvironment("Jwt__Authority", authority)
        .WaitFor(authservice);
    web.WithEnvironment("AUTH_SERVICE_URL", authority).WaitFor(authservice);
}

builder.Build().Run();
