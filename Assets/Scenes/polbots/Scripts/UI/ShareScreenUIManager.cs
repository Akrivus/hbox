using UnityEngine;
using UnityEngine.UI;

public class ShareScreenUIManager : MonoBehaviour
{
    [SerializeField]
    private ShareScreenVideoRail activeVideoRail = ShareScreenVideoRail.Right;

    [SerializeField]
    private UIGridLayoutGroup _gridLayoutGroup;

    [SerializeField]
    private GameObject _shareScreenPrefab;

    [SerializeField]
    private int maxVideoScreens = 12;

    [SerializeField]
    private int minVideoScreens = 4;

    [SerializeField]
    private Vector2 activeMediaScreenSize = new Vector2(1440f, 800f);

    [SerializeField]
    private Vector2 activeMediaScreenOffset = new Vector2(-40f, -24f);

    [SerializeField]
    private Vector2 bottomRailMediaScreenSize = new Vector2(1670f, 640f);

    [SerializeField]
    private Vector2 bottomRailMediaScreenOffset = Vector2.zero;

    private bool _shareScreenActive;
    private GridLayoutState _defaultGridLayout;
    private bool _hasDefaultGridLayout;
    private MediaScreenLayoutState _defaultMediaScreenLayout;
    private bool _hasDefaultMediaScreenLayout;

    public Vector2 ActiveMediaScreenSize => GetMediaScreenLayout(activeVideoRail).Size;

    private void Awake()
    {
        CaptureDefaultGridLayout();
    }

    private void Start()
    {
        ShareScreenOff();
    }

    private void LateUpdate()
    {
        if (_shareScreenPrefab != null && _shareScreenPrefab.activeSelf != _shareScreenActive)
            _shareScreenPrefab.SetActive(_shareScreenActive);
    }

    public void ShareScreenOn()
    {
        switch (activeVideoRail)
        {
            case ShareScreenVideoRail.Right:
                SetShareScreen(
                    GridLayoutGroup.Corner.UpperRight,
                    GridLayoutGroup.Axis.Vertical,
                    TextAnchor.MiddleRight,
                    activeVideoRail,
                    minVideoScreens,
                    true);
                break;
            default:
                SetShareScreen(
                    GridLayoutGroup.Corner.LowerLeft,
                    GridLayoutGroup.Axis.Horizontal,
                    TextAnchor.LowerCenter,
                    activeVideoRail,
                    minVideoScreens,
                    true);
                break;
        }
    }

    public void ShareScreenOff()
    {
        RestoreDefaultGridLayout();
        ApplyShareScreenActive(false);
    }

    public void SetMediaTexture(Texture texture)
    {
        if (_shareScreenPrefab == null)
            return;

        foreach (var image in _shareScreenPrefab.GetComponentsInChildren<RawImage>(true))
            image.texture = texture;
    }

    private void SetShareScreen(GridLayoutGroup.Corner corner, GridLayoutGroup.Axis axis, TextAnchor alignment, ShareScreenVideoRail rail, int childCount, bool active)
    {
        ApplyShareScreenActive(active);

        if (_gridLayoutGroup == null)
            return;

        if (_shareScreenPrefab != null && active)
        {
            _shareScreenPrefab.transform.SetSiblingIndex(0);
            ApplyActiveMediaScreenLayout(rail);
        }

        _gridLayoutGroup.ExcludedChild = _shareScreenPrefab;
        _gridLayoutGroup.startCorner = corner;
        _gridLayoutGroup.startAxis = axis;
        _gridLayoutGroup.childAlignment = alignment;
        _gridLayoutGroup.MaxChildren = childCount;
        RebuildGridLayout(axis);
    }

    private void ApplyShareScreenActive(bool active)
    {
        _shareScreenActive = active;

        if (_gridLayoutGroup != null)
            _gridLayoutGroup.ExcludedChild = _shareScreenPrefab;

        if (_shareScreenPrefab != null)
            _shareScreenPrefab.SetActive(_shareScreenActive);
    }

    private void CaptureDefaultGridLayout()
    {
        if (_gridLayoutGroup == null)
            return;

        _defaultGridLayout = new GridLayoutState(
            _gridLayoutGroup.startCorner,
            _gridLayoutGroup.startAxis,
            _gridLayoutGroup.childAlignment,
            _gridLayoutGroup.MaxChildren);
        _hasDefaultGridLayout = true;
        CaptureDefaultMediaScreenLayout();
    }

    private void RestoreDefaultGridLayout()
    {
        if (_gridLayoutGroup == null)
            return;

        if (!_hasDefaultGridLayout)
            CaptureDefaultGridLayout();

        if (_hasDefaultGridLayout)
        {
            _gridLayoutGroup.startCorner = _defaultGridLayout.StartCorner;
            _gridLayoutGroup.startAxis = _defaultGridLayout.StartAxis;
            _gridLayoutGroup.childAlignment = _defaultGridLayout.ChildAlignment;
            _gridLayoutGroup.MaxChildren = _defaultGridLayout.MaxChildren;
        }
        else
        {
            _gridLayoutGroup.MaxChildren = maxVideoScreens;
        }

        _gridLayoutGroup.ExcludedChild = _shareScreenPrefab;
        RestoreDefaultMediaScreenLayout();
        RebuildGridLayout(_gridLayoutGroup.startAxis);
    }

