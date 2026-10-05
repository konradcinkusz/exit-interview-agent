using System.Text.Json;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Signals;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ExitInterviewAgent.Signals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExitInterviewAgent.InterviewService.Tests.Signals.SignalsHarness;

namespace ExitInterviewAgent.InterviewService.Tests.Signals;

/// <summary>The anti-corruption layer (ADR-0052): what crosses from the record store into the module, and what is dropped on the way.</summary>
public sealed class ObservationAdapterTests
{
    [Fact]
    public void A_stored_record_becomes_an_observation_with_nothing_that_identifies_it()
    {
        var record = Record("acme", rating: 2, tenure: "5y_10y", seniority: "senior", function: "operations_support", quote: "Zebrafish quote about the roof");
        record.Topic("growth")["rating"] = null;
        record["topics"]!["culture"] = new System.Text.Json.Nodes.JsonObject { ["status"] = "no_data" };

        var observation = RecordStoreObservationSource.Map("acme", VerificationLevel.Verified, record.ToJsonString())!;

        Assert.Equal(("acme", "5y_10y", "senior", "operations_support", VerificationState.Verified),
            (observation.EmployerRef, observation.TenureBand, observation.SeniorityBand, observation.FunctionBand, observation.Verification));
        Assert.Equal(5, observation.Ratings.Count);                         // no_data is absent
        Assert.DoesNotContain(observation.Ratings, r => r.Topic == "culture");
        Assert.Null(observation.Ratings.Single(r => r.Topic == "growth").Rating); // covered without a number
        var serialised = JsonSerializer.Serialize(observation);
        Assert.DoesNotContain("Zebrafish", serialised);
        Assert.DoesNotContain(record["interviewId"]!.GetValue<string>(), serialised);
    }

    [Fact]
    public void Optional_bands_left_out_stay_null_never_a_placeholder()
    {
        var record = Record("acme", seniority: null, function: null);

        var observation = RecordStoreObservationSource.Map("acme", VerificationLevel.Unchecked, record.ToJsonString())!;

        Assert.Null(observation.SeniorityBand);
        Assert.Null(observation.FunctionBand);
    }

    [Theory]
    [InlineData(VerificationLevel.Unchecked, VerificationState.Unchecked)]
    [InlineData(VerificationLevel.Unverified, VerificationState.Unverified)]
    [InlineData(VerificationLevel.Verified, VerificationState.Verified)]
    public void Verification_levels_map_one_to_one(VerificationLevel stored, VerificationState expected)
        => Assert.Equal(expected, RecordStoreObservationSource.Map("acme", stored, Record("acme").ToJsonString())!.Verification);

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":"2","context":{"tenureBand":"lt_6m"},"topics":{}}""")]
    [InlineData("""{"schemaVersion":"1","context":{},"topics":{}}""")]
    [InlineData("""{"schemaVersion":"1","context":{"tenureBand":"lt_6m"},"topics":{"culture":{"status":"covered"}}}""")]
    public void Anything_that_is_not_a_schema_v1_record_is_skipped_not_repaired(string json)
        => Assert.Null(RecordStoreObservationSource.Map("acme", VerificationLevel.Unchecked, json));

    [Fact]
    public async Task The_adapter_streams_the_store_grouped_by_employer()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        foreach (var employer in new[] { "bravo", "alpha", "charlie" })
        {
            await SubmitManyAsync(host, employer, 2, _ => Record(employer));
        }

        var observations = await host.InScopeAsync(async sp =>
        {
            var source = new RecordStoreObservationSource(sp.GetRequiredService<InterviewDbContext>());
            var all = new List<Observation>();
            await foreach (var o in source.ReadAsync(default))
            {
                all.Add(o);
            }
            return all;
        });

        Assert.Equal(["alpha", "alpha", "bravo", "bravo", "charlie", "charlie"], observations.Select(o => o.EmployerRef));
    }
}
