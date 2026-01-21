using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using UnityEngine;

public class SpawnPointManager : MonoBehaviour
{
    [Header("Camera Target Group")]
    public CinemachineTargetGroup targetGroup;
    public float speakerRadius = 1.0f;
    public float speakerWeight = 5.0f;
    public float defaultRadius = 3.0f;
    public float defaultWeight = 1.0f;
    public Transform anchor;
    public Transform target;
    public CinemachineVirtualCamera[] virtualCameras;

    [Header("Movement Settings")]
    public float moveSpeed = 2f;
    public float turnSpeed = 4f;
    public float energyOffset = 0.5f;
    public Vector3 distances = new Vector3(5.0f, 2.0f, 0.5f);

    public SpawnPoint[] spawnPoints;

    private Dictionary<Actor, SpawnPoint> actorToSpawnPoint = new();
    private Dictionary<Actor, ActorController> actorToController = new();

    private int nodeIndex = 0;

    private ActorController lastActorController;

    public void Register()
    {
        ChatManagerContext.Current.OnActorAdded += OnActorAdded;
        ChatManagerContext.Current.OnChatNodeActivated += OnChatNodeActivated;
        ChatManagerContext.Current.OnChatLoaded += OnChatLoaded;
    }

    public void UnRegister()
    {
        ChatManagerContext.Current.OnActorAdded -= OnActorAdded;
        ChatManagerContext.Current.OnChatNodeActivated -= OnChatNodeActivated;
        ChatManagerContext.Current.OnChatLoaded -= OnChatLoaded;
        actorToController.Clear();
        actorToSpawnPoint.Clear();
    }

