using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIGridLayoutGroup : GridLayoutGroup
{
    public int MaxChildren = 12;
    public bool PlacePartialHorizontalRowAtBottom = true;
    public GameObject ExcludedChild { get; set; }
    private int lastChildCount = -1;
    private int lastMaxChildren = -1;
    private GameObject lastExcludedChild;

    private void Update()
    {
        if (lastChildCount == rectTransform.childCount &&
            lastMaxChildren == MaxChildren &&
            lastExcludedChild == ExcludedChild)
            return;

        UpdateChildren();
    }

    public void UpdateChildren()
    {
        lastChildCount = rectTransform.childCount;
        lastMaxChildren = MaxChildren;
        lastExcludedChild = ExcludedChild;

        var visibleIndex = 0;
        for (int i = 0; i < rectTransform.childCount; i++)
        {
            var child = rectTransform.GetChild(i).gameObject;
            if (ExcludedChild != null && child == ExcludedChild)
            {
                SetActiveIfChanged(child, false);
                continue;
            }

            SetActiveIfChanged(child, visibleIndex < MaxChildren);
            visibleIndex++;
        }
    }

    private static void SetActiveIfChanged(GameObject child, bool active)
    {
        if (child != null && child.activeSelf != active)
            child.SetActive(active);
    }

    public override void SetLayoutHorizontal()
    {
        SetCellsAlongAxis(0);
    }

    public override void SetLayoutVertical()
    {
        SetCellsAlongAxis(1);
    }

    private void SetCellsAlongAxis(int axis)
    {
        var children = GetLayoutChildren();
        var count = children.Count;
        if (axis == 0)
        {
            for (int i = 0; i < count; i++)
            {
                RectTransform rect = children[i];

                m_Tracker.Add(this, rect,
                    DrivenTransformProperties.Anchors |
                    DrivenTransformProperties.AnchoredPosition |
                    DrivenTransformProperties.SizeDelta);

                rect.anchorMin = Vector2.up;
                rect.anchorMax = Vector2.up;
                rect.sizeDelta = cellSize;
            }
            return;
        }

        float width = rectTransform.rect.size.x;
        float height = rectTransform.rect.size.y;

        int cellCountX = 1;
        int cellCountY = 1;

        if (m_Constraint == Constraint.FixedColumnCount)
        {
            cellCountX = m_ConstraintCount;

            if (cellCountX == m_ConstraintCount + 1)
                cellCountX -= 1;

            if (count > cellCountX)
                cellCountY = count / cellCountX + (count % cellCountX > 0 ? 1 : 0);
        }
        else if (m_Constraint == Constraint.FixedRowCount)
        {
            cellCountY = m_ConstraintCount;

            if (count > cellCountY)
                cellCountX = count / cellCountY + (count % cellCountY > 0 ? 1 : 0);
        }
        else
        {
            if (cellSize.x + spacing.x <= 0)
                cellCountX = int.MaxValue;
            else
                cellCountX = Mathf.Max(1, Mathf.FloorToInt((width - padding.horizontal + spacing.x + 0.001f) / (cellSize.x + spacing.x)));

            if (cellSize.y + spacing.y <= 0)
                cellCountY = int.MaxValue;
            else
                cellCountY = Mathf.Max(1, Mathf.FloorToInt((height - padding.vertical + spacing.y + 0.001f) / (cellSize.y + spacing.y)));
        }

        int cornerX = (int)startCorner % 2;
        int cornerY = (int)startCorner / 2;

        int cellsPerMainAxis, actualCellCountX, actualCellCountY;
        if (startAxis == Axis.Horizontal)
        {
            cellsPerMainAxis = cellCountX;
            actualCellCountX = Mathf.Clamp(cellCountX, 1, count);
            actualCellCountY = Mathf.Clamp(cellCountY, 1, Mathf.CeilToInt(count / (float)cellsPerMainAxis));
        }
        else
        {
            cellsPerMainAxis = cellCountY;
            actualCellCountY = Mathf.Clamp(cellCountY, 1, count);
            actualCellCountX = Mathf.Clamp(cellCountX, 1, Mathf.CeilToInt(count / (float)cellsPerMainAxis));
        }
        int lastCellsCount = count % cellsPerMainAxis;

        Vector2 requiredSpace = new Vector2(
            actualCellCountX * cellSize.x + (actualCellCountX - 1) * spacing.x,
            actualCellCountY * cellSize.y + (actualCellCountY - 1) * spacing.y
        );
        Vector2 startOffset = new Vector2(
            GetStartOffset(0, requiredSpace.x),
            GetStartOffset(1, requiredSpace.y)
        );

        int actualLastCellsCount = lastCellsCount == 0 ? cellsPerMainAxis : lastCellsCount;
        int cellsX = startAxis == Axis.Horizontal ? actualLastCellsCount : actualCellCountX;
        int cellsY = startAxis == Axis.Vertical ? actualLastCellsCount : actualCellCountY;

        Vector2 lastCellsRequiredSpace = new Vector2(
            cellsX * cellSize.x + (cellsX - 1) * spacing.x,
            cellsY * cellSize.y + (cellsY - 1) * spacing.y
        );

        Vector2 lastCellsStartOffset = new Vector2(
            GetStartOffset(0, lastCellsRequiredSpace.x),
            GetStartOffset(1, lastCellsRequiredSpace.y)
        );

        for (int i = 0; i < count; i++)
        {
            int positionX;
            int positionY;
            Vector2 cellStartOffset = (i + 1 > count - actualLastCellsCount) ? lastCellsStartOffset : startOffset;

            if (startAxis == Axis.Horizontal)
            {
                positionX = i % cellsPerMainAxis;
                positionY = i / cellsPerMainAxis;
            }
            else
            {
                positionX = i / cellsPerMainAxis;
                positionY = i % cellsPerMainAxis;
            }

            if (cornerX == 1)
                positionX = actualCellCountX - 1 - positionX;
            if (cornerY == 1 && !ShouldKeepPartialHorizontalRowAtBottom(lastCellsCount))
                positionY = actualCellCountY - 1 - positionY;

            SetChildAlongAxis(children[i], 0, cellStartOffset.x + (cellSize[0] + spacing[0]) * positionX, cellSize[0]);
            SetChildAlongAxis(children[i], 1, cellStartOffset.y + (cellSize[1] + spacing[1]) * positionY, cellSize[1]);
        }
    }

    private bool ShouldKeepPartialHorizontalRowAtBottom(int lastCellsCount)
    {
        return PlacePartialHorizontalRowAtBottom &&
            startAxis == Axis.Horizontal &&
            lastCellsCount > 0;
    }

    private List<RectTransform> GetLayoutChildren()
    {
        var children = new List<RectTransform>(rectChildren.Count);
        for (var i = 0; i < rectChildren.Count; i++)
        {
            var child = rectChildren[i];
            if (child == null || ExcludedChild != null && child.gameObject == ExcludedChild)
                continue;

            children.Add(child);
        }

        return children;
    }
}
