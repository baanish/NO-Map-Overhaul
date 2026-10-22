using UnityEngine;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// Transform writes that skip a value already set: Unity counts any transform write as a change that re-batches the
/// canvas, even one that sets the same value.
/// </summary>
internal static class TransformWrites
{
    public static void SetLocalPosition(Transform transform, Vector3 position)
    {
        if (transform.localPosition != position) transform.localPosition = position;
    }

    public static void SetLocalRotation(Transform transform, Quaternion rotation)
    {
        if (transform.localRotation != rotation) transform.localRotation = rotation;
    }

    public static void SetLocalScale(Transform transform, Vector3 scale)
    {
        if (transform.localScale != scale) transform.localScale = scale;
    }

    public static void SetPosition(Transform transform, Vector3 position)
    {
        if (transform.position != position) transform.position = position;
    }

    public static void SetSizeDelta(RectTransform transform, Vector2 size)
    {
        if (transform.sizeDelta != size) transform.sizeDelta = size;
    }
}
