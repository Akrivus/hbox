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
public sealed class PitchVoteRequest
{
    public string channelKey;
    public string id;
    public int delta;
    public int upDelta;
    public int downDelta;
    public string source;
}

[Serializable]
public sealed class ReplayDiscordBinding
{
    public string channelKey;
    public string slug;
    public string discordMessageId;
    public string discordChannelId;
}
