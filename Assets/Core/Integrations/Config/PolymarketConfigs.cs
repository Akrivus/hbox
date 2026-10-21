using System.Collections.Generic;

public class PolymarketConfigs : WebSourceConfigs
{
    public override string Type => "polymarket";
    public List<string> TagSlugs { get; set; } = new List<string> { "politics" };
    public int DiscoveryPageSize { get; set; } = 50;
    public int DiscoveryPages { get; set; } = 2;
    public double MinLiquidity { get; set; } = 1000;
    public double MinVolume24hr { get; set; } = 1000;
    public float RepeatCooldownHours { get; set; } = 24;
    public float RequestSpacingSeconds { get; set; } = 1;
    public int MaxRequestAttempts { get; set; } = 3;

    public PolymarketConfigs()
    {
        EnablePitchGate = true;
        BatchSize = 5;
        BatchSizeLimit = 2;
        BatchPeriodOffset = "00:02";
        BatchPeriodInMinutes = 30;
    }
}