    private void OnDestroy()
    {
        UnRegister();
        StopAllCoroutines();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, distances.x);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, distances.y);
        Gizmos.color = Color.red;

        foreach (var spawnPoint in spawnPoints)
            if (spawnPoint.stationary || spawnPoint.sitting)
            {
                Gizmos.DrawWireSphere(spawnPoint.position, distances.z);

                var radius = spawnPoint.height * 0.25f;
                var position = spawnPoint.position + Vector3.up * radius;

                Gizmos.DrawSphere(position, radius);
            }
    }

    private void OnActorAdded(Chat chat, ActorController actorController)
    {
        var actor = actorController.Actor;
        actorToController[actor] = actorController;

        var spawnPoint = spawnPoints.FirstOrDefault(t => t.transform == actorController.transform.parent);
        if (spawnPoint == null)
            return;
        actorToSpawnPoint[actor] = spawnPoint;
        SetSpawnPoints(InCircle(transform.position, CalculateSpacing()));

        var animator = actorController.GetComponent<Animator>();
        if (animator != null)
        {
            animator.SetBool("Sitting", spawnPoint.sitting);
            animator.SetFloat("Height", spawnPoint.height);
            animator.SetFloat("Posture", spawnPoint.posture);
        }
    }

    private void OnChatLoaded(Chat chat)
    {
        anchor.position = transform.position;
        anchor.rotation = transform.rotation;
        target.position = transform.position;
        target.rotation = transform.rotation;
    }

    private void OnChatNodeActivated(ChatNode node)
    {
        foreach (var actor in actorToSpawnPoint.Keys)
            ArrangeSpawnPoints(InCircle(transform.position, CalculateSpacing()));

        if (lastActorController != null)
        {
            anchor.position = lastActorController.LookObject.position;
            anchor.rotation = lastActorController.LookObject.rotation;
        }

        lastActorController = actorToController[node.Actor];
        target.position = lastActorController.LookObject.position;
        target.rotation = lastActorController.LookObject.rotation;

        foreach (var camera in virtualCameras)
            camera.Priority = 0;
        virtualCameras.Sample().Priority = 10;

        targetGroup.m_Targets = actorToController.Values
            .Select(t =>
            {
                var speaker = t.Actor == node.Actor;
                var energy = Math.Abs(t.Energy) + energyOffset;

                return new CinemachineTargetGroup.Target
                {
                    radius = (speaker ? speakerRadius : defaultWeight) * energy,
                    weight = (speaker ? speakerWeight : defaultWeight) * energy,
                    target = t.LookObject
                };
            }).ToArray();
        nodeIndex++;
    }

    private IEnumerator MoveToTarget(Actor actor, Vector3 target)
    {
        if (!actorToSpawnPoint.ContainsKey(actor))
            yield break;
        var point = actorToSpawnPoint[actor];
        var controller = actorToController[actor];
        var speed = Math.Abs(controller.Energy) + energyOffset;

        var start = point.position;
        var distance = Vector3.Distance(start, target);

        var i = nodeIndex;
        var t = 0f;

        while (Vector3.Distance(point.position, target) > distances.z)
        {
            t += moveSpeed * speed / distance * Time.deltaTime;
            point.position = Vector3.Lerp(start, target, t);
            yield return null;

            if (i != nodeIndex)
                break;
        }
    }

    private IEnumerator TurnToTarget(Actor actor, Vector3 target)
    {
        if (!actorToSpawnPoint.ContainsKey(actor))
            yield break;
        var point = actorToSpawnPoint[actor];
        var speed = Math.Abs(actorToController[actor].Energy) + energyOffset;
        var dir = target - point.position;
        var rotation = Quaternion.LookRotation(dir);

        var start = point.rotation;
        var distance = Quaternion.Angle(start, rotation);

        var i = nodeIndex;
        var t = 0f;

        while (Quaternion.Angle(point.rotation, rotation) > 1f)
        {
            t += turnSpeed * speed / distance * Time.deltaTime;
            point.rotation = Quaternion.Slerp(start, rotation, t);
            yield return null;

            if (i != nodeIndex)
                break;
        }
    }

    public void ArrangeSpawnPoints(Vector3[] positions)
    {
        var max = Math.Min(positions.Length, actorToSpawnPoint.Keys.Count);
        for (var i = 0; i < max; i++)
        {
            var actor = actorToSpawnPoint.Keys.ElementAt(i);
            var spawnPoint = actorToSpawnPoint[actor];
            if (spawnPoint.stationary || spawnPoint.sitting)
                continue;
            var position = positions[i];
            StartCoroutine(MoveToTarget(actor, position));
            StartCoroutine(TurnToTarget(actor, position));
        }
    }

    public void SetSpawnPoints(Vector3[] positions)
    {
        var max = Math.Min(positions.Length, actorToSpawnPoint.Keys.Count);
        for (var i = 0; i < max; i++)
        {
            var actor = actorToSpawnPoint.Keys.ElementAt(i);
            var spawnPoint = actorToSpawnPoint[actor];
            if (spawnPoint.stationary || spawnPoint.sitting)
                continue;
            var position = positions[i];
            actorToSpawnPoint[actor].position = position;
            actorToSpawnPoint[actor].rotation = Quaternion.LookRotation(transform.position - position);
        }
    }

    public Vector3[] InCircle(Vector3 center, params float[] spacing)
    {
        if (spacing.Length == 0)
        {
            spacing = new float[transform.childCount];
            for (int i = 0; i < spacing.Length; i++)
                spacing[i] = 1;
        }

        var count = spacing.Length;
        var result = new Vector3[count];
        var angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float radians = (angleStep * i + UnityEngine.Random.Range(-10f, 10f)) * Mathf.Deg2Rad;
            float radius = distances.y + spacing[i];

            result[i] = center + new Vector3(
                Mathf.Sin(radians) * radius,
                0f,
                Mathf.Cos(radians) * radius);
        }
        return result;
    }

    private float[] CalculateSpacing()
    {
        var chat = ChatManager.Instance.NowPlaying;
        var count = actorToController.Count;
        var spacing = new float[count];

        for (int i = 0; i < count; i++)
            spacing[i] = Mathf.Abs(chat.Actors[i]?.Sentiment?.Score ?? 0.0f) + energyOffset;
        return spacing;
    }


    [Serializable]
    public class SpawnPoint
    {
        public Transform transform;
        public bool stationary;
        public bool sitting;
        public float height;
        public float posture;

        public Vector3 position
        {
            get => transform.position;
            set => transform.position = value;
        }

        public Quaternion rotation
        {
            get => transform.rotation;
            set => transform.rotation = value;
        }

        public string name => transform.name;
    }
}
