using System;

[Serializable]
public sealed class ReplayVoteRequest
{
    public string channelKey;
    public string slug;
    public int delta;
    public int upDelta;
    public int downDelta;
    public string source;
    public string messageId;
}

[Serializable]
public sealed class ReplayDiscordBinding
{
    public string channelKey;
    public string slug;
    public string discordMessageId;
    public string discordChannelId;
}
