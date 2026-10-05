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

    // DEV-ONLY signing key. Source order: user-secrets (written by scripts/setup.*) -> a throwaway key
    // generated for this run. It is held by the authservice container and nothing else.
    var devKeyPem = builder.Configuration["Parameters:authservice-jwt-private-key"];
    if (string.IsNullOrWhiteSpace(devKeyPem))
    {
        using var rsa = RSA.Create(2048);
        devKeyPem = rsa.ExportPkcs8PrivateKeyPem();
    }
    var signingKey = builder.AddParameter("authservice-jwt-private-key", devKeyPem, secret: true);

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

    var authority = $"http://localhost:{AuthservicePort}";
    interviewService
        .WithEnvironment("Jwt__Authority", authority)
        .WaitFor(authservice);
    web.WithEnvironment("AUTH_SERVICE_URL", authority).WaitFor(authservice);
}

builder.Build().Run();
