using Dapper;
using MySqlConnector;
using DiscodeBot.scripts.db.Models;

namespace DiscodeBot.scripts.db.Repositories;

/// <summary>
/// 길드 정보 Repository
/// </summary>
public class GuildRepository
{ 
    public static async Task<bool> GuildCheck(ulong guildId, string guildName)
    {
        var connection = await DatabaseController.GetConnectionAsync();
        
        try
        {
            var guildInfoEntity = await connection.QuerySingleOrDefaultAsync<GuildInfoEntity>(
                @"
SELECT *
FROM GUILD_INFO
WHERE ID = @id
",
                new { id = guildId });
            
            if (guildInfoEntity == null)
            {
                var sql = @"
INSERT INTO GUILD_INFO (ID, NAME) 
VALUES (@id, @name)
    ";

                var affectedRows = await connection.ExecuteAsync(sql, new {id = guildId, name = guildName});
                if (affectedRows <= 0)
                {
                    return false;
                }
            }
            else
            {
                if (guildInfoEntity.BAN_FLAG)
                {
                   return false; 
                }
                
                var sql = @"
UPDATE GUILD_INFO
SET USE_COUNT = USE_COUNT + 1
WHERE ID = @id
    ";
                var affectedRows = await connection.ExecuteAsync(sql, new { id = guildId });
                if (affectedRows <= 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }
}

