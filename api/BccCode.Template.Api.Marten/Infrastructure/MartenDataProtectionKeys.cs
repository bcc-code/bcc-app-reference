using System.Xml.Linq;
using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace BccCode.Template.Api.Marten.Infrastructure;

/// <summary>A Data Protection key ring entry, shared by every API instance.</summary>
public sealed record DataProtectionKeyDocument(string Id, string Xml);

/// <summary>Keys shared by every instance, so a cookie or token minted on one instance validates on another.</summary>
public sealed class MartenDataProtectionKeys(IDocumentStore store) : IXmlRepository
{
    // IXmlRepository is synchronous. It is called at startup and on key rotation only.
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = store.QuerySession();
        return session.Query<DataProtectionKeyDocument>().ToListAsync().GetAwaiter().GetResult()
            .Select(d => XElement.Parse(d.Xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var session = store.LightweightSession();
        session.Store(new DataProtectionKeyDocument(friendlyName, element.ToString(SaveOptions.DisableFormatting)));
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
