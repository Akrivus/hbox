using System;
using System.Collections.Generic;

// Match association is frozen at audio start. Completion in a later match cannot migrate a quote.
public sealed class SoccerReactionCapture
{
    private string matchId;
    private string channelKey;
    private readonly Dictionary<string, DialoguePlayback> pending = new Dictionary<string, DialoguePlayback>();
    private readonly List<SoccerReactionRecord> reactions = new List<SoccerReactionRecord>();

    public void BeginMatch(string id, string channel)
    {
        EndMatch();
        matchId = id;
        channelKey = channel;
    }

    public void Started(DialoguePlayback playback)
    {
        if (playback == null || string.IsNullOrWhiteSpace(matchId) || playback.ChannelKey != channelKey ||
            (!string.IsNullOrEmpty(playback.SourceMatchId) && playback.SourceMatchId != matchId) ||
            string.IsNullOrWhiteSpace(playback.Speaker) || string.IsNullOrWhiteSpace(playback.SpokenText)) return;
        pending[playback.PlaybackId] = playback;
    }

    public void Completed(DialoguePlayback playback)
    {
        if (playback == null || string.IsNullOrEmpty(matchId) ||
            !pending.TryGetValue(playback.PlaybackId, out var started)) return;
        pending.Remove(playback.PlaybackId);
        if (!playback.CompletedAt.HasValue || !playback.CompletedDspTime.HasValue ||
            playback.CompletedAt.Value < started.StartedAt || playback.CompletedDspTime.Value < started.StartedDspTime) return;
        reactions.Add(new SoccerReactionRecord
        {
            MatchId = matchId, PlaybackId = started.PlaybackId, Speaker = started.Speaker,
            SpokenText = started.SpokenText, OriginalDialogue = started.OriginalDialogue,
            SourceChatId = started.SourceChatId, SourceNodeIndex = started.SourceNodeIndex,
            SourceInterruptId = started.SourceInterruptId, StartedAt = started.StartedAt,
            CompletedAt = playback.CompletedAt.Value, StartedDspTime = started.StartedDspTime,
            CompletedDspTime = playback.CompletedDspTime.Value
        });
    }

    public SoccerReactionRecord[] Snapshot() => reactions.ToArray();

    public void EndMatch()
    {
        matchId = null;
        channelKey = null;
        pending.Clear();
        reactions.Clear();
    }
}
