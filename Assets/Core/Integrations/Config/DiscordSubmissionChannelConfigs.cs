using System;

public sealed class DiscordSubmissionChannelConfigs
{
    public bool Enabled { get; set; } = true;
    public bool ImagesContextOnly { get; set; }
    public string GeneratorSlug { get; set; }
    private string promptRoot = "Discord Submissions";
    public string PromptRoot
    {
        get => promptRoot;
        set => promptRoot = value == "Discord Tips" ? "Discord Submissions" : value;
    }
    // Omit to post polls in the incoming submissions channel.
    public string PitchChannelId { get; set; }
    public int PitchExpirationMinutes { get; set; } = 180;
    public int PitchMinimumVotesToQueue { get; set; } = 1;
    public int PitchApprovalScore { get; set; } = 1;

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(GeneratorSlug) || string.IsNullOrWhiteSpace(PromptRoot) ||
            PitchExpirationMinutes < 1 || PitchMinimumVotesToQueue < 0 || PitchApprovalScore < 1)
            throw new ArgumentException("Enabled Discord submissions routes need a generator, prompt root and valid pitch vote settings.");
    }
}
