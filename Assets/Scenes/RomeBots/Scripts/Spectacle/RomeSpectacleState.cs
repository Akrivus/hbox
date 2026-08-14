using System;
using System.Collections.Generic;

[Serializable]
public sealed class RomeSpectacleState
{
    public string activeArcId;
    public string arcTitle;
    public RomeVenue activeVenue = RomeVenue.Curia;
    public string currentPhase = "CuriaDebate";
    public string currentLeadActor;
    public int publicMood;
    public int senateMood;
    public int treasury;
    public int grain;
    public int chaos;
    public string activeCrisis;
    public List<string> recentSpotlightWinners = new List<string>();
    public List<string> unresolvedHooks = new List<string>();
    public List<RomeActaLog.ActaEntry> actaEntries = new List<RomeActaLog.ActaEntry>();

    public static RomeSpectacleState CreateDefault()
    {
        return new RomeSpectacleState
        {
            activeArcId = Guid.NewGuid().ToString("N").Substring(0, 8),
            arcTitle = "The Year of Bad Omens",
            activeVenue = RomeVenue.Curia,
            currentPhase = "CuriaDebate",
            publicMood = 0,
            senateMood = 0,
            treasury = 5,
            grain = 5,
            chaos = 2,
            activeCrisis = "The gods have gone quiet, which everyone agrees is suspicious.",
            unresolvedHooks = new List<string>
            {
                "The crowd wants proof that Rome is still entertaining.",
                "The Senate wants procedure to look like destiny."
            }
        };
    }
}
