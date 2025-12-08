namespace DiscordBot.scripts.db.Models;

/// <summary>
/// GUILD_INFO 테이블 엔티티
/// </summary>
public class GuildInfoEntity
{
    public uint SEQ { get; set; }
    public ulong ID { get; set; }
    public string NAME { get; set; } = string.Empty;
    public bool BAN_FLAG { get; set; } = false;
}

