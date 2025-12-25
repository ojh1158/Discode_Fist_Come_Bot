namespace DiscodeBot.src.party-member;

public class PartyMemberEntity
{
    // 유휴 상태 여부
    public bool IS_IDLE { get; set; }
    // 대기 상태 여부
    public bool IS_WAITING { get; set; }
    // 참여 시점
    public ulong JOINED_AT { get; set; }
    // 파티 ID
    public ulong PARTY_ID { get; set; }
    // 사용자 ID
    public ulong USER_ID { get; set; }
}
