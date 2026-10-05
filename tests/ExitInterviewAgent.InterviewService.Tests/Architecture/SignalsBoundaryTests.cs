using System.Reflection;
using ExitInterviewAgent.InterviewService.Signals;

namespace ExitInterviewAgent.InterviewService.Tests.Architecture;

/// <summary>
/// ADR-0052 from the service side: the submission side meets the module in exactly one adapter, one registration file and one
/// endpoint slice. Nothing else in the service names a Signals type, and the module names nothing of the service.
/// </summary>
public sealed class SignalsBoundaryTests
{
    private static readonly Assembly Service = typeof(Program).Assembly;

    [Fact]
    public void Only_the_adapter_the_registration_and_the_endpoints_use_signals_types()
    {
        var users = Service.GetTypes()
            .Where(t => !t.IsNested && t.Namespace?.StartsWith("ExitInterviewAgent.InterviewService", StringComparison.Ordinal) == true)
            .Where(t => Touches(t, "ExitInterviewAgent.Signals"))
            .Select(t => t.Name)
            .Where(n => !n.StartsWith('<'))
            .Order()
            .ToArray();

        Assert.Equal(["DemoDataService", "RecordStoreObservationSource", "SignalsEndpoints", "SignalsMapping"], users); // signatures only: SignalsHostExtensions uses the module inside a method body
    }

    private static bool Touches(Type type, string assemblyName)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var seen = type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(type.GetFields(all).Select(f => f.FieldType)).Concat(type.GetProperties(all).Select(p => p.PropertyType))
            .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Concat(type.GetInterfaces());
        return seen.Any(t => Walk(t).Any(x => x.Assembly.GetName().Name == assemblyName));
    }

    private static IEnumerable<Type> Walk(Type t)
    {
        yield return t;
        foreach (var argument in t.IsGenericType ? t.GetGenericArguments() : [])
        {
            foreach (var inner in Walk(argument))
            {
                yield return inner;
            }
        }
        if (t.HasElementType)
        {
            foreach (var inner in Walk(t.GetElementType()!))
            {
                yield return inner;
            }
        }
    }

    [Fact]
    public void The_submission_side_does_not_depend_on_the_module_except_through_the_adapter()
    {
        // The reverse direction is pinned in the module's own test project: it references only the kernel.
        var recordStoreTypes = new[] { "InterviewDbContext", "RecordRow", "SubmissionService", "ReceiptService", "RetentionPurger", "TicketStore" };
        var offenders = Service.GetTypes().Where(t => recordStoreTypes.Contains(t.Name) && Touches(t, "ExitInterviewAgent.Signals")).Select(t => t.Name).ToArray();

        Assert.Empty(offenders);
        Assert.Contains(typeof(RecordStoreObservationSource).Assembly.GetReferencedAssemblies(), a => a.Name == "ExitInterviewAgent.Signals");
    }
}
