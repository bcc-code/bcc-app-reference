using System.Text;
using System.Xml.Linq;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace BccCode.Template.Web.DataProtection;

/// <summary>
/// Decrypts Data Protection key XML that was encrypted by <see cref="KmsXmlEncryptor"/>.
/// Data Protection instantiates this type by name through its <c>IActivator</c>, which only
/// knows two constructor shapes: one taking <see cref="IServiceProvider"/>, or a parameterless
/// one. <see cref="KeyManagementServiceClient"/> must therefore be registered in DI.
/// </summary>
internal sealed class KmsXmlDecryptor : IXmlDecryptor
{
    private readonly KeyManagementServiceClient _client;

    // The constructor Data Protection calls. It must be public and take exactly IServiceProvider;
    // a constructor taking the client directly is invisible to the activator, which then fails
    // every stored key with MissingMethodException.
    public KmsXmlDecryptor(IServiceProvider services)
        : this(services.GetRequiredService<KeyManagementServiceClient>())
    {
    }

    internal KmsXmlDecryptor(KeyManagementServiceClient client)
    {
        _client = client;
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        var keyName = (string?)encryptedElement.Element("kmsKeyName")
            ?? throw new InvalidOperationException("Encrypted key element has no kmsKeyName");
        var value = (string?)encryptedElement.Element("value")
            ?? throw new InvalidOperationException("Encrypted key element has no value");
        var ciphertext = Convert.FromBase64String(value);

        var response = _client.Decrypt(new DecryptRequest
        {
            Name = keyName,
            Ciphertext = ByteString.CopyFrom(ciphertext),
        });

        return XElement.Parse(Encoding.UTF8.GetString(response.Plaintext.ToByteArray()));
    }
}
