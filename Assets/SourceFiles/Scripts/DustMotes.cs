using UnityEngine;

/// <summary>
/// One camera-attached ParticleSystem per run (F70 decision 12), authored as a prefab per theme by the
/// builder so the user has real content to inspect in the gallery, then cloned at runtime by
/// MazeGenerator.BuildDust. The particle system itself simulates in World space, so motes drift instead
/// of following the camera; only this component's transform follows.
///
/// FirstPersonRig runs at execution order -90, after MazeGenerator.Awake (-100), so the player camera
/// does not exist yet when the clone is created. Update polls for it lazily until found, then parents
/// once and stops looking.
/// </summary>
public class DustMotes : MonoBehaviour
{
    [Tooltip("Local offset from the player camera once attached (camera space).")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0f, 2.5f);

    private bool _isRuntimeClone;
    private bool _attached;
    private FirstPersonRig _rig;

    /// <summary>
    /// Called once by MazeGenerator.BuildDust right after Instantiate. A gallery showcase instance (built
    /// by FloorThemeBuilder/KitMazeThemeBuilder) is never marked, so Update below leaves it as a static
    /// preview on its plinth instead of hunting for a player camera in the Editor.
    /// </summary>
    public void MarkRuntimeClone()
    {
        _isRuntimeClone = true;
    }

    private void Update()
    {
        if (!_isRuntimeClone || _attached) return;

        if (_rig == null) _rig = FindAnyObjectByType<FirstPersonRig>();
        if (_rig == null || _rig.PlayerCamera == null) return;

        transform.SetParent(_rig.PlayerCamera.transform, false);
        transform.localPosition = cameraOffset;
        transform.localRotation = Quaternion.identity;
        _attached = true;
    }
}
