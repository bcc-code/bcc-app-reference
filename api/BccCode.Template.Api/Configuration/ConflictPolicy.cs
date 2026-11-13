using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine.Configuration;
using Wolverine.Http;
using Wolverine.Http.Runtime;
using Wolverine.Middleware;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// A second writer of one row answers 409 on every transactional chain, listed in the OpenAPI document (standard 12.12).
/// Instead of <c>MapConcurrencyFailuresToConflict</c>, whose filter runs before transactions are applied, so it would miss
/// endpoints that are transactional only because they take a <c>Change</c> or return a storage action.
/// </summary>
public sealed class ConflictPolicy : IHttpPolicy
{
    public void Apply(IReadOnlyList<HttpChain> chains, GenerationRules rules, IServiceContainer container)
    {
        var transactional = chains.Where(ConflictMapping.IsTransactional).ToList();

        var middleware = new MiddlewarePolicy();
        middleware.AddType(typeof(DbConcurrencyMiddleware));
        middleware.Apply(transactional.Cast<IChain>().ToList(), rules, container);

        new ConflictProblemPolicy(ConflictMapping.IsTransactional).Apply(transactional, rules, container);
    }
}
