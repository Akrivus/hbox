using UnityEngine;

public abstract class AutoActor : MonoBehaviour
{
    public ActorController ActorController => _actor ?? (_actor = GetComponent<ActorController>());
    public Actor Actor => ActorController.Actor;

    private ActorController _actor;

    private void Awake()
    {
        _actor = GetComponent<ActorController>();
    }
}