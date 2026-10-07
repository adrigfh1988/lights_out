using UnityEngine;

/// <summary>
/// F85: lets one hanging-body ceiling prop (DeadBody LITE, Pack_HangingBody) serve floors with different ceilings.
/// The pack's own DeadBodyHanging script just sets a rope bone's local Y; that script is gone from our prefab, so this
/// does the same one-off at spawn: MazeGenerator.BuildCeilingProps calls Fit(wallHeight) on every instance, which
/// lengthens the rope and drops the body by the same amount so the rope top stays on the ceiling and the feet hang
/// FeetClearance off the floor. One component per model variant (the variants sit behind a ModelVariants), all fitted
/// whether or not they are the active one. Written by FloorThemeBuilder.Pack.cs; never edited by hand.
/// </summary>
public class HangingBodyFit : MonoBehaviour
{
    [SerializeField] private Transform rope;
    [SerializeField] private Transform body;
    [SerializeField] private BoxCollider box;
    [Tooltip("Rope bone local Y, body local Y and collider centre Y at the depth the prefab was built for")]
    [SerializeField] private float ropeLength0;
    [SerializeField] private float bodyY0;
    [SerializeField] private float boxY0;
    [Tooltip("How far the rope bone's local Y moves the rope top, per metre (measured from the baked skinned mesh)")]
    [SerializeField] private float slope = 0.86f;
    [Tooltip("Ceiling underside to the soles at build time (m)")]
    [SerializeField] private float depth0;
    [SerializeField] private float feetClearance = 0.4f;

    /// <summary>Re-seats the rope and body for a room whose ceiling underside is ceilingHeight above its floor.</summary>
    public void Fit(float ceilingHeight)
    {
        if (rope == null || body == null) return;

        float delta = (ceilingHeight - feetClearance) - depth0;
        Vector3 r = rope.localPosition;
        rope.localPosition = new Vector3(r.x, ropeLength0 + delta / Mathf.Max(0.01f, slope), r.z);
        Vector3 b = body.localPosition;
        body.localPosition = new Vector3(b.x, bodyY0 - delta, b.z);
        if (box != null)
        {
            Vector3 c = box.center;
            box.center = new Vector3(c.x, boxY0 - delta, c.z);
        }
    }
}
