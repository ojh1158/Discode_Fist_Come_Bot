using System.Text;
using MySqlConnector;
using Dapper;
using DiscodeBot.src._core;
using DiscodeBot.src.user;

namespace DiscodeBot.src.user;

public class UserRepository : BaseRepository
{
    /// <summary>
    /// [Save] 사용자 정보를 저장하거나 업데이트합니다. (Upsert)
    /// </summary>
    public async Task<bool> SaveAsync(UserEntity entity)
    {
        const string sql = @"
            INSERT INTO USER (ID, NAME, NICKNAME) 
            VALUES (@ID, @NAME, @NICKNAME)
            ON DUPLICATE KEY UPDATE
                NAME = VALUES(NAME),
                NICKNAME = VALUES(NICKNAME);
        ";
        try
        {
            using var connection = await GetConnectionAsync();
            var affectedRows = await connection.ExecuteAsync(sql, entity);
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserRepository] Save 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// [Find] 조건에 맞는 사용자 목록과 전체 개수를 조회합니다.
    /// </summary>
    public async Task<PagedResult<UserEntity>> FindAsync(IEnumerable<ulong>? ids, int limit = 10, int offset = 0)
    {
        var builder = new StringBuilder();
        var parameters = new DynamicParameters();
        builder.Append(" WHERE 1=1 ");
        if (ids != null && ids.Any())
        {
            builder.Append(" AND ID IN @Ids ");
            parameters.Add("@Ids", ids);
        }
        var whereSql = builder.ToString();
        var sql = $@"
            SELECT COUNT(*) FROM USER {whereSql};
            SELECT * FROM USER {whereSql} LIMIT @Limit OFFSET @Offset;
        ";
        parameters.Add("@Limit", limit);
        parameters.Add("@Offset", offset);
        try
        {
            using var connection = await GetConnectionAsync();
            using var result = await connection.QueryMultipleAsync(sql, p);
            var totalCount = await result.ReadFirstAsync<long>();
            var items = await result.ReadAsync<UserEntity>();
            return new PagedResult<UserEntity>
            {
                TotalCount = totalCount,
                Items = items
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserRepository] Find 실패: {ex.Message}");
            return new PagedResult<UserEntity> { TotalCount = -1, Items = [] };
        }
    }
}
