using System;

static class Tests
{
    static void Main()
    {
        Check(UiVisibilityPolicy.Resolve(true, false, false, false), true, true, true, "call replay");
        Check(UiVisibilityPolicy.Resolve(true, false, true, false), true, true, false, "new episode hides feed");
        Check(UiVisibilityPolicy.Resolve(true, false, true, true), true, true, true, "queue empty restores feed");
        Check(UiVisibilityPolicy.Resolve(false, false, false, false), false, true, true, "globe keeps its prior title/feed behavior");
        Check(UiVisibilityPolicy.Resolve(true, true, false, true), false, false, false, "soccer overrides queue-empty and replay visibility");
        Check(UiVisibilityPolicy.Resolve(true, false, true, false), true, true, false, "leaving soccer during new episode keeps feed hidden");
        Check(UiVisibilityPolicy.Resolve(false, false, false, false), false, true, true, "leaving soccer into globe keeps call overlay hidden");
        Check(UiVisibilityPolicy.Resolve(false, true, true, false), false, false, false, "overlapping globe and soccer suppression");
        Console.WriteLine("8 UI visibility scenarios passed.");
    }

    static void Check(UiVisibilityState state, bool call, bool title, bool feed, string scenario)
    {
        if (state.CallOverlayVisible != call || state.EpisodeTitleAllowed != title || state.EventFeedVisible != feed)
            throw new Exception("Visibility regression: " + scenario);
    }
}
