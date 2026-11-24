using System.Text;
using System.Xml.Linq;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace BccCode.Template.Api.DataProtection;

/// <summary>
/// Encrypts Data Protection key XML with a Google Cloud KMS key before it is persisted to
/// Postgres. Pairs with <see cref="KmsXmlDecryptor"/>, which Data Protection instantiates through
/// its <c>IActivator</c> using the type recorded in <see cref="EncryptedXmlInfo"/>. The client is resolved on first
/// use, because creating it needs Google credentials, which codegen and startup must not depend on.
/// </summary>
internal sealed class KmsXmlEncryptor(Func<KeyManagementServiceClient> client, string keyName) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextElement.ToString());

        var response = client().Encrypt(new EncryptRequest
        {
            Name = keyName,
            Plaintext = ByteString.CopyFrom(plaintextBytes),
        });

        var encryptedElement = new XElement("encryptedKey",
            new XElement("kmsKeyName", keyName),
            new XElement("value", Convert.ToBase64String(response.Ciphertext.ToByteArray())));

        return new EncryptedXmlInfo(encryptedElement, typeof(KmsXmlDecryptor));
    }
}