    private void CaptureDefaultMediaScreenLayout()
    {
        if (_shareScreenPrefab == null)
            return;

        var root = _shareScreenPrefab.transform as RectTransform;
        if (root == null)
            return;

        _defaultMediaScreenLayout = new MediaScreenLayoutState(root);
        _hasDefaultMediaScreenLayout = true;
    }

    private void ApplyActiveMediaScreenLayout(ShareScreenVideoRail rail)
    {
        if (_shareScreenPrefab == null)
            return;

        if (!_hasDefaultMediaScreenLayout)
            CaptureDefaultMediaScreenLayout();

        var root = _shareScreenPrefab.transform as RectTransform;
        if (root == null)
            return;

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        var layout = GetMediaScreenLayout(rail);
        root.anchoredPosition = layout.Offset;
        root.sizeDelta = layout.Size;
        SetNestedMediaScreenSize(root, layout.Size);
    }

    private MediaScreenActiveLayout GetMediaScreenLayout(ShareScreenVideoRail rail)
    {
        return rail == ShareScreenVideoRail.Bottom
            ? new MediaScreenActiveLayout(bottomRailMediaScreenSize, bottomRailMediaScreenOffset)
            : new MediaScreenActiveLayout(activeMediaScreenSize, activeMediaScreenOffset);
    }

    private void RestoreDefaultMediaScreenLayout()
    {
        if (!_hasDefaultMediaScreenLayout || _shareScreenPrefab == null)
            return;

        _defaultMediaScreenLayout.Restore(_shareScreenPrefab.transform as RectTransform);
    }

    private static void SetNestedMediaScreenSize(RectTransform root, Vector2 size)
    {
        if (root == null)
            return;

        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            child.sizeDelta = size;
        }
    }

    private void RebuildGridLayout(GridLayoutGroup.Axis axis)
    {
        if (_gridLayoutGroup == null)
            return;

        switch (axis)
        {
            case GridLayoutGroup.Axis.Horizontal:
                _gridLayoutGroup.SetLayoutHorizontal();
                break;
            case GridLayoutGroup.Axis.Vertical:
                _gridLayoutGroup.SetLayoutVertical();
                break;
        }

        _gridLayoutGroup.UpdateChildren();
        if (_shareScreenPrefab != null && _shareScreenPrefab.activeSelf != _shareScreenActive)
            _shareScreenPrefab.SetActive(_shareScreenActive);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_gridLayoutGroup.transform as RectTransform);
    }

    private readonly struct GridLayoutState
    {
        public GridLayoutState(GridLayoutGroup.Corner startCorner, GridLayoutGroup.Axis startAxis, TextAnchor childAlignment, int maxChildren)
        {
            StartCorner = startCorner;
            StartAxis = startAxis;
            ChildAlignment = childAlignment;
            MaxChildren = maxChildren;
        }

        public readonly GridLayoutGroup.Corner StartCorner;
        public readonly GridLayoutGroup.Axis StartAxis;
        public readonly TextAnchor ChildAlignment;
        public readonly int MaxChildren;
    }

    private readonly struct MediaScreenLayoutState
    {
        public MediaScreenLayoutState(RectTransform root)
        {
            AnchorMin = root.anchorMin;
            AnchorMax = root.anchorMax;
            Pivot = root.pivot;
            AnchoredPosition = root.anchoredPosition;
            SizeDelta = root.sizeDelta;
            ChildSizes = new Vector2[root.childCount];
            for (var i = 0; i < root.childCount; i++)
                ChildSizes[i] = root.GetChild(i) is RectTransform child ? child.sizeDelta : Vector2.zero;
        }

        public void Restore(RectTransform root)
        {
            if (root == null)
                return;

            root.anchorMin = AnchorMin;
            root.anchorMax = AnchorMax;
            root.pivot = Pivot;
            root.anchoredPosition = AnchoredPosition;
            root.sizeDelta = SizeDelta;

            var count = Mathf.Min(root.childCount, ChildSizes?.Length ?? 0);
            for (var i = 0; i < count; i++)
                if (root.GetChild(i) is RectTransform child)
                    child.sizeDelta = ChildSizes[i];
        }

        private readonly Vector2 AnchorMin;
        private readonly Vector2 AnchorMax;
        private readonly Vector2 Pivot;
        private readonly Vector2 AnchoredPosition;
        private readonly Vector2 SizeDelta;
        private readonly Vector2[] ChildSizes;
    }

    private readonly struct MediaScreenActiveLayout
    {
        public MediaScreenActiveLayout(Vector2 size, Vector2 offset)
        {
            Size = size;
            Offset = offset;
        }

        public readonly Vector2 Size;
        public readonly Vector2 Offset;
    }
}

public enum ShareScreenVideoRail
{
    Bottom,
    Right
}
