using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Http;
using Wolverine.Middleware;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Calls an endpoint's static <c>Permission</c> method after LoadAsync and before Before and Validate, the way Wolverine
/// calls Validate: a non-null result ends the request. Before checks the version the client saw (a stale one is 409),
/// which a caller who may not act must not learn. Permission answers who may see the resource (404) and who may act on
/// it (403); Validate keeps the business rules (standard 09.2). PermissionConventionTests requires one on every endpoint
/// that is not on its allowlist.
/// </summary>
public sealed class PermissionPolicy : IHttpPolicy
{
    private const string METHOD_NAME = "Permission";

    public void Apply(IReadOnlyList<HttpChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            var endpointType = chain.Method.HandlerType;
            var method = endpointType.GetMethod(METHOD_NAME, BindingFlags.Public | BindingFlags.Static);
            if (method is null) continue;

            var call = new MethodCall(endpointType, method);
            chain.ApplyParameterMatching(call);
            var frames = new List<Frame> { call };
            if (rules.TryFindContinuationHandler(chain, call, out var stop)) frames.Add(stop!);

            var validate = chain.Middleware.FindIndex(f => f is MethodCall m && m.HandlerType == endpointType && m.Method.Name is "Before" or "BeforeAsync" or "Validate" or "ValidateAsync");
            chain.Middleware.InsertRange(validate < 0 ? chain.Middleware.Count : validate, frames);
        }
    }
}
