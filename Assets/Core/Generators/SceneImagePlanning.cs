using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public sealed class SceneImageSurfaceInfo
{
    public string Id;
    public string Description;
}

public static class SceneImagePlanning
{
    public static async Task GenerateAsync(Chat chat, SceneImageSurfaceInfo[] surfaces)
    {
        var images = chat.Idea?.Images;
        if (images == null || images.Count == 0)
            return;
        chat.GeneratedData[SceneImagePlan.Key] = JsonConvert.SerializeObject(new SceneImagePlan());
        foreach (var image in images.Where(image => image != null))
            image.PresentationEligible = false;
        try
        {
            if (surfaces.Length == 0 || chat.Nodes.Count == 0 || chat.Nodes.Count > 512 ||
                !images.Any(image => image != null && !image.ContextOnly && !string.IsNullOrWhiteSpace(image.Analysis) && !string.IsNullOrEmpty(image.DataBase64)))
                return;
            var catalog = images.Where(image => image != null).Take(ImageAttachmentContext.MaxImages).Select(image => new
            {
                image.Id, image.SourceLabel, image.SourceUrl, image.Caption, image.ContextOnly,
                Available = !string.IsNullOrEmpty(image.DataBase64) && !string.IsNullOrWhiteSpace(image.Analysis),
                Analysis = ImageAttachmentContext.Limit(image.Analysis, ImageAttachmentContext.MaxAnalysisChars)
            });
            var nodes = chat.Nodes.Select((node, index) => new { Index = index, Actor = node.Actor?.Name, Text = node.Text ?? node.Line });
            var prompt = new PromptResolver(chat.ManagerContext, "Source Images", "Presentation");
            await prompt.Resolve(chat.Idea.Prompt, chat.Topic, JsonConvert.SerializeObject(catalog), JsonConvert.SerializeObject(nodes), JsonConvert.SerializeObject(surfaces));
            
            // The presentation prompt owns its context wording; avoid the context-only generation wrapper.
            var request = new Chat { ManagerContext = chat.ManagerContext, Key = chat.Key, FileName = chat.FileName, Idea = new Idea(chat.Idea.Prompt) };
            var json = await LLM.CompleteAsync(prompt, request, LlmProfile.SceneReasoning);
            var plan = SceneImagePlan.Approve(json, images, chat.Nodes.Count, surfaces.Select(surface => surface.Id));
            chat.GeneratedData[SceneImagePlan.Key] = JsonConvert.SerializeObject(plan);
        }
        catch (Exception e)
        {
            foreach (var image in images.Where(image => image != null))
                image.PresentationEligible = false;
            chat.GeneratedData[SceneImagePlan.Key] = JsonConvert.SerializeObject(new SceneImagePlan());
            Debug.LogWarning("Scene image presentation omitted: " + e.Message);
        }
    }
}
