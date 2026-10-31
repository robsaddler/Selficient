using Microsoft.Data.SqlClient;

namespace Selficient.TestKit;

/// <summary>
/// A SQL Server database of its own for one test host, created on the server named by
/// <c>SELFICIENT_TEST_SQL</c> (the local default instance with integrated security when unset; the
/// workflow's service container on the runner) and dropped when the host is done. Every suite runs
/// the real store and the real migrations; nothing about the database is mocked.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    /// <summary>The environment variable naming the server. A connection string without a database.</summary>
    public const string ServerVariable = "SELFICIENT_TEST_SQL";

    private const string LocalServer = "Server=localhost;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    private bool _dropped;

    private TestDatabase(string name)
    {
        Name = name;
        ConnectionString = new SqlConnectionStringBuilder(Server) { InitialCatalog = name }.ConnectionString;
    }

    /// <summary>The server, without a database.</summary>
    public static string Server => Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } named ? named : LocalServer;

    /// <summary>The database's name.</summary>
    public string Name { get; }

    /// <summary>The connection string into this database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates an empty database with a fresh name.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The database name is a GUID this class made a moment ago; nothing in it was ever input.")]
    public static TestDatabase Create()
    {
        TestDatabase database = new($"selficient_test_{Guid.NewGuid():N}");
        using SqlConnection master = new(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString);
        master.Open();
        using SqlCommand create = new($"CREATE DATABASE [{database.Name}]", master);
        create.ExecuteNonQuery();
        return database;
    }

    /// <summary>Drops the database, closing every pooled connection into it first.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The database name is a GUID this class made; nothing in it was ever input.")]
    public void Dispose()
    {
        if (_dropped)
        {
            return;
        }

        _dropped = true;
        SqlConnection.ClearAllPools();
        using SqlConnection master = new(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString);
        master.Open();
        // A connection the pool had already lent out can still be closing; every session into the
        // database is ended first, and a drop that still finds one is tried again a moment later.
        string drop = $"""
            IF DB_ID('{Name}') IS NOT NULL
            BEGIN
                DECLARE @kill NVARCHAR(MAX) = N'';
                SELECT @kill += N'KILL ' + CAST(session_id AS NVARCHAR(10)) + N';' FROM sys.dm_exec_sessions WHERE database_id = DB_ID('{Name}') AND session_id <> @@SPID;
                EXEC (@kill);
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END
            """;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using SqlCommand command = new(drop, master);
                command.ExecuteNonQuery();
                return;
            }
            catch (SqlException) when (attempt < 5)
            {
                Thread.Sleep(250 * attempt);
            }
        }
    }
}
