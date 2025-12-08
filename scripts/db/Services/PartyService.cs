using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using MySqlConnector;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 파티 비즈니스 로직 처리 (Service Layer)
/// Spring Boot의 @Service와 동일한 역할
/// Repository 메서드에 Lock이 내장되어 있어 간단하게 호출 가능
/// </summary>
public class PartyService
{
    /// <summary>
    /// 파티 생성
    /// 비즈니스 로직: 기존 파티 만료 처리 + 새 파티 생성
    /// </summary>
    public static Task<bool> CreatePartyAsync(PartyEntity party)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 같은 메시지 키로 기존 파티가 있다면 만료 처리
            await PartyRepository.ExpiredParty(party.MESSAGE_KEY, conn, trans);

            // 2. 새 파티 생성
            var created = await PartyRepository.CreatePartyAsync(party, conn, trans);
            
            if (!created)
            {
                throw new Exception("파티 생성 실패");
            }

            return true;
        });
    }
    
    /// <summary>
    /// 파티 참가
    /// 비즈니스 로직: 중복 체크 + 참가 + 대기열 승격
    /// </summary>
    public static Task<bool> JoinPartyAsync(ulong messageId, ulong userId, string userNickname, bool isWait = false)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 이미 참가했는지 체크
            var exists = await PartyRepository.ExistsUser(messageId, userId, conn, trans);
            if (exists)
            {
                return false;
            }

            // 2. 유저 추가
            var added = await PartyRepository.AddUser(messageId, userId, userNickname, isWait, conn, trans);
            if (!added)
            {
                return false;
            }

            // 3. 대기열 승격 처리
            await PromoteWaitingMembersAsync(messageId, conn, trans);
            
            return true;
        });
    }
    
    /// <summary>
    /// 파티 나가기
    /// 비즈니스 로직: 나가기 + 대기열 승격
    /// </summary>
    public static Task<bool> LeavePartyAsync(ulong messageId, ulong userId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            if (!await PartyRepository.ExistsUser(messageId, userId, conn, trans))
            {
                return false;
            }
            
            // 1. 유저 제거
            var removed = await PartyRepository.RemoveUser(messageId, userId, conn, trans);
            if (!removed)
            {
                return false;
            }

            // 2. 대기열 승격 처리
            await PromoteWaitingMembersAsync(messageId, conn, trans);

            return true;
        });
    }
    
    /// <summary>
    /// 파티 인원 변경
    /// 비즈니스 로직: 인원 수 변경 + 증가 시 대기열 승격
    /// </summary>
    public static Task<(List<PartyMemberEntity> members, List<PartyMemberEntity> waitMember)> ResizePartyAsync(ulong messageId, int newCount)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 인원 수 업데이트
            var updated = await PartyRepository.UpdatePartySize(messageId, newCount, conn, trans);
            if (!updated)
            {
                throw new Exception("인원 수 업데이트 실패");
            }

            // 2. 대기열 승격 처리 (인원이 증가한 경우)
            await PromoteWaitingMembersAsync(messageId, conn, trans);

            // 같은 connection/transaction 사용하여 데드락 방지
            var members = await PartyRepository.GetPartyMemberList(messageId, conn, trans);
            var waitMembers = await PartyRepository.GetPartyWaitMemberList(messageId, conn, trans);

            return (members, waitMembers);
        });
    }
    
    /// <summary>
    /// 파티 강퇴
    /// 비즈니스 로직: 강퇴 + 대기열 승격
    /// </summary>
    public static Task<bool> KickMemberAsync(ulong messageId, ulong targetUserId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            // 1. 유저 제거
            var removed = await PartyRepository.RemoveUser(messageId, targetUserId, conn, trans);
            if (!removed)
            {
                throw new Exception("유저 제거 실패");
            }

            // 2. 대기열 승격 처리
            await PromoteWaitingMembersAsync(messageId, conn, trans);

            return true;
        });
    }
    
    /// <summary>
    /// 파티 만료
    /// </summary>
    public static Task<bool> ExpirePartyAsync(ulong messageId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.ExpiredParty(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 일시정지/재개
    /// </summary>
    public static Task<bool> SetPartyCloseAsync(ulong messageId, bool isClose)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.SetPartyClose(messageId, isClose, conn, trans);
        });
    }

    public static Task<bool> PartyRename(ulong messageId, string newName)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.PartyRename(messageId, newName, conn, trans);
        });
    }
    
    // ==================== 조회 메서드 ====================
    
    /// <summary>
    /// 파티 정보 조회
    /// </summary>
    public static Task<PartyEntity?> GetPartyEntityAsync(ulong messageId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.GetPartyEntity(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 멤버 리스트 조회
    /// </summary>
    public static Task<List<PartyMemberEntity>> GetPartyMemberListAsync(ulong messageId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.GetPartyMemberList(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 대기 멤버 리스트 조회
    /// </summary>
    public static Task<List<PartyMemberEntity>> GetPartyWaitMemberListAsync(ulong messageId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.GetPartyWaitMemberList(messageId, conn, trans);
        });
    }
    
    /// <summary>
    /// 파티 존재 여부 확인
    /// </summary>
    public static Task<bool> IsPartyExistsAsync(string displayName, ulong guildId)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.IsPartyExistsAsync(displayName, guildId, conn, trans);
        });
    }
    
    /// <summary>
    /// 만료된 파티 목록 조회
    /// </summary>
    public static Task<List<PartyEntity>> CycleExpiredPartyListAsync()
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await PartyRepository.CycleExpiredPartyList(conn, trans);
        });
    }

    /// <summary>
    /// 대기열 승격 처리 (비즈니스 로직)
    /// 파티에 빈 자리가 있고 대기 멤버가 있으면 자동 승격
    /// </summary>
    private static async Task PromoteWaitingMembersAsync(ulong messageId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        // 1. 파티 정보 조회
        var party = await PartyRepository.GetPartyEntity(messageId, connection, transaction);
        if (party == null) return;

        await PartyRepository.RemoveAllUser(messageId, connection, transaction);
            
        var list = new List<PartyMemberEntity>();
            
        list.AddRange(party.Members);
        list.AddRange(party.WaitMembers);
            
        for (var i = 0; i < list.Count; i++)
        {
            await PartyRepository.AddUser(messageId, list[i].USER_ID, list[i].USER_NICKNAME, (i + 1) > party.MAX_COUNT_MEMBER, connection, transaction);
        }

        // // 2. 빈 자리가 있고 대기 멤버가 있으면
        // if (party.WaitMembers.Count > 0 && party.Members.Count < party.MAX_COUNT_MEMBER)
        // {
        //     var availableSlots = party.MAX_COUNT_MEMBER - party.Members.Count;
        //
        //     for (int i = 0; i < availableSlots && i < party.WaitMembers.Count; i++)
        //     {
        //         var waitMember = party.WaitMembers[i];
        //
        //         // 대기열에서 제거
        //         var removed = await PartyRepository.RemoveUser(messageId, waitMember.USER_ID, connection, transaction);
        //         if (!removed)
        //         {
        //             throw new Exception($"Failed to remove waiting member {waitMember.USER_ID}");
        //         }
        //
        //         // 파티에 추가
        //         var added = await PartyRepository.AddUser(messageId, waitMember.USER_ID, waitMember.USER_NICKNAME, false, connection, transaction);
        //         if (!added)
        //         {
        //             throw new Exception($"Failed to add member {waitMember.USER_ID}");
        //         }
        //     }
        // }
        // else if(party.Members.Count > party.MAX_COUNT_MEMBER)
        // {
        //     await PartyRepository.RemoveAllUser(messageId, connection, transaction);
        //     
        //     var list = new List<PartyMemberEntity>();
        //     
        //     list.AddRange(party.Members);
        //     list.AddRange(party.WaitMembers);
        //     
        //     for (var i = 0; i < list.Count; i++)
        //     {
        //         await PartyRepository.AddUser(messageId, list[i].USER_ID, list[i].USER_NICKNAME, (i + 1) > party.MAX_COUNT_MEMBER, connection, transaction);
        //     }
        // }
    }
}

