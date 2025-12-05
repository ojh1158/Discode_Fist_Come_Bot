using MySqlConnector;
using System.Reflection;

namespace DiscodeBot.scripts.db;

public class DatabaseController : IDisposable
{
    private static string _connectionString = string.Empty;
    private static MySqlConnection? _connection;

    public static void Init()
    {
        _connectionString = Environment.GetEnvironmentVariable("DATABASE__CONNECTIONSTRING") 
            ?? throw new InvalidOperationException("DATABASE__CONNECTIONSTRING 환경변수가 설정되지 않았습니다.");
    }

    public static async Task<MySqlConnection> GetConnectionAsync()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new MySqlConnection(_connectionString);
            await _connection.OpenAsync();
        }
        return _connection;
    }

    /// <summary>
    /// 파라미터화된 비쿼리 실행 (INSERT, UPDATE, DELETE)
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        var connection = await GetConnectionAsync();
        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 스칼라 값 조회 (COUNT, SUM 등)
    /// </summary>
    public static async Task<object?> ExecuteScalarAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        var connection = await GetConnectionAsync();
        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// 비파라미터 쿼리 실행 (하위 호환성 유지)
    /// </summary>
    public static async Task NonQuery(string sql)
    {
        await ExecuteNonQueryAsync(sql);
    }

    /// <summary>
    /// SELECT 쿼리 결과를 제네릭 타입 리스트로 반환
    /// </summary>
    public static async Task<List<T>> Query<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
    {
        var connection = await GetConnectionAsync();
        var result = new List<T>();

        using var command = new MySqlCommand(sql, connection);
        
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }
        }
        
        using var reader = await command.ExecuteReaderAsync();

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        while (await reader.ReadAsync())
        {
            var item = new T();
            foreach (var prop in properties)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(prop.Name);
                    if (!reader.IsDBNull(ordinal))
                    {
                        var value = reader.GetValue(ordinal);
                        
                        // 타입 변환 처리 개선
                        if (prop.PropertyType == typeof(bool) && value is sbyte sbyteValue)
                        {
                            prop.SetValue(item, sbyteValue != 0);
                        }
                        else if (prop.PropertyType == typeof(bool?) && value is sbyte nullableSbyteValue)
                        {
                            prop.SetValue(item, nullableSbyteValue != 0);
                        }
                        else
                        {
                            prop.SetValue(item, Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType));
                        }
                    }
                }
                catch
                {
                    // 컬럼이 없거나 타입 변환 실패 시 무시
                }
            }
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// 단일 결과를 제네릭 타입으로 반환
    /// </summary>
    public static async Task<T?> QuerySingle<T>(string sql, Dictionary<string, object>? parameters = null) where T : new()
    {
        var results = await Query<T>(sql, parameters);
        return results.FirstOrDefault();
    }
    
    public void Dispose()
    {
        DisposeDatabase();
    }

    public static void DisposeDatabase()
    {
        _connection?.Dispose();
    }
}
