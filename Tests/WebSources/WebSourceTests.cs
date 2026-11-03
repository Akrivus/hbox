using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class Tests
{
    private static int checks;
    static async Task Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var sandbox = Path.Combine(root, "Temp", "WebSourceTests", "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        Directory.SetCurrentDirectory(sandbox);
        try
        {
            ConfigAndSchedule();
            var document = Parsing();
            await ImageAttachments(root);
            await DiscordSubmissions(root);
            await IndependentImagePlanning(root);
            await CommandPitchGate(root);
            await AutoApprovalControls();
            if (args.Length == 1)
            {
                var events = JArray.Parse(File.ReadAllText(args[0]));
                Check(events.OfType<JObject>().Any(item => PolymarketSource.ParseEvent(item, new PolymarketConfigs(), DateTime.UtcNow) != null), "live Gamma response parses eligible market");
            }
            VotingAndCompatibility(document);
            await Batching(document);
            Console.WriteLine($"Web source tests passed: {checks} checks (image discovery/context/replays, parsing, schedules, legacy persistence, voting, batching and reloads).");
        }
        finally { Directory.SetCurrentDirectory(root); }
    }

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    static ChatManagerContext Context(string key = null)
    {
        var context = new ChatManagerContext();
        if (key != null) context.Key = key;
        context.Generator = new ChatGenerator { ManagerContext = context };
        ChatManagerContext.Current = context;
        return context;
    }

    static async Task ImageAttachments(string root)
    {
        var direct = JObject.Parse(@"{'id':'pic','author':'reporter','subreddit_name_prefixed':'r/news','permalink':'/r/news/pic','url':'https://i.redd.it/test.png','title':'The scene'}");
        var images = RedditImageAttachments.Parse(direct);
        Check(images.Count == 1 && images[0].Id == "reddit:pic:image" && images[0].SourceUrl.EndsWith("/r/news/pic"), "direct Reddit image with source attribution");
        var gallery = JObject.Parse(@"{'id':'gallery','gallery_data':{'items':[{'media_id':'b','caption':'Second'},{'media_id':'a'},{'media_id':'b'},{'media_id':'gif'}]},'media_metadata':{'a':{'status':'valid','e':'Image','s':{'u':'https://preview.redd.it/a.jpg?x=1&amp;y=2'}},'b':{'status':'valid','e':'Image','s':{'u':'https://i.redd.it/b.png'}},'gif':{'status':'valid','e':'AnimatedImage','s':{'gif':'https://i.redd.it/a.gif'}}}}");
        var galleryImages = RedditImageAttachments.Parse(gallery);
        Check(galleryImages.Count == 2 && galleryImages[0].Caption == "Second" && galleryImages[1].Url.Contains("&y=2"), "gallery order, captions, deduplication and HTML entities");
        var crosspost = JObject.Parse(@"{'id':'cross','crosspost_parent_list':[]}");
        ((JArray)crosspost["crosspost_parent_list"]).Add(direct);
        Check(RedditImageAttachments.Parse(crosspost).Single().Id == images[0].Id, "crosspost retains original image identity");
        direct["spoiler"] = true;
        Check(RedditImageAttachments.Parse(direct).Count == 0, "spoiler images omitted");
        direct.Remove("spoiler"); direct["over_18"] = true;
        Check(RedditImageAttachments.Parse(direct).Count == 0, "NSFW images omitted");
        Check(RedditImageAttachments.Parse(JObject.Parse(@"{'id':'article','url':'https://example.com/story','thumbnail':'https://example.com/thumb.png','preview':{'images':[{'source':{'url':'https://example.com/preview.png'}}]}}")).Count == 0, "article thumbnail is not source imagery");
        var preview = JObject.Parse(@"{'id':'preview','url':'https://www.reddit.com/media/123','post_hint':'image','preview':{'images':[{'source':{'url':'https://preview.redd.it/photo.png'}}]}}");
        Check(RedditImageAttachments.Parse(preview).Count == 1, "image-post preview fallback");
        ((JArray)gallery["gallery_data"]["items"]).Clear();
        for (var i = 0; i < 7; i++)
        {
            ((JArray)gallery["gallery_data"]["items"]).Add(JObject.FromObject(new { media_id = i.ToString() }));
            gallery["media_metadata"][i.ToString()] = JObject.Parse("{'status':'valid','e':'Image','s':{'u':'https://i.redd.it/" + i + ".png'}}");
        }
        Check(RedditImageAttachments.Parse(gallery).Count == 4, "image count bounded");
        var bytes = File.ReadAllBytes(Path.Combine(root, "Assets/Scenes/PBNN/Fixtures/LocalImage.bytes"));
        Check(ImageAttachmentService.TryReadImage(bytes, out var mime) && mime == "image/jpeg", "real JPEG fixture accepted");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aO9sAAAAASUVORK5CYII=");
        Check(ImageAttachmentService.TryReadImage(png, out mime) && mime == "image/png", "PNG accepted");
        Check(!ImageAttachmentService.TryReadImage(new byte[ImageAttachmentContext.MaxImageBytes + 1], out _) &&
            !ImageAttachmentService.TryReadImage(System.Text.Encoding.UTF8.GetBytes("<html>This is not an image</html>"), out _), "oversize and non-images rejected");
        var huge = (byte[])png.Clone(); huge[16] = 127;
        Check(!ImageAttachmentService.TryReadImage(huge, out _), "oversized image dimensions rejected");
        var downloads = 0; var analyses = 0;
        Func<string, Task<byte[]>> download = url => { downloads++; return Task.FromResult(bytes); };
        Func<ImageAttachment, Task<string>> analyze = image => { analyses++; return Task.FromResult("Visible label: radar. Timestamp: 14:00. Claims are unverified."); };
        await ImageAttachmentService.PrepareAsync(images, analyze, download);
        await ImageAttachmentService.PrepareAsync(images, analyze, download);
        Check(downloads == 1 && analyses == 1 && images[0].Sha256.Length == 64, "download and vision analysis cached once");
        Check(!images[0].PresentationEligible && images[0].UseForGeneration, "context-only default");
        var description = ImageAttachmentContext.Describe(images);
        Check(description.Contains("radar") && description.Contains(images[0].Id) && !description.Contains(images[0].DataBase64), "bounded textual image context excludes binary");
        var context = Context();
        var source = new WebSourceDocument { id = "pic", title = "Photo", images = images };
        var candidate = PitchCandidateFactory.FromText("Title: Picture\nPitch: A radar story", source, "test-generator", "#stream", 180);
        var service = new PitchCandidateService(context, context.Generator);
        await service.GenerateAsync(source, "Photo", "#stream", 180, "Source");
        Check(LLM.LastPrompt.Contains("radar") && !LLM.LastPrompt.Contains(images[0].DataBase64), "pitch evaluation receives analyzed context without base64");
        var store = PitchCandidateStore.GetOrCreate(context, context.Generator);
        store.Save(candidate);
        var restarted = Context(context.Key);
        PitchCandidateStore.GetOrCreate(restarted, restarted.Generator);
        var savedCandidate = PitchCandidateStore.GetPitchStatus(restarted.Key).Single(value => value.id == candidate.id);
        Check(savedCandidate.source.images.Single().DataBase64 == images[0].DataBase64, "pitch restart reloads cached image bytes from disk");
        ChatManagerContext.Current = context;
        var idea = PitchToIdeaConverter.Convert(savedCandidate);
        var generated = new Chat(idea, context);
        generated.Nodes.Add(new ChatNode { Actor = new ActorContext { Name = "Reporter" }, Text = "Discuss the image" });
        var displayPlan = SceneImagePlan.Approve("{'Version':1,'Cues':[{'ImageId':'" + idea.Images[0].Id + "','SurfaceId':'news-tv','StartNode':0,'EndNodeExclusive':1,'Caption':'Image','Reason':'Discussed'}]}", idea.Images, 1, new[] { "news-tv" });
        generated.GeneratedData[SceneImagePlan.Key] = JsonConvert.SerializeObject(displayPlan);
        Check(generated.BuildRecallQuery().Contains("radar"), "image context participates in generation memory recall");
        var replayJson = JsonConvert.SerializeObject(generated);
        var replay = JsonConvert.DeserializeObject<Chat>(replayJson);
        var asset = replay.Idea.Images.Single();
        Check(SceneImagePlan.Read(replay.GeneratedData, replay.Idea.Images, replay.Nodes.Count).AtNode("news-tv", 0).ImageId == asset.Id && asset.PresentationEligible, "production Chat persists approved presentation plan against original image IDs");
        Check(asset.DataBase64 == Convert.ToBase64String(bytes) && asset.Analysis == images[0].Analysis && asset.Id == images[0].Id && asset.SourceUrl == images[0].SourceUrl, "production Chat replay preserves bytes, identity, analysis and provenance");
        Check(!replayJson.Contains("data:image"), "computed data URL is not redundantly serialized");
        Check(JsonConvert.DeserializeObject<Chat>("{\"Idea\":{\"Prompt\":\"Old replay\"}}").Idea.Images.Count == 0, "old replay defaults to empty attachments");
        var batch = new FakeSource { Images = images };
        batch.Boot();
        batch.Configure(new PolymarketConfigs { EnablePitchGate = false, BatchSize = 1, BatchSizeLimit = 1 });
        await batch.FetchIdeas();
        Check(context.Generator.Ideas.Count == 1 && context.Generator.Ideas.Last().Images.Single().DataBase64 == images[0].DataBase64, "explicit gate-off batch queues directly with attachments");
        batch.Configure(new PolymarketConfigs { EnablePitchGate = true, PitchAutoApprovalBatchSize = 1, BatchSize = 1, BatchSizeLimit = 1 });
        await batch.FetchIdeas();
        Check(context.Generator.Ideas.Count == 2 && context.Generator.Ideas.Last().Images.Single().Analysis == images[0].Analysis, "configured auto-approval queues an evaluated pitch with analyzed attachments");
        images[0].UseForGeneration = false;
        Check(ImageAttachmentContext.Describe(images) == "", "context opt-out independent of bytes");
        images[0].UseForGeneration = true;
        var failed = new[] { new ImageAttachment { Id = "bad", Url = "bad" }, new ImageAttachment { Id = "good", Url = "good" } };
        await ImageAttachmentService.PrepareAsync(failed, analyze, url => url == "bad" ? throw new IOException("unavailable") : Task.FromResult(bytes));
        Check(failed[0].DownloadError == "unavailable" && failed[1].Analysis != null && ImageAttachmentContext.Describe(failed).Contains("unavailable"), "failed image does not block other assets");
        var analysisFailure = new[] { new ImageAttachment { Id = "vision-failure", Url = "good" } };
        await ImageAttachmentService.PrepareAsync(analysisFailure, _ => throw new Exception("vision failed"), download);
        Check(analysisFailure[0].DataBase64 != null && analysisFailure[0].AnalysisError == "vision failed", "vision failure preserves replay bytes");
        Check(!ImageAttachmentService.IsPublicAddress(System.Net.IPAddress.Parse("127.0.0.1")) &&
            !ImageAttachmentService.IsPublicAddress(System.Net.IPAddress.Parse("192.168.1.1")) &&
            !ImageAttachmentService.IsPublicAddress(System.Net.IPAddress.Parse("::ffff:169.254.169.254")), "private image endpoints rejected");
        try { await ImageAttachmentService.DownloadAsync("file:///secret.png"); Check(false, "local file download must fail"); }
        catch (InvalidDataException) { checks++; }
    }

    static async Task DiscordSubmissions(string root)
    {
        var context = Context("submissions-" + Guid.NewGuid().ToString("N"));
        context.Generator.slug = context.Key + "-default";
        var settings = new DiscordSubmissionChannelConfigs { GeneratorSlug = context.Generator.slug, PitchMinimumVotesToQueue = 2, PitchApprovalScore = 2 };
        settings.Validate();
        var legacyHistory = $"discord-tips-{context.Generator.slug}.json";
        File.WriteAllText(legacyHistory, "[\"discord:legacy-rejected\"]");
        File.WriteAllText($"discord-submissions-{context.Generator.slug}.json", "[\"discord:canonical-rejected\"]");
        var image = new ImageAttachment { Id = "discord:image", DataBase64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "Assets/Scenes/PBNN/Fixtures/LocalImage.bytes"))), Analysis = "Readable screenshot text", SourceUrl = "https://discord.com/channels/g/c/m" };
        var source = new WebSourceDocument { provider = "discord", id = "submission-1", title = "A suggestion", selftext = "A premise for any world", url = "https://discord.com/channels/g/c/submission-1", author = "author", images = new List<ImageAttachment> { image } };
        var service = new DiscordSubmissionIntake(context.Generator);
        var callsBeforeHistory = LLM.CandidateCalls;
        await service.SubmitAsync(new WebSourceDocument { provider = "discord", id = "legacy-rejected" }, settings, () => true);
        await service.SubmitAsync(new WebSourceDocument { provider = "discord", id = "canonical-rejected" }, settings, () => true);
        Check(LLM.CandidateCalls == callsBeforeHistory, "legacy and canonical submission histories both prevent regeneration");
        var before = DiscordManager.Posted;
        await service.SubmitAsync(source, settings, () => true);
        var migratedHistory = File.ReadAllText($"discord-submissions-{context.Generator.slug}.json");
        Check(migratedHistory.Contains("legacy-rejected") && migratedHistory.Contains("canonical-rejected") && migratedHistory.Contains("discord:submission-1"), "new submission history preserves both histories plus current message");
        var candidate = PitchCandidateStore.GetPitchStatus(context.Key).Single();
        Check(DiscordManager.Posted == before + 1 && context.Generator.Ideas.Count == 0 && candidate.status == PitchStatus.PostedForVote, "submissions publish through evaluator/voting gate, never directly queue");
        Check(candidate.minimumVotesToQueue == 2 && candidate.approvalScore == 2 && candidate.id == "discord:submission-1", "submission thresholds and stable message identity persist");
        Check(candidate.source.images.Single().Analysis.Contains("screenshot") && !candidate.source.Summary().Contains("karma") && PitchDiscordPublisher.BuildEmbed(candidate).Author.URL == "https://discord.com", "Discord source branding and image context survive pitch");
        var calls = LLM.CandidateCalls;
        await service.SubmitAsync(source, settings, () => true);
        Check(LLM.CandidateCalls == calls && DiscordManager.Posted == before + 1, "duplicate message does not regenerate a pitch");
        var restarted = Context(context.Key); restarted.Generator.slug = settings.GeneratorSlug;
        service = new DiscordSubmissionIntake(restarted.Generator);
        await service.SubmitAsync(source, settings, () => true);
        Check(LLM.CandidateCalls == calls, "submission deduplication survives restart");
        PitchCandidateStore.ApplyVote(restarted.Key, candidate.id, 1, 0, "test");
        Check(restarted.Generator.Ideas.Count == 0, "submission waits for configured voting threshold");
        PitchCandidateStore.ApplyVote(restarted.Key, candidate.id, 1, 0, "test");
        Check(restarted.Generator.Ideas.Count == 1 && restarted.Generator.Ideas[0].Images.Single().DataBase64 == image.DataBase64 && restarted.Generator.Ideas[0].Source == "discord", "winning submission queues an attributed Idea with images");
        source = JObject.FromObject(source).ToObject<WebSourceDocument>(); source.id = "rejected"; LLM.Reject = true;
        await service.SubmitAsync(source, settings, () => true);
        calls = LLM.CandidateCalls;
        service = new DiscordSubmissionIntake(restarted.Generator);
        await service.SubmitAsync(source, settings, () => true);
        Check(LLM.CandidateCalls == calls && DiscordManager.Posted == before + 1, "rejected submission is not posted and remains deduplicated after restart");
        LLM.Reject = false;
        source = JObject.FromObject(source).ToObject<WebSourceDocument>(); source.id = "disabled-midflight";
        LLM.Block = new TaskCompletionSource<bool>();
        var enabled = true;
        var pending = service.SubmitAsync(source, settings, () => enabled);
        enabled = false; LLM.Block.SetResult(true); await pending; LLM.Block = null;
        Check(DiscordManager.Posted == before + 1, "disabled in-flight route does not publish a poll");
        enabled = true;
        await service.SubmitAsync(source, settings, () => enabled);
        var expiring = PitchCandidateStore.GetPitchStatus(restarted.Key).Single(value => value.source.id == source.id);
        expiring.expiresAtUtc = DateTime.UtcNow.AddMinutes(-1).ToString("O");
        expiring.discordBotApplicationId = "app";
        service.Tick();
        Check(expiring.status == PitchStatus.Expired && DiscordBotService.DeletedSubmissions == 1, "submissions poll expiration cleans bot-posted poll without another submission or web batch");
        var other = new ChatGenerator { ManagerContext = restarted, slug = "another-generator" };
        service = new DiscordSubmissionIntake(other);
        settings.GeneratorSlug = other.slug; settings.PitchMinimumVotesToQueue = 1; settings.PitchApprovalScore = 1;
        source = JObject.FromObject(source).ToObject<WebSourceDocument>(); source.id = "other-generator";
        await service.SubmitAsync(source, settings, () => true);
        var otherCandidate = PitchCandidateStore.GetPitchStatus(restarted.Key).Single(value => value.source.id == source.id);
        PitchCandidateStore.ApplyVote(restarted.Key, otherCandidate.id, 1, 0, "test");
        Check(ServerSource.RoutedIdeas.Last().Slug == other.slug && ServerSource.RoutedIdeas.Last().Idea.Source == "discord", "shared pitch store respects submission target generator");
    }

    static async Task IndependentImagePlanning(string root)
    {
        var context = Context("new-world-" + Guid.NewGuid().ToString("N"));
        var image = new ImageAttachment { Id = "image", Analysis = "A source image", SourceLabel = "Source", DataBase64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "Assets/Scenes/PBNN/Fixtures/LocalImage.bytes"))) };
        var chat = new Chat(new Idea("Use a custom scene display") { Images = new List<ImageAttachment> { image } }, context);
        chat.Nodes.Add(new ChatNode { Actor = new ActorContext { Name = "Host" }, Text = "Look at this image" });
        LLM.PresentationOutput = "{'Version':1,'Cues':[{'ImageId':'image','SurfaceId':'custom-display','StartNode':0,'EndNodeExclusive':1,'Caption':'Image','Reason':'Relevant'}]}";
        await SceneImagePlanning.GenerateAsync(chat, new[] { new SceneImageSurfaceInfo { Id = "custom-display", Description = "A scene-provided display" } });
        Check(image.PresentationEligible && SceneImagePlan.Read(chat.GeneratedData, chat.Idea.Images, 1).AtNode("custom-display", 0) != null, "new channel/custom surface plans without a Core channel override");
        Check(LLM.LastArgs[4].ToString().Contains("custom-display") && LLM.LastArgs[2].ToString().Contains("A source image") && !LLM.LastArgs[2].ToString().Contains(image.DataBase64), "planning consumes scene-supplied surfaces and bounded image context");
        image.ContextOnly = true;
        await SceneImagePlanning.GenerateAsync(chat, new[] { new SceneImageSurfaceInfo { Id = "custom-display" } });
        Check(!image.PresentationEligible && SceneImagePlan.Read(chat.GeneratedData, chat.Idea.Images, 1).Cues.Count == 0, "scene extension cannot bypass context-only veto");
    }

    static async Task CommandPitchGate(string root)
    {
        var context = Context("commands-" + Guid.NewGuid().ToString("N"));
        context.Generator.slug = context.Key + "-default";
        var image = new ImageAttachment { Id = "command-photo", Analysis = "Photo", SourceLabel = "Discord /idea", DataBase64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "Assets/Scenes/PBNN/Fixtures/LocalImage.bytes"))), ContextOnly = true };
        var idea = new Idea("Literal {0} command idea", "viewer", "discord") { Images = new List<ImageAttachment> { image } };
        var interaction = JObject.Parse("{'id':'100','guild_id':'guild','channel_id':'channel'}");
        var source = DiscordSubmissionMessage.FromInteraction(interaction, idea);
        var intake = new DiscordSubmissionIntake(context.Generator);
        var policy = new DiscordSubmissionChannelConfigs { GeneratorSlug = context.Generator.slug, PitchMinimumVotesToQueue = 2, PitchApprovalScore = 2 };
        var posts = DiscordManager.Posted;
        await intake.SubmitAsync(source, policy, () => true);
        Check(context.Generator.Ideas.Count == 0 && DiscordManager.Posted == posts + 1, "evaluated command posts to shared gate without generating an episode");
        var candidate = PitchCandidateStore.GetPitchStatus(context.Key).Single();
        PitchCandidateStore.ApplyVote(context.Key, candidate.id, 1, 0, "test");
        Check(context.Generator.Ideas.Count == 0, "command waits until all configured vote thresholds are met");
        PitchCandidateStore.ApplyVote(context.Key, candidate.id, 1, 0, "test");
        var accepted = context.Generator.Ideas.Single();
        Check(accepted.Author == "viewer" && accepted.Source == "discord" && accepted.Prompt.Contains("Literal {0}") && accepted.Images.Single().ContextOnly && accepted.Images[0].DataBase64 == image.DataBase64, "winning command preserves source, literal prompt, bytes and context-only veto");
        Check(accepted.Slug.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && accepted.Slug.Contains("command-100"), "accepted command has a Windows-safe replay filename while pitch IDs stay stable");
        interaction["id"] = "101"; LLM.Reject = true;
        await intake.SubmitAsync(DiscordSubmissionMessage.FromInteraction(interaction, idea), policy, () => true);
        LLM.Reject = false;
        Check(context.Generator.Ideas.Count == 1 && DiscordManager.Posted == posts + 1, "rejected command never posts or becomes a generation job");
        await intake.SubmitAsync(source, policy, () => true);
        Check(context.Generator.Ideas.Count == 1 && DiscordManager.Posted == posts + 1, "redelivered command cannot generate or post twice");
    }

    static async Task AutoApprovalControls()
    {
        var context = Context();
        var source = new FakeSource(); source.Boot();
        source.Configure(new PolymarketConfigs { EnablePitchGate = true, PitchAutoApprovalBatchSize = 1, BatchSize = 4, BatchSizeLimit = 3 });
        var posted = DiscordManager.Posted;
        await source.FetchIdeas(true);
        Check(context.Generator.Ideas.Count == 1 && DiscordManager.Posted == posted + 2, "auto-approval budget applies per batch and overflow goes to voting");
        await source.FetchIdeas(false);
        Check(context.Generator.Ideas.Count == 1 && DiscordManager.Posted == posted + 5, "outside active hours all approved pitches go to voting");
        LLM.Reject = true;
        await source.FetchIdeas(true);
        LLM.Reject = false;
        Check(context.Generator.Ideas.Count == 1 && DiscordManager.Posted == posted + 5, "automatic approval cannot bypass evaluator rejection");
        var calls = LLM.CandidateCalls;
        source.Configure(new PolymarketConfigs { EnablePitchGate = false, PitchAutoApprovalBatchSize = 99, BatchSize = 2, BatchSizeLimit = 2 });
        await source.FetchIdeas(true);
        Check(context.Generator.Ideas.Count == 3 && LLM.CandidateCalls == calls && DiscordManager.Posted == posted + 5, "gate-off testing skips evaluation and voting without consuming auto-approval budget");
        Check(context.Generator.Ideas.Last().Slug.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "direct testing path also uses a valid replay filename");
    }

    static void ConfigAndSchedule()
    {
        var ungated = JsonConvert.DeserializeObject<RedditConfigs>("{'EnablePitchGate':false,'PitchAutoApprovalBatchSize':99}");
        Check(!ungated.EnablePitchGate && ungated.PitchAutoApprovalBatchSize == 99 && JsonConvert.SerializeObject(ungated).Contains("EnablePitchGate") && JsonConvert.SerializeObject(ungated).Contains("PitchAutoApprovalBatchSize"), "pitch gate and auto-approval controls deserialize and persist");
        var legacy = JsonConvert.DeserializeObject<RedditConfigs>("{\"BatchSize\":2,\"BatchSizeLimit\":3,\"BatchPeriodInMinutes\":5,\"SubReddits\":{\"worldnews/top\":\"{0}\"}}");
        Check(legacy.Batch == null && legacy.BatchSize == 2 && legacy.BatchSizeLimit == 3, "legacy flat Reddit config");
        var nested = JsonConvert.DeserializeObject<PolymarketConfigs>("{\"BatchSize\":99,\"Batch\":{\"BatchSize\":3,\"BatchPeriodInMinutes\":15}}");
        Check(nested.Batch.BatchSize == 3 && nested.Batch.BatchPeriodInMinutes == 15, "separate nested batch config");
        var schedule = new WebSourceBatchConfigs { BatchPeriodOffset = "00:02", BatchPeriodInMinutes = 5, ActiveHoursStart = "22:00", ActiveHoursEnd = "06:00" };
        var now = new DateTime(2026, 10, 20, 12, 2, 0);
        Check(schedule.NextRun(now) == now.AddMinutes(5), "strictly future aligned schedule");
        Check(schedule.IsActive(now.Date.AddHours(23)) && schedule.IsActive(now.Date.AddHours(5)) && !schedule.IsActive(now), "overnight active window");
        schedule.BatchPeriodInMinutes = 0;
        try { schedule.Validate(); Check(false, "invalid schedule must fail"); }
        catch (ArgumentException) { checks++; }
        schedule.BatchPeriodInMinutes = float.NaN;
        try { schedule.Validate(); Check(false, "NaN schedule must fail"); }
        catch (ArgumentException) { checks++; }
        Check(WebSourcePrompts.Format("{0}\n{1}", "literal {1}", "date") == "literal {1}\ndate", "source braces stay literal");
    }

    static JObject Event() => JObject.Parse(@"{
      'id':'42','slug':'test-event','title':'An event','active':true,'closed':false,
      'markets':[
        {'id':'bad','question':'Closed?','active':true,'closed':true,'volume24hr':99999},
        {'id':'thin','question':'Thin?','liquidityNum':2,'volume24hr':99999},
        {'id':'good','question':'Will it happen?','active':true,'closed':false,
         'description':'Resolve YES only if the official notice is published. Literal {1}.',
         'resolutionSource':'Official notice','endDate':'2026-12-31T00:00:00Z',
         'outcomes':'[""Yes"",""No""]','outcomePrices':'[""0.65"",""0.35""]',
         'liquidityNum':5000,'volume24hr':8000}
      ]}");

    static WebSourceDocument Parsing()
    {
        var config = new PolymarketConfigs();
        var snapshot = PolymarketSource.ParseEvent(Event(), config, DateTime.UtcNow);
        Check(snapshot != null && snapshot.provider == "polymarket" && snapshot.id == "42", "parse Gamma string arrays");
        Check(snapshot.market.marketId == "good" && snapshot.market.prices[0] == .65 && snapshot.market.resolutionSource == "Official notice", "eligible market selection and rules");
        Check(snapshot.Summary().Contains("65.0") && snapshot.Summary().Contains("Snapshot:"), "snapshot prices and timestamp");
        var item = Event();
        item["markets"][2]["outcomes"] = new JArray("Yes", "No");
        item["markets"][2]["outcomePrices"] = new JArray(.6, .4);
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) != null, "native arrays supported");
        item["markets"][2]["outcomePrices"] = new JArray("NaN", ".4");
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "nonfinite prices rejected");
        item["markets"][2]["outcomePrices"] = "broken JSON";
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "malformed arrays rejected");
        item = Event(); item["closed"] = true;
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "closed event rejected");
        Check(PolymarketSource.BuildEventsUrl(config, "foreign policy", 1).Contains("tag_slug=foreign%20policy") &&
            PolymarketSource.BuildEventsUrl(config, "politics", 1).Contains("offset=50"), "escaped topic and pagination");
        return snapshot;
    }

    static void VotingAndCompatibility(WebSourceDocument document)
    {
        var context = Context();
        var store = PitchCandidateStore.GetOrCreate(context, context.Generator);
        Check(ReferenceEquals(store, PitchCandidateStore.GetOrCreate(context, context.Generator)), "adapters share one store");
        var reddit = PitchCandidateFactory.FromText("Title: Reddit card\nPitch: A receipt\nCast: France", new RedditPostSource { id = "42", title = "Post", subreddit = "r/test", permalink = "/r/test/42", selftext = "Raw post", threadContext = "Dialogue" }, "test-generator", "#stream", 180);
        var poly = PitchCandidateFactory.FromText("Title: Market card\nPitch: Another receipt\nCast: Germany", document, "test-generator", "#stream", 180);
        Check(reddit.id != poly.id, "provider IDs cannot collide");
        reddit.status = poly.status = PitchStatus.PostedForVote;
        reddit.minimumVotesToQueue = 1; reddit.approvalScore = 2;
        poly.minimumVotesToQueue = 3; poly.approvalScore = 1;
        store.Save(reddit); store.Save(poly);
        PitchCandidateStore.ApplyVote(context.Key, reddit.id, 1, 0, "test");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 1, 0, "test");
        Check(context.Generator.Ideas.Count == 0, "each source enforces its thresholds");
        PitchCandidateStore.ApplyVote(context.Key, reddit.id, 1, 0, "test");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 2, 0, "test");
        Check(context.Generator.Ideas.Count == 2 && reddit.status == PitchStatus.QueuedAsIdea && poly.status == PitchStatus.QueuedAsIdea, "mixed-source approval queues both");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 9, 0, "test");
        store.QueueCandidate(poly);
        Check(context.Generator.Ideas.Count == 2, "no duplicate delivery after approval");
        var idea = context.Generator.Ideas.Single(value => value.Source == "polymarket");
        Check(idea.Prompt.Contains("65.0") && idea.Prompt.Contains("official notice") && idea.Prompt.Contains("not facts"), "market rules and uncertainty survive conversion");
        var redditEmbed = PitchDiscordPublisher.BuildEmbed(reddit);
        var polyEmbed = PitchDiscordPublisher.BuildEmbed(poly);
        Check(redditEmbed.Author.Name == "From Reddit" && polyEmbed.Author.Name == "From Polymarket", "embed attribution");
        Check(redditEmbed.Color == 0xFF4500 && polyEmbed.Color == 0x2E5CFF && redditEmbed.Author.Icon != polyEmbed.Author.Icon, "brand colors and logos");
        Check(!polyEmbed.Description.Contains("karma") && polyEmbed.Description.Contains("24h volume"), "provider-specific embed metrics");
        var persisted = JsonConvert.DeserializeObject<PitchCandidate>(JsonConvert.SerializeObject(poly));
        Check(persisted.minimumVotesToQueue == 3 && persisted.source.market.prices[0] == .65, "policy and snapshot persist");
        var secondWindow = PitchCandidateFactory.FromText("Title: New snapshot", document, "test-generator", "#stream", 180);
        Check(secondWindow.id != poly.id, "repeat event gets a distinct voting window");
        var restarted = Context(context.Key);
        var restartedStore = PitchCandidateStore.GetOrCreate(restarted, restarted.Generator);
        var reloaded = PitchCandidateStore.GetPitchStatus(restarted.Key).Single(value => value.id == poly.id);
        Check(!ReferenceEquals(store, restartedStore) && reloaded.minimumVotesToQueue == 3 && reloaded.approvalScore == 1, "restart retains independent vote policies");

        var anotherShow = Context();
        var anotherStore = PitchCandidateStore.GetOrCreate(anotherShow, anotherShow.Generator);
        var repeatedReddit = PitchCandidateFactory.FromText("Title: Other show", reddit.source, "test-generator", "#stream", 180);
        anotherStore.Save(repeatedReddit);
        Check(PitchCandidateStore.GetPitchStatus(anotherShow.Key).Any(value => value.id == repeatedReddit.id), "same Reddit ID remains visible in another show");

        var oldContext = Context();
        File.WriteAllText($"reddit-pitches-{oldContext.Key}.json", @"{'candidates':[{'id':'old','source':{'id':'old','title':'Legacy','subreddit':'r/legacy','permalink':'/r/legacy/old'},'status':1,'expiresAtUtc':'2099-01-01T00:00:00Z'}]}");
        var oldStore = PitchCandidateStore.GetOrCreate(oldContext, oldContext.Generator);
        oldStore.ConfigureVoting(1, 2);
        var old = PitchCandidateStore.GetPitchStatus(oldContext.Key).Single();
        Check(old.source.Provider == "reddit" && old.id == "old" && old.source.SourceUrl.Contains("reddit.com/r/legacy"), "legacy source shape and ID preserved");
        PitchCandidateStore.ApplyVote(oldContext.Key, "old", 2, 0, "test");
        Check(oldContext.Generator.Ideas.Count == 1, "legacy pitches still vote and queue");

        var expired = PitchCandidateFactory.FromText("Title: Expired", document, "test-generator", "#stream", 180);
        expired.id = "expired-policy"; expired.status = PitchStatus.PostedForVote;
        expired.minimumVotesToQueue = 3; expired.approvalScore = 1;
        expired.upVotes = 2; expired.expiresAtUtc = DateTime.UtcNow.AddMinutes(-1).ToString("O");
        store.Save(expired); store.ResolveFinishedVotes(0);
        Check(expired.status == PitchStatus.Expired, "expiration uses per-pitch vote minimum");
    }

    static async Task Batching(WebSourceDocument document)
    {
        var context = Context();
        var source = new FakeSource(); source.Boot();
        source.Configure(new PolymarketConfigs { Batch = new WebSourceBatchConfigs { BatchSize = 10, BatchSizeLimit = 2, BatchIterations = 2 }, EnablePitchGate = true });
        var before = DiscordManager.Posted;
        await source.FetchIdeas();
        Check(source.Accepted == 2 && DiscordManager.Posted - before == 2, "strict limit inside fetched batch");
        Check(context.Generator.Ideas.Count == 0, "zero autoapproval means every candidate gets a vote");

        source.Configure(new PolymarketConfigs { BatchSize = 2, BatchSizeLimit = 1, PitchAutoApprovalBatchSize = 1 });
        await source.FetchIdeas(false);
        Check(context.Generator.Ideas.Count == 0, "outside active hours keeps autoapproval off");
        await source.FetchIdeas(true);
        Check(context.Generator.Ideas.Count == 1, "active window permits the configured auto-approval budget");

        LLM.Block = new TaskCompletionSource<bool>();
        var running = source.FetchIdeas();
        var fetches = source.Fetches;
        await source.FetchIdeas();
        Check(source.Fetches == fetches && source.IsBatchRunning, "concurrent manual drop ignored");
        source.Configure(new PolymarketConfigs { Enabled = false });
        LLM.Block.SetResult(true); await running; LLM.Block = null;
        Check(!source.IsBatchRunning && context.Generator.Ideas.Count == 1, "reload cancels delivery from in-flight generation");

        source.Configure(new PolymarketConfigs { BatchSize = 2, BatchSizeLimit = 2 });
        LLM.Reject = true; var acceptedBefore = source.Accepted;
        await source.FetchIdeas(); LLM.Reject = false;
        Check(source.Accepted == acceptedBefore, "evaluator rejection never enters seen history");
        source.Configure(new PolymarketConfigs { EnablePitchGate = false, BatchSize = 2, BatchSizeLimit = 1 });
        await source.FetchIdeas();
        Check(context.Generator.Ideas.Count == 2 && context.Generator.Ideas.Last().Prompt.Contains("Literal {1}") && context.Generator.Ideas.Last().Source == "polymarket", "gate-off config directly queues source-preserving ideas");

        var poly = new PolymarketSource();
        typeof(WebSource).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(poly, null);
        poly.Configure(new PolymarketConfigs()); poly.RememberAccepted(document);
        Check(File.ReadAllText($"polymarket-{context.Key}.json").Contains("42"), "Polymarket accepted event history persists");
        Check(!UnityEngine.Debug.Warnings.Any(), "batch execution has no swallowed failures: " + string.Join("; ", UnityEngine.Debug.Warnings));
    }
}

public class FakeSource : WebSource
{
    public List<ImageAttachment> Images = new List<ImageAttachment>();
    public override string SourceKey => "polymarket";
    public override string PromptRoot => "Polymarket Source";
    public int Accepted, Fetches;
    private int sequence;
    public void Boot() => Start();
    public void Configure(PolymarketConfigs config) => ConfigureWebSource(config);
    protected override void RegisterConfig() { }
    public override Task<IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit)
    {
        Fetches++;
        return Task.FromResult<IReadOnlyList<WebSourceDocument>>(Enumerable.Range(0, limit).Select(_ => new WebSourceDocument
        { provider = "polymarket", id = (++sequence).ToString(), title = "Question", selftext = "Literal {1}", images = Images, market = new PolymarketSnapshot { outcomes = new[] { "Yes", "No" }, prices = new[] { .6, .4 } } }).ToList());
    }
    public override void RememberAccepted(WebSourceDocument source) => Accepted++;
}
