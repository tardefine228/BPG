using UnityEngine;
using UnityEngine.UI;

// A crisp, stepped frame. Its centre is transparent and never intercepts input.
[DisallowMultipleComponent]
public sealed class PixelFocusFrame : MaskableGraphic
{
    [SerializeField, Min(1f)] private float thickness = 3f;

    public static GameObject Create(RectTransform parent, Color color, float width)
    {
        var frame = new GameObject("FocusFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(PixelFocusFrame));
        frame.transform.SetParent(parent, false);
        var rect = (RectTransform)frame.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var graphic = frame.GetComponent<PixelFocusFrame>();
        graphic.color = color;
        graphic.thickness = width;
        graphic.raycastTarget = false;
        frame.SetActive(false);
        return frame;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        float w = Mathf.Min(Mathf.Max(1f, thickness), Mathf.Min(r.width, r.height) / 6f);
        if (w <= 0f) return;
        float c = w * 2f;
        AddRect(mesh, r.xMin + c, r.yMax - w, r.xMax - c, r.yMax);
        AddRect(mesh, r.xMin + c, r.yMin, r.xMax - c, r.yMin + w);
        AddRect(mesh, r.xMin, r.yMin + c, r.xMin + w, r.yMax - c);
        AddRect(mesh, r.xMax - w, r.yMin + c, r.xMax, r.yMax - c);
        AddRect(mesh, r.xMin + w, r.yMin + w, r.xMin + c, r.yMin + c);
        AddRect(mesh, r.xMax - c, r.yMin + w, r.xMax - w, r.yMin + c);
        AddRect(mesh, r.xMin + w, r.yMax - c, r.xMin + c, r.yMax - w);
        AddRect(mesh, r.xMax - c, r.yMax - c, r.xMax - w, r.yMax - w);
    }

    private void AddRect(VertexHelper mesh, float left, float bottom, float right, float top)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(left, bottom), color, Vector2.zero);
        mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
        mesh.AddVert(new Vector3(right, top), color, Vector2.zero);
        mesh.AddVert(new Vector3(right, bottom), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start + 2, start + 3, start);
    }
}
