using MySqlConnector;
using Dapper;
using System.Data;

namespace DiscodeBot.src._core;

public static class DbConfig
{
    public static string ConnectionString { get; private set; } = string.Empty;

    public static void Init()
    {
        ConnectionString = Environment.GetEnvironmentVariable("DATABASE__CONNECTIONSTRING") 
            ?? throw new InvalidOperationException("DATABASE__CONNECTIONSTRING 환경변수 누락");
    }
}

public abstract class BaseRepository
{
    protected string ConnectionString => DbConfig.ConnectionString;

    protected async Task<MySqlConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

public class PagedResult<T>
{
    public long TotalCount { get; set; }
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
}
