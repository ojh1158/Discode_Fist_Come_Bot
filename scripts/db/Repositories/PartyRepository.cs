using MySqlConnector;
using DiscodeBot.scripts.db.Models;
using DiscodeBot.scripts._src;
using Dapper;

namespace DiscodeBot.scripts.db.Repositories;

/// <summary>
/// 실제 DB 구조에 맞춘 파티 Repository
/// </summary>
public class PartyRepository
{
    /// <summary>
    /// 파티를 생성합니다.
    /// </summary>
    /// <returns>생성 성공 시 true, 실패 시 false</returns>
    public static async Task<bool> CreatePartyAsync(
        PartyEntity party)
    {
        if (await ExpiredParty(party.MESSAGE_KEY))
        {
            return false;
        }

        try
        {
            var sql = @"
INSERT INTO PARTY (DISPLAY_NAME, MAX_COUNT_MEMBER, MESSAGE_KEY, GUILD_KEY, CHANNEL_KEY, OWNER_KEY, OWNER_NICKNAME, EXPIRE_DATE, IS_CLOSED)
VALUES (@DISPLAY_NAME, @MAX_COUNT_MEMBER, @MESSAGE_KEY, @GUILD_KEY, @CHANNEL_KEY, @OWNER_KEY, @OWNER_NICKNAME, @EXPIRE_DATE, 0)
    ";
            var connection = await DatabaseController.GetConnectionAsync();

            var affectedRows = await connection.ExecuteAsync(sql, party);
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"파티 생성 실패: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> IsPartyExistsAsync(string displayName, ulong guildId)
    {
        try
        {
            var sql =
                @"
SELECT EXISTS(
    SELECT 1
    FROM PARTY
    WHERE DISPLAY_NAME = @DisplayName
    AND   GUILD_KEY = @GuildKey
    AND IS_EXPIRED = FALSE
)
";

            var connection = await DatabaseController.GetConnectionAsync();
            return await connection.ExecuteScalarAsync<bool>(sql,
                new { DisplayName = displayName, GuildKey = guildId });
        }
        catch (Exception e)
        {
            Console.WriteLine($"파티 생성 실패: {e.Message}");
            return false;
        }
    }

    // PartyRepository.cs에 추가할 메서드 예시
    public static async Task<PartyEntity?> GetPartyEntity(ulong messageId)
    {
        try
        {
            var connection = await DatabaseController.GetConnectionAsync();

            // 1. 파티 기본 정보
            var party = await connection.QuerySingleOrDefaultAsync<PartyEntity>(
                @"
SELECT * 
FROM PARTY 
WHERE MESSAGE_KEY = @MessageId
AND IS_EXPIRED = FALSE 
",
                new { MessageId = messageId }
            );

            if (party == null) return null;

            // 2. 파티 멤버
            party.Members = await GetPartyMemberList(messageId);

            // 3. 대기 멤버
            party.WaitMembers = await GetPartyWaitMemberList(messageId);

            return party;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyMemberList(ulong messageId)
    {
        var connection = await DatabaseController.GetConnectionAsync();

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
                new { MessageId = messageId });

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task<List<PartyMemberEntity>> GetPartyWaitMemberList(ulong messageId)
    {
        var connection = await DatabaseController.GetConnectionAsync();

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
                new { MessageId = messageId });

            return result.ToList();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task<bool> ExistsUser(ulong messageId, ulong userId)
    {
        var connection = await DatabaseController.GetConnectionAsync();
        
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
                new { MESSAGE_KEY = messageId , USER_ID = userId });

            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }

    public static async Task<bool> AddUser(ulong messageId, ulong userId, string userNickname, bool isWait, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();
        
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

    public static async Task<bool> RemoveUser(ulong messageId, ulong userId, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();

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
    public static async Task<bool> UpdateParty(ulong messageID)
    {
        // 1. 먼저 파티 정보 조회 (트랜잭션 없이)
        var partyEntity = await GetPartyEntity(messageID);

        if (partyEntity == null)
        {
            Console.WriteLine($"{messageID} not found");
            return false;
        }

        // 2. 이후 트랜잭션 시작
        var connection = await DatabaseController.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            if (partyEntity.WaitMembers.Count > 0 && partyEntity.Members.Count < partyEntity.MAX_COUNT_MEMBER)
            {
                for (int i = 0; i < partyEntity.MAX_COUNT_MEMBER - partyEntity.Members.Count; i++)
                {
                    var waitMember = partyEntity.WaitMembers.FirstOrDefault();
                    if (waitMember == null) continue;

                    // 트랜잭션 내에서 실행
                    var removeSuccess = await RemoveUser(messageID, waitMember.USER_ID, connection, transaction);
                    if (!removeSuccess)
                    {
                        throw new Exception($"Failed to remove user {waitMember.USER_ID}");
                    }

                    var addSuccess = await AddUser(messageID, waitMember.USER_ID, waitMember.USER_NICKNAME, false, connection, transaction);
                    if (!addSuccess)
                    {
                        throw new Exception($"Failed to add user {waitMember.USER_ID}");
                    }

                    partyEntity.WaitMembers.Remove(waitMember);
                }
            }

            // 모든 작업이 성공하면 커밋
            await transaction.CommitAsync();

            return true;
        }
        catch (Exception e)
        {
            // 오류 발생 시 롤백
            Console.WriteLine($"UpdateParty failed, rolling back: {e.Message}");
            await transaction.RollbackAsync();
            return false;
        }
    }

    public static async Task<bool> SetPartyClose(ulong messageId, bool isClose, MySqlConnection? connection = null, MySqlTransaction? transaction = null)
    {
        connection ??= await DatabaseController.GetConnectionAsync();

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

    public static async Task<bool> ExpiredParty(ulong messageId)
    {
        var connection = await DatabaseController.GetConnectionAsync();
        
        try
        {
            var affectedRows = await connection.ExecuteAsync(
                @"
UPDATE PARTY
SET IS_EXPIRED = TRUE
WHERE MESSAGE_KEY = @MESSAGE_KEY
",
                new { MESSAGE_KEY = messageId });

            return affectedRows > 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }   
    }


    public static async Task<List<PartyEntity>> CycleExpiredPartyList()
    {
        try
        {
            var connection = await DatabaseController.GetConnectionAsync();

            // 만료 시간이 지난 파티 목록 조회
            var parties = (await connection.QueryAsync<PartyEntity>(
                @"
SELECT * 
FROM PARTY 
WHERE IS_EXPIRED = FALSE
AND EXPIRE_DATE <= NOW()
")).ToList();

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
}

