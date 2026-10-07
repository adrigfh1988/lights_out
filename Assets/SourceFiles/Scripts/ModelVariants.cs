using UnityEngine;

/// <summary>
/// Look-alike models under one set piece (the FallenRunner's six DeadBody LITE corpses): all are children, one
/// is active. MazeGenerator.BuildSetPieces calls Apply with a hash of the cell - the same no-rng-draw trick as
/// DecalVariants and InteractableKit.bottleVariants, so the +8 set-piece stream is untouched.
/// Written by LIGHTS OUT &gt; Build &gt; Floor Themes / Kit Maze Theme (CorpseUpgrade); never edited by hand.
/// </summary>
public class ModelVariants : MonoBehaviour
{
    [SerializeField] private GameObject[] options;

    public int Count => options != null ? options.Length : 0;

    /// <summary>Activates option (hash mod Count) and deactivates the rest.</summary>
    public void Apply(int hash)
    {
        if (Count == 0) return;

        int index = (hash & int.MaxValue) % options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] != null) options[i].SetActive(i == index);
        }
    }
}
