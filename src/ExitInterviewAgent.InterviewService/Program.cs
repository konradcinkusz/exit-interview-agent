using ExitInterviewAgent.InterviewService.Infrastructure;
using ExitInterviewAgent.ServiceDefaults;

// Program.cs is a manifest (P9): each block is one capability, wired in the service's own extensions.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddCorsPolicy(builder.Configuration, CorsPolicies.Frontend);
builder.Services.AddStandardRateLimiting();
builder.Services.AddOpenApiDocument("interview-service", "v1", "Structured exit-interview records and employer signals.");
builder.Services.AddInterviewPersistence(builder.Configuration);

var app = builder.Build();

app.UseInterviewPipeline();
app.MapDefaultEndpoints();
app.MapInterviewEndpoints();
app.Services.LogIntegrationBanner();

app.Run();

/// <summary>Entry-point marker so the test project can host the service in-process.</summary>
public partial class Program;
