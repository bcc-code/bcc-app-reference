using System.Xml.Linq;
using BccCode.Template.Persistence;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Api.DataProtection;

/// <summary>Keys shared by every instance, so a cookie or token minted on one instance validates on another.</summary>
public sealed class EfDataProtectionKeys(IDbContextFactory<TemplateDb> factory) : IXmlRepository
{
    // IXmlRepository is synchronous. It is called at startup and on key rotation only.
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var db = factory.CreateDbContext();
        return [.. db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).AsEnumerable().Select(xml => XElement.Parse(xml))];
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var db = factory.CreateDbContext();
        var xml = element.ToString(SaveOptions.DisableFormatting);
        var row = db.DataProtectionKeys.Find(friendlyName);
        if (row is null) db.DataProtectionKeys.Add(new DataProtectionKeyRow { FriendlyName = friendlyName, Xml = xml });
        else row.Xml = xml;
        db.SaveChanges();
    }
}
