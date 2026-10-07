using UnityEngine;

/// <summary>
/// Look-alike variants for one decal PropPiece (the blood splat / smear / pool, from Assets/Blood decal pack):
/// a material per variant plus its width:height, so a 4:1 smear is drawn on a 4:1 quad instead of being
/// squashed square. MazeGenerator.BuildDecals calls Apply with a hash of the cell, the same no-rng-draw trick
/// as InteractableKit.bottleVariants, so the +7 decal stream - and every layout after it - is untouched.
///
/// The decal's placed size (PropPiece.decalSizeRange, the root's uniform scale) is the variant's LONGEST
/// side: a wide smear gets shorter, never wider than a square decal of the same size.
/// Written by LIGHTS OUT &gt; Build &gt; Floor Themes / Kit Maze Theme (BloodDecalUpgrade); never edited by hand.
/// </summary>
public class DecalVariants : MonoBehaviour
{
    [SerializeField] private Material[] materials;
    [Tooltip("Width / height of each variant's texture, same order as Materials")]
    [SerializeField] private float[] aspects;

    public int Count => materials != null ? materials.Length : 0;

    /// <summary>Swaps the decal's quad to variant (hash mod Count) and reshapes it to that texture's aspect.</summary>
    public void Apply(int hash)
    {
        if (Count == 0) return;

        int index = (hash & int.MaxValue) % materials.Length;
        Material material = materials[index];
        if (material == null) return;

        MeshRenderer quad = GetComponentInChildren<MeshRenderer>(true);
        if (quad == null) return;

        quad.sharedMaterial = material;

        float aspect = aspects != null && index < aspects.Length && aspects[index] > 0f ? aspects[index] : 1f;
        // A Quad's local X is the texture's U (width) and its local Y the V (height) whether it lies on a wall
        // or flat on the floor, so the shape is set on X/Y only.
        quad.transform.localScale = aspect >= 1f ? new Vector3(1f, 1f / aspect, 1f) : new Vector3(aspect, 1f, 1f);
    }
}
