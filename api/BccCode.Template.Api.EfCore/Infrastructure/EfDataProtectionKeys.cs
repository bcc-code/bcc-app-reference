using System.Xml.Linq;
using BccCode.Template.Api.EfCore.Configuration;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Api.EfCore.Infrastructure;

/// <summary>A Data Protection key ring entry, shared by every API instance.</summary>
public sealed class DataProtectionKeyRow
{
    public string FriendlyName { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}

public sealed class DataProtectionKeyRowConfiguration : IEntityTypeConfiguration<DataProtectionKeyRow>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRow> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.HasKey(k => k.FriendlyName);
        builder.Property(k => k.FriendlyName).HasMaxLength(200);
    }
}

/// <summary>Keys shared by every instance, so a cookie or token minted on one instance validates on another.</summary>
public sealed class EfDataProtectionKeys(IDbContextFactory<TemplateDbContext> factory) : IXmlRepository
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
