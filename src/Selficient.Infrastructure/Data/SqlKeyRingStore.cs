using Dapper;
using Microsoft.Data.SqlClient;
using Selficient.Application.Operations;

namespace Selficient.Infrastructure.Data;

/// <summary>The key ring in the platform database: one row per key, written once, read in order.</summary>
public sealed class SqlKeyRingStore : IKeyRingStore
{
    private readonly string _connectionString;

    /// <summary>Creates the store over the connection string.</summary>
    public SqlKeyRingStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ReadAll()
    {
        using SqlConnection connection = new(_connectionString);
        connection.Open();
        return connection.Query<string>("SELECT xml FROM dbo.data_protection_keys ORDER BY id").ToList();
    }

    /// <inheritdoc />
    public void Write(string name, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        using SqlConnection connection = new(_connectionString);
        connection.Open();
        connection.Execute(
            "INSERT INTO dbo.data_protection_keys (friendly_name, xml, created_at) VALUES (@name, @xml, SYSUTCDATETIME())",
            new { name, xml });
    }
}
