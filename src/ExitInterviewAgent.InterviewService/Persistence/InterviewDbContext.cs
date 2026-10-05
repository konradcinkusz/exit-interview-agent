using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.InterviewService.Persistence;

/// <summary>
/// The interview-service's database (P3: owned by this service, opened by nothing else). The model
/// is empty on purpose: the scaffold ships the mechanism, entities arrive with the record schema.
/// </summary>
public sealed class InterviewDbContext(DbContextOptions<InterviewDbContext> options) : DbContext(options);
