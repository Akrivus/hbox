using UnityEngine;

public class ItemController : AutoActor, ISubChats, ISubNode
{
    private const string EmptyPropName = "none";

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

    private void Awake()
    {
        ClearItem();
    }

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

    private string ToPropAssetName(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji))
            return null;

        var value = emoji.Trim();
        if (string.Equals(value, EmptyPropName, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "null", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "empty", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n/a", System.StringComparison.OrdinalIgnoreCase))
            return EmptyPropName;

        if (IsHexCodePointName(value))
            return value.ToLowerInvariant();

        var isSurrogatePair = value.Length > 1 && char.IsSurrogatePair(value, 0);
        var codePoint = isSurrogatePair ? char.ConvertToUtf32(value, 0) : value[0];
        var consumedLength = isSurrogatePair ? 2 : 1;

        for (var i = consumedLength; i < value.Length; i++)
        {
            if (!IsEmojiModifier(value[i]))
                return null;
        }

        return codePoint.ToString("x");
    }

    private bool IsHexCodePointName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
                return false;
        }

        return value.Length >= 3 && value.Length <= 6;
    }

    private bool IsEmojiModifier(char character)
    {
        return character == '\ufe0e' || character == '\ufe0f' || character == '\u200d';
    }

    private void SetItem(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            ClearItem();
            return;
        }

        OperatorTelemetry.RecordTouchedProp($"{ChatManagerContext.Current?.Name}/Props/{item}");
        var ownerKey = RuntimeAssetCache.BuildContextOwnerKey(ChatManagerContext.Current?.Key);
        var texture = RuntimeAssetCache.LoadContextTexture(ChatManagerContext.Current?.Name, "Props", item, ownerKey);
        if (texture == null && !string.Equals(item, EmptyPropName, System.StringComparison.OrdinalIgnoreCase))
            texture = RuntimeAssetCache.LoadContextTexture(ChatManagerContext.Current?.Name, "Props", EmptyPropName, ownerKey);

        if (texture == null)
        {
            ClearItem();
            return;
        }

        itemRenderer.material.mainTexture = texture;
        itemRenderer.enabled = !string.Equals(item, EmptyPropName, System.StringComparison.OrdinalIgnoreCase);
    }

    private void ClearItem()
    {
        if (itemRenderer == null)
            return;

        itemRenderer.material.mainTexture = null;
        itemRenderer.enabled = false;
    }

    public void Initialize(Chat chat)
    {
        if (chat == null) return;
        var context = chat.Actors.Get(Actor);
        var item = ToPropAssetName(context.Item);
        SetItem(item);
    }

    public void Activate(ChatNode node)
    {
        if (node == null)
            return;

        var item = ToPropAssetName(node.Item);
        SetItem(item);
    }
}
