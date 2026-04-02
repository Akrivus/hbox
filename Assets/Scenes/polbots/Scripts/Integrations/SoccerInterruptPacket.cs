using System;
using System.Collections.Generic;

public sealed class SoccerInterruptPacket
{
    public string PacketId;
    public string MatchId;
    public string TriggerEventType;
    public SoccerPacketBank Bank;
    public SoccerInterruptPriority Priority;
    public DateTime CreatedAtUtc;
    public DateTime ExpiresAtUtc;
    public SoccerEventSummary SeedEvent;
    public long Sequence;
    public string WorldFingerprint;
    public bool IsScoreSensitive;
    public bool IsTemplatePacket;
    public List<ChatNode> Nodes = new List<ChatNode>();
    public bool Consumed;
    public bool Superseded;
}
