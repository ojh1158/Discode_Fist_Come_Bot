namespace DiscodeBot.src.party;

public class PartyEntity
{
    // 파티 정원
    public int CAPACITY { get; set; } = 10;
    // 디스코드 채널 ID
    public ulong CHANNEL_ID { get; set; }
    // 만료 시각
    public DateTime EXPIRES_AT { get; set; }
    // 디스코드 길드 ID
    public ulong GUILD_ID { get; set; }
    // 파티 고유 ID ( === 디스코드 메시지 ID )
    public ulong ID { get; set; }
    // 모집 종료 여부
    public bool IS_CLOSED { get; set; } = false;
    // 파티 이름
    public string NAME { get; set; } = string.Empty;
    // 파티장 디스코드 사용자 ID
    public ulong OWNER_ID { get; set; }
}
