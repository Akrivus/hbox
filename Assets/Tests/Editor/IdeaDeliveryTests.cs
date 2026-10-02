using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

// Reflection lets this test assembly exercise the predefined Assembly-CSharp
// without moving runtime code into a new assembly.
public sealed class IdeaDeliveryTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> objects = new List<GameObject>();
    private Type serverType;
    private Component server;
    private Component generator;
    private Component context;
    private string slug;

    [SetUp]
    public void SetUp()
    {
        serverType = Type.GetType("ServerSource, Assembly-CSharp", true);
        server = Create("idea-test-server", serverType);
        // Keep objects inactive so Start cannot open sockets or start generation.
        Invoke(server, "Awake");
        context = Create("idea-test-context", Type.GetType("ChatManagerContext, Assembly-CSharp", true));
        context.GetType().GetField("key", PrivateInstance).SetValue(context, "idea-test");
        generator = NewGenerator();
        slug = (string)generator.GetType().GetProperty("slug").GetValue(generator);
        Register(generator);
    }

    [TearDown]
    public void TearDown()
    {
        if (server != null)
            Invoke(server, "OnDestroy");
        foreach (var obj in objects)
            UnityEngine.Object.DestroyImmediate(obj);
        objects.Clear();
    }

    [Test]
    public void UnregisteredBetweenSubmissionAndDrainIsNotAcknowledged()
    {
        var delivery = SubmitFromWorker();
        Assert.That(delivery.IsCompleted, Is.False);
        Unregister(generator);
        Drain();
        Assert.That(delivery.Result, Is.False);
        Assert.That(QueueDepth(generator), Is.Zero);
    }

    [Test]
    public void SuccessWaitsForMainThreadDrain()
    {
        var delivery = SubmitFromWorker();
        Assert.That(delivery.IsCompleted, Is.False);
        Assert.That(QueueDepth(generator), Is.Zero);
        Drain(); // NUnit EditMode tests execute on Unity's main thread.
        Assert.That(delivery.Result, Is.True);
        Assert.That(QueueDepth(generator), Is.EqualTo(1));
        Drain();
        Assert.That(QueueDepth(generator), Is.EqualTo(1));
    }

    [Test]
    public void ReplacementWithSameSlugDoesNotReceiveOldSubmission()
    {
        var delivery = SubmitFromWorker();
        Unregister(generator);
        var replacement = NewGenerator();
        Register(replacement);
        Drain();
        Assert.That(delivery.Result, Is.False);
        Assert.That(QueueDepth(generator), Is.Zero);
        Assert.That(QueueDepth(replacement), Is.Zero);
    }

    [Test]
    public void ShutdownCompletesPendingSubmissionAsFailure()
    {
        var delivery = SubmitFromWorker();
        Invoke(server, "OnDestroy");
        Assert.That(delivery.IsCompleted, Is.True);
        Assert.That(delivery.Result, Is.False);
        Assert.That(QueueDepth(generator), Is.Zero);
    }

    private Component Create(string name, Type type)
    {
        var obj = new GameObject(name);
        objects.Add(obj);
        obj.SetActive(false);
        return obj.AddComponent(type);
    }

    private Component NewGenerator()
    {
        var result = Create("idea-test-generator", Type.GetType("ChatGenerator, Assembly-CSharp", true));
        result.GetType().GetField("chatManagerContext", PrivateInstance).SetValue(result, context);
        return result;
    }

    private Task<bool> SubmitFromWorker()
    {
        // Wait only for submission, never for delivery (which requires this thread).
        return Task.Run(() => Task.FromResult((Task<bool>)serverType.GetMethod("QueueIdea")
            .Invoke(null, new object[] { slug, "regression prompt" }))).Result;
    }

    private void Register(Component target) => serverType.GetMethod("RegisterGenerator").Invoke(server, new object[] { target });
    private void Unregister(Component target) => serverType.GetMethod("UnregisterGenerator").Invoke(server, new object[] { target });
    private void Drain() => Invoke(server, "ProcessPendingIdeaRequests");
    private static int QueueDepth(Component target) => (int)target.GetType().GetProperty("QueueDepth").GetValue(target);
    private static void Invoke(Component target, string method) => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
}
