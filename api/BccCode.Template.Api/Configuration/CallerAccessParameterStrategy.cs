using System.Reflection;
using BccCode.Template.Security;
using JasperFx;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Wolverine.Http;
using Wolverine.Http.CodeGen;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Supplies a <see cref="CallerAccess"/> parameter of any endpoint method (endpoint, LoadAsync, Permission, Validate) from
/// <see cref="CallerAccess.Load"/>, called once per request however many methods ask for it. Without this, Wolverine would
/// read the parameter from the request body.
/// </summary>
public sealed class CallerAccessParameterStrategy : IParameterStrategy
{
    private static readonly MethodInfo _load = typeof(CallerAccess).GetMethod(nameof(CallerAccess.Load))!;

    public bool TryMatch(HttpChain chain, IServiceContainer container, ParameterInfo parameter, out Variable? variable)
    {
        variable = null;
        if (parameter.ParameterType != typeof(CallerAccess)) return false;

        var call = chain.Middleware.OfType<MethodCall>().FirstOrDefault(m => m.Method == _load);
        if (call is null)
        {
            call = new MethodCall(typeof(CallerAccess), _load);
            chain.Middleware.Insert(0, call);
        }

        variable = call.ReturnVariable;
        return true;
    }
}
