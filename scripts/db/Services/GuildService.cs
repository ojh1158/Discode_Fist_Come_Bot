using DiscordBot.scripts.db.Repositories;

namespace DiscordBot.scripts.db.Services;

/// <summary>
/// 길드 비즈니스 로직 처리 (Service Layer)
/// </summary>
public class GuildService
{
    /// <summary>
    /// 길드 체크 (등록 또는 업데이트)
    /// </summary>
    public static Task<bool> GuildCheckAsync(ulong guildId, string guildName)
    {
        return DatabaseController.ExecuteInTransactionAsync(async (conn, trans) =>
        {
            return await GuildRepository.GuildCheck(guildId, guildName, conn, trans);
        });
    }
}


