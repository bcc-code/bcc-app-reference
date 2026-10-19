using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Wolverine;
using Wolverine.Http;

namespace BccCode.Template.ArchitectureTests;

public class SliceConventionTests
{
    private static IEnumerable<MethodInfo> EndpointMethods() =>
        TemplateAssemblies.Apis.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().Any(a => a is WolverineHttpMethodAttribute));

    [Fact]
    public void Endpoints_exist_so_the_rules_below_are_not_vacuous() => EndpointMethods().Should().HaveCountGreaterThan(5);

    [Fact]
    public void Endpoints_are_static_classes() =>
        Types.InAssemblies(TemplateAssemblies.Apis).That().HaveNameEndingWith("Endpoint").Should().BeStatic().GetResult().FailingTypes
            .Select(t => t.FullName).Should().BeNullOrEmpty();

    [Fact]
    public void No_services_or_repositories() =>
        Types.InAssemblies(TemplateAssemblies.All).That().HaveNameEndingWith("Service").Or().HaveNameEndingWith("Repository").GetTypes()
            .Should().BeEmpty();

    [Fact]
    public void Every_endpoint_class_lives_in_a_feature_folder() =>
        Types.InAssemblies(TemplateAssemblies.Apis).That().HaveNameEndingWith("Endpoint").GetTypes()
            .Select(t => t.FullName).Where(name => !name.Contains(".Features.", StringComparison.Ordinal)).Should().BeEmpty();

    /// <summary>
    /// A request or a response is its own sealed record: an endpoint never returns a stored document or entity, because that
    /// would expose every stored field and tie the API to the schema (standard 09, rule 7).
    /// </summary>
    [Fact]
    public void Endpoints_return_sealed_records_and_not_stored_types()
    {
        var offenders = EndpointMethods()
            .Select(m => (Method: m, Response: Unwrap(m.ReturnType)))
            .Select(x => (x.Method, Response: IsValueTuple(x.Response) ? x.Response.GetGenericArguments()[0] : x.Response))
            .Where(x => !IsStorageAction(x.Response))   // an endpoint with no response body returns only the side effect, which names the stored type on purpose
            .Select(x => (x.Method, Response: x.Response.IsGenericType ? x.Response.GetGenericArguments()[0] : x.Response))
            .Where(x => TemplateAssemblies.Apis.Contains(x.Response.Assembly))
            .Where(x => !x.Response.IsSealed || x.Response.GetMethod("<Clone>$") is null)
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name} returns {x.Response.Name}");

        offenders.Should().BeEmpty();
    }

    // Names, not types, so this file compiles with any variant: Marten's Events, IMartenOp and IStartStream, and the framework's.
    private static readonly string[] SideEffectNames = ["Events", "IMartenOp", "IStartStream", "ISideEffect", "IResult", "ProblemDetails", "RoutedToEndpointMessage`1"];

    /// <summary>
    /// A returned message must name its endpoint, so it is never routed by convention: in serverless mode an unrouted message
    /// fails at send time. Everything after the response in an endpoint's return tuple must be a side effect, not a bare message.
    /// </summary>
    [Fact]
    public void Every_cascaded_value_is_a_side_effect_and_never_a_bare_message()
    {
        var bare = new List<string>();
        foreach (var method in EndpointMethods())
        {
            var returned = Unwrap(method.ReturnType);
            if (!IsValueTuple(returned)) continue;
            foreach (var type in returned.GetGenericArguments().Skip(1).Where(t => !IsSideEffect(t)))
                bare.Add($"{method.DeclaringType!.Name}.{method.Name} cascades a bare {type.Name}");
        }

        bare.Should().BeEmpty();
    }

    private static bool IsStorageAction(Type type) => type.IsGenericType && type.GetGenericTypeDefinition().Name == "IStorageAction`1";

    private static bool IsSideEffect(Type type) =>
        SideEffectNames.Contains(type.IsGenericType ? type.GetGenericTypeDefinition().Name : type.Name)
        || typeof(ISideEffect).IsAssignableFrom(type) || typeof(IResult).IsAssignableFrom(type) || typeof(ProblemDetails).IsAssignableFrom(type);

    private static bool IsValueTuple(Type type) => type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal);

    private static Type Unwrap(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericArguments()[0]
            : type;
}
