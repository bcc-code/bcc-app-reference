using BccCode.Template.Api.Configuration;
using Google.Cloud.Kms.V1;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace BccCode.Template.Api.DataProtection;

/// <summary>
/// The Data Protection key ring protects the session tickets, so every instance must share it
/// (standard 14, rule 14). The key store is the app's database, supplied as an <see cref="IXmlRepository"/>; the keys
/// are encrypted with a Cloud KMS key in every deployed environment.
/// </summary>
public static class DataProtectionConfiguration
{
    public const string KmsKeyNameKey = "Gcp:DataProtectionKmsKeyName";

    public static void AddTemplateDataProtection(this WebApplicationBuilder builder, Func<IServiceProvider, IXmlRepository> keyStore)
    {
        if (CodeGeneration.IsJasperFxCommand()) return;

        builder.Services.AddDataProtection().SetApplicationName("bcc-template");
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(o => o.XmlRepository = keyStore(sp)));

        var kmsKeyName = builder.Configuration[KmsKeyNameKey];
        if (!string.IsNullOrWhiteSpace(kmsKeyName))
        {
            builder.Services.AddSingleton(_ => KeyManagementServiceClient.Create());
            builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                new ConfigureOptions<KeyManagementOptions>(o => o.XmlEncryptor = new KmsXmlEncryptor(sp.GetRequiredService<KeyManagementServiceClient>, kmsKeyName)));
        }
        else if (!builder.Environment.IsDevelopmentOrTest())
        {
            throw new InvalidOperationException($"{KmsKeyNameKey} must be set outside Development and Test.");
        }
    }
}
