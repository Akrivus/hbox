using UnityEngine;
using UnityEngine.UI;

public class ShareScreenUIManager : MonoBehaviour
{
    [SerializeField]
    private UIGridLayoutGroup _gridLayoutGroup;

    [SerializeField]
    private GameObject _shareScreenPrefab;

    [SerializeField]
    private int maxVideoScreens = 12;

    [SerializeField]
    private int minVideoScreens = 4;

    private bool _shareScreenActive;

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
        SetShareScreen(
            GridLayoutGroup.Corner.UpperLeft,
            GridLayoutGroup.Axis.Horizontal,
            TextAnchor.LowerCenter,
            minVideoScreens, true);
    }

    public void ShareScreenOff()
    {
        SetShareScreen(
            GridLayoutGroup.Corner.UpperLeft,
            GridLayoutGroup.Axis.Horizontal,
            TextAnchor.MiddleCenter,
            maxVideoScreens, false);
    }

    private void SetShareScreen(GridLayoutGroup.Corner corner, GridLayoutGroup.Axis axis, TextAnchor alignment, int childCount, bool active)
    {
        _shareScreenActive = active;

        if (_shareScreenPrefab != null && active)
            _shareScreenPrefab.transform.SetSiblingIndex(0);

        _gridLayoutGroup.startCorner = corner;
        _gridLayoutGroup.startAxis = axis;
        _gridLayoutGroup.childAlignment = alignment;
        _gridLayoutGroup.MaxChildren = childCount;

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
        if (_shareScreenPrefab != null)
            _shareScreenPrefab.SetActive(_shareScreenActive);
    }
}
