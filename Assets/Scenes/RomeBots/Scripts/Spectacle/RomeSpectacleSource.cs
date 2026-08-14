using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public sealed class RomeSpectacleSource : MonoBehaviour
{
    [SerializeField]
    private ChatGenerator generator;

    [SerializeField]
    private string statePath = "romebots-spectacle-state.json";

    [SerializeField]
    private bool showDebugUi = true;

    private readonly RomeCrierService crier = new RomeCrierService();
    private IRomeVenueService activeVenueService;
    private RomeSpectacleState state;
    private List<RomeSpotlightCandidate> currentCandidates = new List<RomeSpotlightCandidate>();
    private RomeSpotlightCandidate currentWinner;
    private RomePerformanceSlot currentSlot;
    private string status = "Rome spectacle prototype idle.";

    public RomeSpectacleState State => state;

    private void Awake()
    {
        activeVenueService = new RomeCuriaService();
        ResolveGenerator();
        LoadState();
    }

    public void LoadState()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                state = RomeSpectacleState.CreateDefault();
                status = "Created default Rome spectacle state.";
                return;
            }

            var json = File.ReadAllText(statePath);
            state = JsonConvert.DeserializeObject<RomeSpectacleState>(json) ?? RomeSpectacleState.CreateDefault();
            if (state.activeVenue != RomeVenue.Curia)
                state.activeVenue = RomeVenue.Curia;
            status = $"Loaded Rome spectacle state from {statePath}.";
        }
        catch (Exception e)
        {
            state = RomeSpectacleState.CreateDefault();
            status = $"State load failed; created default state. {e.Message}";
            Debug.LogWarning($"RomeSpectacleSource.LoadState failed: {e}");
        }
    }

    public void SaveState()
    {
        try
        {
            File.WriteAllText(statePath, JsonConvert.SerializeObject(state, Formatting.Indented));
            status = $"Saved Rome spectacle state to {statePath}.";
        }
        catch (Exception e)
        {
            status = $"State save failed: {e.Message}";
            Debug.LogWarning($"RomeSpectacleSource.SaveState failed: {e}");
        }
    }

    public void GenerateCuriaDebate()
    {
        EnsureState();
        state.activeVenue = RomeVenue.Curia;
        state.currentPhase = "CuriaDebate";
        currentCandidates = activeVenueService.GenerateCandidates(state, ResolveActors()).ToList();
        currentWinner = null;
        currentSlot = null;

        var idea = activeVenueService.BuildDebateIdea(state, currentCandidates);
        QueueIdea(idea);
        status = $"Generated Curia debate with {currentCandidates.Count} spotlight candidates.";
    }

    public void ResolveWinner()
    {
        EnsureState();
        if (currentCandidates == null || currentCandidates.Count == 0)
            currentCandidates = activeVenueService.GenerateCandidates(state, ResolveActors()).ToList();

        currentWinner = activeVenueService.ResolveWinner(state, currentCandidates);
        if (currentWinner == null)
        {
            status = "No Curia winner resolved.";
            return;
        }

        state.currentLeadActor = currentWinner.proposerActor;
        state.recentSpotlightWinners.Add(currentWinner.proposerActor);
        while (state.recentSpotlightWinners.Count > 12)
            state.recentSpotlightWinners.RemoveAt(0);

        status = $"Curia winner resolved: {currentWinner.proposerActor} ({currentWinner.title}).";
    }

    public void QueueLeadScene()
    {
        EnsureState();
        if (currentWinner == null)
            ResolveWinner();
        if (currentWinner == null)
            return;

        state.currentPhase = "LeadScene";
        currentSlot = activeVenueService.BuildPerformanceSlot(state, currentWinner, currentCandidates);
        ApplySlotMutations(currentSlot);
        QueueIdea(activeVenueService.BuildLeadSceneIdea(state, currentSlot));
        status = $"Queued lead scene for {currentSlot.leadActor}.";
    }

    public void WriteCrierRecap()
    {
        EnsureState();
        if (currentSlot == null)
        {
            if (currentWinner == null)
                ResolveWinner();
            currentSlot = activeVenueService.BuildPerformanceSlot(state, currentWinner, currentCandidates);
        }

        if (currentSlot == null)
        {
            status = "No performance slot available for Acta entry.";
            return;
        }

        var entry = crier.WriteEntry(state, currentWinner, currentSlot);
        state.currentPhase = "CrierRecap";
        SaveState();
        status = entry.crierText;
    }

    private void ApplySlotMutations(RomePerformanceSlot slot)
    {
        if (slot == null)
            return;

        state.publicMood += currentWinner != null ? Math.Sign(currentWinner.popularity - currentWinner.opposition) : 0;
        state.senateMood += currentWinner != null ? Math.Sign(currentWinner.support - currentWinner.chaos) : 0;
        state.treasury += currentWinner != null ? Math.Sign(currentWinner.wealth - 2) : 0;
        state.chaos = Mathf.Clamp(state.chaos + (currentWinner?.chaos ?? 0) - 1, 0, 10);

        if (!string.IsNullOrWhiteSpace(slot.unresolvedRisk) && !state.unresolvedHooks.Contains(slot.unresolvedRisk))
            state.unresolvedHooks.Add(slot.unresolvedRisk);
        while (state.unresolvedHooks.Count > 12)
            state.unresolvedHooks.RemoveAt(0);
    }

    private void QueueIdea(Idea idea)
    {
        ResolveGenerator();
        if (generator == null)
        {
            Debug.Log($"RomeSpectacleSource generated idea without ChatGenerator:\n{idea?.Prompt}");
            return;
        }

        generator.AddIdeaToQueue(idea);
    }

    private Actor[] ResolveActors()
    {
        ResolveGenerator();
        var context = generator?.ManagerContext ?? (ChatManager.Instance != null ? ChatManagerContext.Current : null);
        return context?.Actors ?? Array.Empty<Actor>();
    }

    private void ResolveGenerator()
    {
        if (generator != null)
            return;

        generator = GetComponent<ChatGenerator>() ??
            GetComponentInParent<ChatGenerator>() ??
            FindObjectOfType<ChatGenerator>();
    }

    private void EnsureState()
    {
        if (state == null)
            state = RomeSpectacleState.CreateDefault();
    }

    private void OnGUI()
    {
        if (!showDebugUi)
            return;

        GUILayout.BeginArea(new Rect(20, 20, 360, 340), GUI.skin.box);
        GUILayout.Label("Rome Spectacle Prototype");
        GUILayout.Label(status, GUI.skin.textArea);
        if (GUILayout.Button("Load State"))
            LoadState();
        if (GUILayout.Button("Save State"))
            SaveState();
        if (GUILayout.Button("Generate Curia Debate"))
            GenerateCuriaDebate();
        if (GUILayout.Button("Resolve Winner"))
            ResolveWinner();
        if (GUILayout.Button("Queue Lead Scene"))
            QueueLeadScene();
        if (GUILayout.Button("Write Crier Recap"))
            WriteCrierRecap();

        if (state != null)
        {
            GUILayout.Space(8);
            GUILayout.Label($"Arc: {state.arcTitle}");
            GUILayout.Label($"Venue: {state.activeVenue} | Phase: {state.currentPhase}");
            GUILayout.Label($"Lead: {(string.IsNullOrWhiteSpace(state.currentLeadActor) ? "None" : state.currentLeadActor)}");
            GUILayout.Label($"Acta Entries: {state.actaEntries?.Count ?? 0}");
        }
        GUILayout.EndArea();
    }
}
