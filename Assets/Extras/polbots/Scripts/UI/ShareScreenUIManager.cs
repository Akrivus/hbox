using TMPro;
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

    private void Start()
    {
        ShareScreenOff();
    }

    public void ShareScreenOn()
    {
        ChatManager.Instance.RemoveActorsOnCompletion = false;
        SetShareScreen(
            GridLayoutGroup.Corner.UpperLeft,
            GridLayoutGroup.Axis.Horizontal,
            TextAnchor.LowerCenter,
            minVideoScreens, true);
    }

    public void ShareScreenOff()
    {
        ChatManager.Instance.RemoveActorsOnCompletion = true;
        SetShareScreen(
            GridLayoutGroup.Corner.UpperLeft,
            GridLayoutGroup.Axis.Horizontal,
            TextAnchor.MiddleCenter,
            maxVideoScreens, false);
    }

    private void SetShareScreen(GridLayoutGroup.Corner corner, GridLayoutGroup.Axis axis, TextAnchor alignment, int childCount, bool active)
    {
        _gridLayoutGroup.startCorner = corner;
        _gridLayoutGroup.startAxis = axis;
        _gridLayoutGroup.childAlignment = alignment;
        _gridLayoutGroup.MaxChildren = childCount;
        _gridLayoutGroup.UpdateChildren();

        switch (axis)
        {
            case GridLayoutGroup.Axis.Horizontal:
                _gridLayoutGroup.SetLayoutHorizontal();
                _shareScreenPrefab.SetActive(active);
                break;
            case GridLayoutGroup.Axis.Vertical:
                _gridLayoutGroup.SetLayoutVertical();
                _shareScreenPrefab.SetActive(active);
                break;
        }
    }
}
