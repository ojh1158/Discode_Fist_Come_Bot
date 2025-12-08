using MySqlConnector;
using DiscordBot.scripts._src;
using Dapper;
using DiscordBot.scripts.db.Models;

namespace DiscordBot.scripts.db.Repositories;

/// <summary>
/// 실제 DB 구조에 맞춘 파티 Repository (Data Access Layer)
/// 순수 DB CRUD 작업만 담당
/// </summary>
public class PartyRepository
{
    /// <summary>
    /// 파티를 생성합니다. (순수 INSERT만)
    /// </summary>
    /// <returns>생성 성공 시 true, 실패 시 false</returns>
    public static async Task<bool> CreatePartyAsync(PartyEntity party, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
INSERT INTO PARTY (DISPLAY_NAME, MAX_COUNT_MEMBER, MESSAGE_KEY, GUILD_KEY, CHANNEL_KEY, OWNER_KEY, OWNER_NICKNAME, EXPIRE_DATE, IS_CLOSED)
VALUES (@DISPLAY_NAME, @MAX_COUNT_MEMBER, @MESSAGE_KEY, @GUILD_KEY, @CHANNEL_KEY, @OWNER_KEY, @OWNER_NICKNAME, @EXPIRE_DATE, 0)
    ";
            var affectedRows = await connection.ExecuteAsync(sql, party, transaction: transaction);
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"파티 생성 실패: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> IsPartyExistsAsync(string displayName, ulong guildId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var sql = @"
SELECT EXISTS(
    SELECT 1
    FROM PARTY
    WHERE DISPLAY_NAME = @DisplayName
    AND   GUILD_KEY = @GuildKey
    AND IS_EXPIRED = FALSE
)
";
            return await connection.ExecuteScalarAsync<bool>(sql,
                new { DisplayName = displayName, GuildKey = guildId },
                transaction: transaction);
        }
        catch (Exception e)
        {
            Console.WriteLine($"파티 존재 확인 실패: {e.Message}");
            return false;
        }
    }

    public static async Task<PartyEntity?> GetPartyEntity(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            // 1. 파티 기본 정보
            var party = await connection.QuerySingleOrDefaultAsync<PartyEntity>(
                @"
SELECT * 
FROM PARTY 
WHERE MESSAGE_KEY = @MessageId
AND IS_EXPIRED = FALSE 
",
                new { MessageId = messageId },
                transaction: transaction
            );

            if (party == null) return null;

            // 2. 파티 멤버
            party.Members = await GetPartyMemberList(messageId, connection, transaction);

            // 3. 대기 멤버
            party.WaitMembers = await GetPartyWaitMemberList(messageId, connection, transaction);

            return party;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyMemberList(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var result = await connection.QueryAsync<PartyMemberEntity>(
                @"
SELECT * 
FROM PARTY_MEMBER 
WHERE MESSAGE_KEY = @MessageId
 AND EXIT_FLAG = FALSE
ORDER BY CREATE_DATE
",
                new { MessageId = messageId },
                transaction: transaction);

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyWaitMemberList(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var result = await connection.QueryAsync<PartyMemberEntity>(
                @"
SELECT * 
FROM PARTY_WAIT_MEMBER 
WHERE MESSAGE_KEY = @MessageId
AND EXIT_FLAG = FALSE
ORDER BY CREATE_DATE
",
                new { MessageId = messageId },
                transaction: transaction);

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task<bool> ExistsUser(ulong messageId, ulong userId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var result = await connection.ExecuteScalarAsync<bool>(
                @"
SELECT EXISTS(
    SELECT 1
    FROM PARTY_MEMBER
    WHERE MESSAGE_KEY = @MESSAGE_KEY
      AND USER_ID = @USER_ID
      AND EXIT_FLAG = false
    UNION ALL
    SELECT 1
    FROM PARTY_WAIT_MEMBER
    WHERE MESSAGE_KEY = @MESSAGE_KEY
      AND USER_ID = @USER_ID
      AND EXIT_FLAG = false
    LIMIT 1
    )
",
                new { MESSAGE_KEY = messageId , USER_ID = userId },
                transaction: transaction);

            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }

    public static async Task<bool> AddUser(ulong messageId, ulong userId, string userNickname, bool isWait, MySqlConnection connection, MySqlTransaction transaction)
    {
        
        try
        {
            string sql;
            
            if (isWait)
            {
                // 대기 멤버는 제한 없이 추가
                sql = @"
INSERT INTO PARTY_WAIT_MEMBER (MESSAGE_KEY, USER_ID, USER_NICKNAME) 
VALUES (@MESSAGE_KEY, @USER_ID, @USER_NICKNAME)";
            }
            else
            {
                // 파티 멤버는 MAX_COUNT_MEMBER 체크 후 추가
                sql = @"
INSERT INTO PARTY_MEMBER (MESSAGE_KEY, USER_ID, USER_NICKNAME)
SELECT @MESSAGE_KEY, @USER_ID, @USER_NICKNAME
WHERE (
    SELECT COUNT(*) 
    FROM PARTY_MEMBER 
    WHERE MESSAGE_KEY = @MESSAGE_KEY
    AND EXIT_FLAG = FALSE
) < (
    SELECT MAX_COUNT_MEMBER 
    FROM PARTY 
    WHERE MESSAGE_KEY = @MESSAGE_KEY
)";
            }
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { MESSAGE_KEY = messageId, USER_ID = userId, USER_NICKNAME = userNickname },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }

    public static async Task<bool> RemoveUser(ulong messageId, ulong userId, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            // 두 개의 별도 UPDATE로 분리
            var sql1 = @"
UPDATE PARTY_MEMBER 
SET EXIT_FLAG = 1
WHERE USER_ID = @USER_ID AND MESSAGE_KEY = @MESSAGE_KEY";

            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER 
SET EXIT_FLAG = 1
WHERE USER_ID = @USER_ID AND MESSAGE_KEY = @MESSAGE_KEY";

            var affected1 = await connection.ExecuteAsync(sql1,
                new { MESSAGE_KEY = messageId, USER_ID = userId },
                transaction: transaction);

            var affected2 = await connection.ExecuteAsync(sql2,
                new { MESSAGE_KEY = messageId, USER_ID = userId },
                transaction: transaction);

            // 둘 중 하나라도 업데이트되면 성공
            return (affected1 + affected2) > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> ExitAllUser(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        
        try
        {
            // 두 개의 별도 UPDATE로 분리
            var sql1 = @"
UPDATE PARTY_MEMBER 
SET EXIT_FLAG = 1
WHERE MESSAGE_KEY = @MESSAGE_KEY";

            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER 
SET EXIT_FLAG = 1
WHERE MESSAGE_KEY = @MESSAGE_KEY";

            var affected1 = await connection.ExecuteAsync(sql1,
                new { MESSAGE_KEY = messageId },
                transaction: transaction);

            var affected2 = await connection.ExecuteAsync(sql2,
                new { MESSAGE_KEY = messageId },
                transaction: transaction);

            // 둘 중 하나라도 업데이트되면 성공
            return (affected1 + affected2) > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    /// <summary>
    /// 파티 인원 수 업데이트 (순수 UPDATE만)
    /// </summary>
    public static async Task<bool> UpdatePartySize(ulong messageId, int newSize, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET MAX_COUNT_MEMBER = @MaxCount
WHERE MESSAGE_KEY = @MessageKey
";
            var affectedRows = await connection.ExecuteAsync(sql,
                new { MaxCount = newSize, MessageKey = messageId },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    public static async Task<bool> SetPartyClose(ulong messageId, bool isClose, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET IS_CLOSED = @isClose
WHERE MESSAGE_KEY = @MESSAGE_KEY
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { MESSAGE_KEY = messageId , isClose = isClose ? 1 : 0 },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    public static async Task<bool> PartyRename(ulong messageId, string newName, MySqlConnection connection, MySqlTransaction transaction)
    {

        try
        {
            var sql = @"
UPDATE PARTY
SET DISPLAY_NAME= @name
WHERE MESSAGE_KEY = @MESSAGE_KEY
    ";
            
            var affectedRows = await connection.ExecuteAsync(sql,
                new { MESSAGE_KEY = messageId , name = newName },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }


    public static async Task<bool> ExpiredParty(ulong messageId, MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var affectedRows = await connection.ExecuteAsync(
                @"
UPDATE PARTY
SET IS_EXPIRED = TRUE
WHERE MESSAGE_KEY = @MESSAGE_KEY
",
                new { MESSAGE_KEY = messageId },
                transaction: transaction);

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }



    public static async Task<List<PartyEntity>> CycleExpiredPartyList(MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            // 만료 시간이 지난 파티 목록 조회
            var parties = (await connection.QueryAsync<PartyEntity>(
                @"
SELECT * 
FROM PARTY 
WHERE IS_EXPIRED = FALSE
AND EXPIRE_DATE <= NOW()
",
                transaction: transaction)).ToList();

            if (!parties.Any())
            {
                return new List<PartyEntity>();
            }
            
            return parties;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return new List<PartyEntity>();
        }
    }

    public static async Task<bool> RemoveAllUser(ulong messageId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        try
        {
            var sql = @"
UPDATE PARTY_MEMBER
SET EXIT_FLAG = 1
WHERE MESSAGE_KEY = @MessageKey
            ";
            
            var sql2 = @"
UPDATE PARTY_WAIT_MEMBER
SET EXIT_FLAG = 1
WHERE MESSAGE_KEY = @MessageKey
            ";
            
            var a1 = await connection.ExecuteAsync(sql,
                new { MessageKey = messageId },
                transaction: transaction);
            
            var a2 = await connection.ExecuteAsync(sql2,
                new { MessageKey = messageId },
                transaction: transaction);

            return a2 > 0 || a1 > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
}

