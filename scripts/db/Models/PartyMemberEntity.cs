namespace DiscordBot.scripts.db.Models;

/// <summary>
/// PARTY_MEMBER AND PARTY_WAIT_MEMBER 테이블 엔티티
/// </summary>
public class PartyMemberEntity
{
    public ulong MESSAGE_KEY { get; set; }
    public ulong USER_ID { get; set; }
    public string USER_NICKNAME { get; set; }
}

