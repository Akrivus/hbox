public class SentimentAnalyzer : SentimentTagger
{
    public override bool IsBlocking => true;

    public override void AfterTagging(string output, ChatNode node)
    {
        var edit = output.Find("Edit");
        if (!string.IsNullOrEmpty(edit))
            node.SetText(edit);
        node.Thoughts = output.Find("Delivery");
    }
}