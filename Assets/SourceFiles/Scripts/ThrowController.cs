using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Carries up to 3 thrown items (bottles/cans) and throws the most recently picked up one on G /
/// gamepad right shoulder (F74, decision D16). Added to the player by MazeGenerator.SetUpAtmosphere -
/// deliberately an instance field, not a static: a fresh player GameObject (and so a fresh, empty
/// ThrowController) exists every scene load, which is exactly "scene-scoped, not carried to the next
/// floor" (F74 slice spec) without needing any of CLAUDE.md's static-reset machinery.
/// </summary>
[DefaultExecutionOrder(-10)]
public class ThrowController : MonoBehaviour
{
    private const int MaxCarried = 3;
    private const float ThrowSpeed = 11f;
    private const float ThrowLift = 2.5f;

    private readonly List<ThrowableItem.Kind> _carried = new List<ThrowableItem.Kind>();

    private Camera _camera;
    private AIFollower _hunter;
    private PlayerStealthState _stealth;
    private Transform _mazeRoot;
    private ThrowableItem _bottleTemplate;
    private ThrowableItem _canTemplate;
    private NoisySurface _glassPatchTemplate;

    /// <summary>How many items are currently carried - shown by PlayerHud next to the consumable slots.</summary>
    public int Carried => _carried.Count;

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere. Any template argument may be null (an incomplete InteractableKit - throwing just never fires).</summary>
    public void Configure(Camera playerCamera, AIFollower hunter, PlayerStealthState stealth, Transform mazeRoot,
        ThrowableItem bottleTemplate, ThrowableItem canTemplate, NoisySurface glassPatchTemplate)
    {
        _camera = playerCamera;
        _hunter = hunter;
        _stealth = stealth;
        _mazeRoot = mazeRoot;
        _bottleTemplate = bottleTemplate;
        _canTemplate = canTemplate;
        _glassPatchTemplate = glassPatchTemplate;

        // F75 shop item: Bottle Pack starts the floor already carrying 3 bottles.
        if (PlayerInventory.HasActive(ShopItem.BottlePack))
        {
            _carried.Clear();
            for (int i = 0; i < MaxCarried; i++) _carried.Add(ThrowableItem.Kind.Bottle);
        }
    }

    /// <summary>Called by ThrowableItem.Interact. False (and a no-op) once 3 are already carried.</summary>
    public bool TryPickUp(ThrowableItem.Kind kind)
    {
        if (_carried.Count >= MaxCarried) return false;
        _carried.Add(kind);
        return true;
    }

    private void Update()
    {
        if (_camera == null || _carried.Count == 0 || !IsActive()) return;

#if ENABLE_INPUT_SYSTEM
        bool pressed = (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);
#else
        bool pressed = false;
#endif
        pressed |= TouchInput.Pressed(TouchInput.TouchAction.Throw);
        if (pressed) Throw();
    }

    private bool IsActive()
    {
        if (Time.timeScale <= 0f) return false;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver || GameFlow.IsInShop) return false;
        if (_stealth != null && _stealth.Hidden) return false; // no throwing from inside a locker
        return true;
    }

    private void Throw()
    {
        ThrowableItem.Kind kind = _carried[_carried.Count - 1];
        ThrowableItem template = kind == ThrowableItem.Kind.Bottle ? _bottleTemplate : _canTemplate;
        if (template == null) return; // an incomplete kit - keep the item rather than eat it on a no-op throw

        _carried.RemoveAt(_carried.Count - 1);

        // Spawned a full pace ahead of the camera so it clears the player's own capsule before its
        // collider goes solid in Launch, rather than fighting the CharacterController for a frame.
        Vector3 origin = _camera.transform.position + _camera.transform.forward * 0.8f;
        Vector3 velocity = _camera.transform.forward * ThrowSpeed + Vector3.up * ThrowLift;

        ThrowableItem projectile = Instantiate(template, origin, Quaternion.identity, _mazeRoot);
        projectile.gameObject.SetActive(true);
        projectile.Launch(origin, velocity, _hunter, _glassPatchTemplate, _mazeRoot);
    }
}
