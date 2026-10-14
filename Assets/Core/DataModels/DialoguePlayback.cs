using System;

// Immutable snapshot taken when an AudioSource actually starts, not when a node is activated.
public sealed class DialoguePlayback
{
    public string PlaybackId { get; }
    public string ChannelKey { get; }
    public string SourceChatId { get; }
    public int SourceNodeIndex { get; }
    public string SourceMatchId { get; }
    public string SourceInterruptId { get; }
    public string Speaker { get; }
    public string SpokenText { get; }
    public string OriginalDialogue { get; }
    public DateTimeOffset StartedAt { get; }
    public double StartedDspTime { get; }
    public DateTimeOffset? CompletedAt { get; }
    public double? CompletedDspTime { get; }

    public DialoguePlayback(string channelKey, string sourceChatId, int sourceNodeIndex,
        string sourceMatchId, string sourceInterruptId, string speaker, string spokenText,
        string originalDialogue, DateTimeOffset startedAt, double startedDspTime,
        string playbackId = null, DateTimeOffset? completedAt = null, double? completedDspTime = null)
    {
        PlaybackId = playbackId ?? Guid.NewGuid().ToString("N");
        ChannelKey = channelKey;
        SourceChatId = sourceChatId;
        SourceNodeIndex = sourceNodeIndex;
        SourceMatchId = sourceMatchId;
        SourceInterruptId = sourceInterruptId;
        Speaker = speaker;
        SpokenText = spokenText;
        OriginalDialogue = originalDialogue;
        StartedAt = startedAt;
        StartedDspTime = startedDspTime;
        CompletedAt = completedAt;
        CompletedDspTime = completedDspTime;
    }

    public DialoguePlayback Complete(DateTimeOffset at, double dspTime) =>
        new DialoguePlayback(ChannelKey, SourceChatId, SourceNodeIndex, SourceMatchId,
            SourceInterruptId, Speaker, SpokenText, OriginalDialogue, StartedAt,
            StartedDspTime, PlaybackId, at, dspTime);
}

// Conservative natural-end detector, shared by Unity polling and standalone regressions.
// A coroutine returning, an activation event, or a silent/missing clip cannot satisfy it.
public sealed class DialogueAudioCompletion
{
    private readonly double startedDspTime;
    private readonly double duration;
    private bool ended;

    public DialogueAudioCompletion(double startedDspTime, double duration)
    {
        this.startedDspTime = startedDspTime;
        this.duration = duration;
    }

    public bool Observe(bool isPlaying, bool playbackValid, double dspTime)
    {
        if (ended) return false;
        if (!playbackValid || duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration))
        {
            ended = true;
            return false;
        }
        if (isPlaying) return false;
        ended = true;
        return dspTime >= startedDspTime + duration;
    }

    public bool Ended => ended;
}
