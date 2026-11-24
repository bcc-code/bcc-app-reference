using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace OidcMock;

/// <summary>
/// The mock's signing and encryption keys, kept in a schema of the app's database. Ephemeral keys change on every start, and
/// the API keeps the old JWKS cached, so on Cloud Run (scale to zero) the first sign-in after each cold start failed with
/// IDX10503. The keys only sign tokens for a mock that lets anyone sign in as anyone, so storing them in plain text is fine.
/// </summary>
internal static partial class PersistentKeys
{
    public static async Task<(SecurityKey Signing, SecurityKey Encryption)> LoadOrCreateAsync(string connectionString, string schema)
    {
        // The schema name goes into SQL text, so only plain lower-case names are accepted.
        if (!SchemaName().IsMatch(schema))
            throw new ArgumentException("The schema name must match [a-z0-9_]+.", nameof(schema));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // CREATE SCHEMA IF NOT EXISTS needs CREATE on the database even when the schema exists, which a preview's database user
        // may lack, so the schema is created only when it is missing.
        await using (var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = @schema)", connection))
        {
            exists.Parameters.AddWithValue("schema", schema);
            if (!(bool)(await exists.ExecuteScalarAsync())!)
            {
                await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
                await createSchema.ExecuteNonQueryAsync();
            }
        }

        // The same statement runs in preview.yml, so keep the two identical.
        await using (var createTable = new NpgsqlCommand(
            $"CREATE TABLE IF NOT EXISTS \"{schema}\".oidc_mock_keys (purpose text PRIMARY KEY, pem text NOT NULL)", connection))
        {
            await createTable.ExecuteNonQueryAsync();
        }

        return (await LoadOrCreateAsync(connection, schema, "signing"), await LoadOrCreateAsync(connection, schema, "encryption"));
    }

    private static async Task<SecurityKey> LoadOrCreateAsync(NpgsqlConnection connection, string schema, string purpose)
    {
        // Two instances starting together both offer a key; the first insert wins and both read it.
        using (var candidate = RSA.Create(2048))
        await using (var insert = new NpgsqlCommand(
            $"INSERT INTO \"{schema}\".oidc_mock_keys (purpose, pem) VALUES (@purpose, @pem) ON CONFLICT (purpose) DO NOTHING",
            connection))
        {
            insert.Parameters.AddWithValue("purpose", purpose);
            insert.Parameters.AddWithValue("pem", candidate.ExportRSAPrivateKeyPem());
            await insert.ExecuteNonQueryAsync();
        }

        await using var select = new NpgsqlCommand($"SELECT pem FROM \"{schema}\".oidc_mock_keys WHERE purpose = @purpose", connection);
        select.Parameters.AddWithValue("purpose", purpose);
        var pem = (string)(await select.ExecuteScalarAsync())!;

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex SchemaName();
}
