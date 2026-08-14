using System;
using System.Collections.Generic;

[Serializable]
public sealed class RomePerformanceSlot
{
    public string id;
    public RomeVenue venue;
    public string leadActor;
    public string authoritySource;
    public string scenePremise;
    public string[] supportingCast = Array.Empty<string>();
    public string expectedMutation;
    public string unresolvedRisk;
    public List<string> stateMutations = new List<string>();
}
