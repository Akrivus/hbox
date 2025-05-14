using UnityEngine;

public class ItemController : AutoActor, ISubChats, ISubNode
{
    [SerializeField]
    private MeshRenderer itemRenderer;
    private Vector3 itemPosition;

    [Header("Rotation")]
    [SerializeField]
    private float xAngleOffset;
    [SerializeField]
    private float yAngleOffset;
    [SerializeField]
    private float zAngleOffset;

    private void Start()
    {
        itemPosition = itemRenderer.transform.localPosition;
    }

    private void Update()
    {
        var time = Time.time * (ActorController.IsTalking ? 1.0f : 0.5f) + transform.GetSiblingIndex() * 1000f;
        var sin = Mathf.Sin(time) * ActorController.Sentiment.Score * (ActorController.IsTalking ? 4f : 1f);
        var position = itemPosition - Vector3.forward * sin;

        itemRenderer.transform.LookAt(ActorController.Camera.transform);

        itemRenderer.transform.Rotate(Vector3.forward, Mathf.Sin(time) * 0.025f);
        itemRenderer.transform.Rotate(xAngleOffset, yAngleOffset, zAngleOffset);

        itemRenderer.transform.localPosition = Vector3.Lerp(
            itemRenderer.transform.localPosition,
            position,
            Time.deltaTime * 8.0f);
    }

    private string ToCodePoint(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return null;
        if (char.IsSurrogatePair(emoji, 0))
            return char.ConvertToUtf32(emoji, 0).ToString("x");
        return emoji;
    }

    private void SetItem(string item)
    {
        var texture = Resources.Load<Texture2D>($"{ChatManager.Instance.name}/Props/{item}");
        if (texture != null)
            itemRenderer.material.mainTexture = texture;
    }

    public void Initialize(Chat chat)
    {
        if (chat == null) return;
        var context = chat.Actors.Get(Actor);
        var item = ToCodePoint(context.Item);
        if (item != null)
            SetItem(item);
    }

    public void Activate(ChatNode node)
    {
        if (node.Item == null) return;
        var item = ToCodePoint(node.Item);
        if (item != null)
            SetItem(item);
    }
}
