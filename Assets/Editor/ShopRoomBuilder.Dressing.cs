using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Alchemist House half of the shop builder: placement helpers that work from renderer bounds (the
/// pack's pivots are all over the place, so nothing is ever positioned by transform.position alone) and
/// the furniture / clutter passes that dress the room. See plannings/shop-room-alchemist-plan.md.
///
/// All positions are room-local. The room is never rotated or scaled, so a room-local point is just
/// RoomPosition plus that point in world space.
/// </summary>
public static partial class ShopRoomBuilder
{
    private enum Wall { North, South, East, West }

    private const string Pack = "Assets/BK_AlchemistHouse/Prefabs/";
    private const float PlasterDepth = 0.35f; // measured: the wall modules' plaster face sits 0.36 m behind their frame

    // Reset by BuildCore. Used to report which pack prefabs ended up in the room.
    private static readonly HashSet<string> UsedPrefabs = new HashSet<string>();
    // Pack lights we deliberately keep; BuildLights destroys every other Light the pack brought along.
    private static readonly List<Light> KeptLights = new List<Light>();
    private static Transform _room;

    /// <summary>Pack prefab paths (relative to Prefabs/) the last build placed.</summary>
    public static IReadOnlyCollection<string> LastUsedPrefabs => UsedPrefabs;

    // ---------------------------------------------------------------- spawning

    /// <summary>Loads Pack + relativePath + ".prefab"; logs an error and returns null if missing.</summary>
    private static GameObject Load(string relativePath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + relativePath + ".prefab");
        if (prefab == null) Debug.LogError($"Shop room: pack prefab '{relativePath}' not found under {Pack}.");
        return prefab;
    }

    /// <summary>Instantiates, parents, unpacks and strips. Never sets localScale; scaleMul multiplies the prefab's own.</summary>
    private static GameObject Spawn(Transform parent, string relativePath, float scaleMul = 1f)
    {
        GameObject prefab = Load(relativePath);
        if (prefab == null) return null;

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = go.transform.localScale * scaleMul;
        Strip(go);
        AlchemistPack.FixDefaultMaterials(go); // Bottle01/02/04 leave their label on the magenta Default-Material
        UsedPrefabs.Add(relativePath);
        return go;
    }

    /// <summary>
    /// Physics is the builder's own boxes (D5), so the pack's rigidbodies and colliders all go, plus the
    /// Built-in-only VolumetricLight and any missing-script slots. Every Light is switched to no shadows.
    /// </summary>
    private static void Strip(GameObject go)
    {
        foreach (Component c in go.GetComponentsInChildren<Component>(true))
        {
            if (c != null && c.GetType().Name == "VolumetricLight") Object.DestroyImmediate(c);
        }
        foreach (Joint joint in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Collider col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- bounds

    /// <summary>Combined world bounds of all MeshRenderers (particle renderers report junk in edit mode).</summary>
    private static Bounds RenderBounds(GameObject go)
    {
        if (go == null) return new Bounds(_room.position, Vector3.zero);

        MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>RenderBounds expressed in room-local space.</summary>
    private static Bounds LocalBounds(GameObject go)
    {
        Bounds b = RenderBounds(go);
        b.center -= _room.position;
        return b;
    }

    private static Bounds Union(params GameObject[] items)
    {
        Bounds b = RenderBounds(items[0]);
        for (int i = 1; i < items.Length; i++) b.Encapsulate(RenderBounds(items[i]));
        return b;
    }

    private static void Orient(GameObject go, float yaw, float pitch, float roll)
    {
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, roll) * go.transform.localRotation;
    }

    private static void Shift(GameObject go, Vector3 worldDelta)
    {
        go.transform.position += worldDelta;
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Spawns, rotates, then moves so the bounds' bottom-centre lands on bottomCentre (room-local).</summary>
    private static GameObject PlaceOnFloor(Transform parent, string path, Vector3 bottomCentre, float yaw = 0f, float scaleMul = 1f, float pitch = 0f, float roll = 0f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        Orient(go, yaw, pitch, roll);

        Bounds b = RenderBounds(go);
        Vector3 target = _room.TransformPoint(bottomCentre);
        Shift(go, target - new Vector3(b.center.x, b.min.y, b.center.z));
        return go;
    }

    /// <summary>Spawns and rotates, then aligns the bounds' centre to centre (room-local).</summary>
    private static GameObject PlaceCentred(Transform parent, string path, Vector3 centre, float yaw = 0f, float scaleMul = 1f, float pitch = 0f, float roll = 0f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        Orient(go, yaw, pitch, roll);

        Bounds b = RenderBounds(go);
        Shift(go, _room.TransformPoint(centre) - b.center);
        return go;
    }

    /// <summary>Floor / ceiling tiles: centred on (x, z) with the bounds' top at topY.</summary>
    private static GameObject PlaceTile(Transform parent, string path, float x, float z, float topY, float yaw = 0f, float scaleMul = 1f, float pitch = 0f, float roll = 0f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        Orient(go, yaw, pitch, roll);

        Bounds b = RenderBounds(go);
        Vector3 target = _room.TransformPoint(new Vector3(x, topY, z));
        Shift(go, target - new Vector3(b.center.x, b.max.y, b.center.z));
        return go;
    }

    /// <summary>
    /// Sits the item's bounds on the top of support, centred at the support's bounds centre plus offsetXZ
    /// (room axes). lift raises (or sinks, if negative) it. align turns the item 90 degrees if its long
    /// horizontal axis does not match the support's, which is what keeps book rows along their furniture.
    /// </summary>
    private static GameObject PlaceOnTop(Transform parent, Bounds support, string path, Vector2 offsetXZ, float yaw = 0f, float pitch = 0f, float roll = 0f, float lift = 0f, bool align = false, float scaleMul = 1f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        Orient(go, yaw, pitch, roll);

        if (align)
        {
            Vector3 s = RenderBounds(go).size;
            bool itemLongX = s.x >= s.z;
            bool supportLongX = support.size.x >= support.size.z;
            if (itemLongX != supportLongX) Orient(go, 90f, 0f, 0f);
        }

        Bounds b = RenderBounds(go);
        Vector3 target = new Vector3(support.center.x + offsetXZ.x, support.max.y + lift, support.center.z + offsetXZ.y);
        Shift(go, target - new Vector3(b.center.x, b.min.y, b.center.z));
        return go;
    }

    private static GameObject PlaceOnTop(Transform parent, GameObject support, string path, Vector2 offsetXZ, float yaw = 0f, float pitch = 0f, float roll = 0f, float lift = 0f, bool align = false, float scaleMul = 1f)
    {
        return PlaceOnTop(parent, RenderBounds(support), path, offsetXZ, yaw, pitch, roll, lift, align, scaleMul);
    }

    private static float WallYaw(Wall wall)
    {
        switch (wall)
        {
            case Wall.North: return 0f;
            case Wall.South: return 180f;
            case Wall.East: return 90f;
            default: return -90f;
        }
    }

    /// <summary>
    /// Architecture: the bounds' room-facing side is put on the wall plane and the piece is centred at
    /// `along`. The thickness of the module therefore always ends up outside the room, whichever way
    /// the mesh faces; flip turns its detailed face the other way.
    /// </summary>
    private static GameObject PlaceWallPiece(Transform parent, string path, Wall wall, float along, float flip = 0f)
    {
        GameObject go = Spawn(parent, path, ArchScale);
        if (go == null) return null;
        // The wall meshes are single-sided and their visible face points along local +Z (the thickness runs the
        // same way), so a plain WallYaw showed the back from inside the room; +180 turns the face inward.
        Orient(go, WallYaw(wall) + 180f + flip, 0f, 0f);

        Bounds b = LocalBounds(go);
        Vector3 d = new Vector3(0f, -b.min.y, 0f);
        switch (wall)
        {
            case Wall.North: d.z = HalfZ - b.min.z; d.x = along - b.center.x; break;
            case Wall.South: d.z = -HalfZ - b.max.z; d.x = along - b.center.x; break;
            case Wall.East: d.x = HalfX - b.min.x; d.z = along - b.center.z; break;
            default: d.x = -HalfX - b.max.x; d.z = along - b.center.z; break;
        }
        Shift(go, d);
        return go;
    }

    /// <summary>
    /// Furniture standing against a wall, front toward the room. The smaller horizontal dimension is
    /// kept perpendicular to the wall (turns the piece 90 degrees if it is not), inset is the gap.
    /// </summary>
    private static GameObject PlaceAgainstWall(Transform parent, string path, Wall wall, float along, float inset = 0.05f, float extraYaw = 0f, float scaleMul = 1f)
    {
        GameObject go = Spawn(parent, path, scaleMul);
        if (go == null) return null;
        Orient(go, WallYaw(wall) + extraYaw, 0f, 0f);

        bool alongX = wall == Wall.North || wall == Wall.South;
        Vector3 size = RenderBounds(go).size;
        float perp = alongX ? size.z : size.x;
        float len = alongX ? size.x : size.z;
        if (perp > len + 0.05f) Orient(go, 90f, 0f, 0f);

        Bounds b = LocalBounds(go);
        Vector3 d = new Vector3(0f, -b.min.y, 0f);
        switch (wall)
        {
            case Wall.North: d.z = HalfZ - inset - b.max.z; d.x = along - b.center.x; break;
            case Wall.South: d.z = -HalfZ + inset - b.min.z; d.x = along - b.center.x; break;
            case Wall.East: d.x = HalfX - inset - b.max.x; d.z = along - b.center.z; break;
            default: d.x = -HalfX + inset - b.min.x; d.z = along - b.center.z; break;
        }
        Shift(go, d);
        return go;
    }

    /// <summary>
    /// Wall-hung item (paintings, shelves, the board): thin axis kept perpendicular to the wall, back
    /// 0.01 m off it, bounds centre at `height`, slid to `along`. frontIsPlusX/extraYaw fix a prefab whose
    /// painted side comes out facing the wall.
    /// </summary>
    private static GameObject PlaceOnWall(Transform parent, string path, Wall wall, float along, float height, float extraYaw = 0f, bool onPlaster = false)
    {
        GameObject go = Spawn(parent, path);
        if (go == null) return null;

        Vector3 raw = RenderBounds(go).size;
        float yaw = WallYaw(wall) + extraYaw + (raw.x < raw.z ? 90f : 0f);
        Orient(go, yaw, 0f, 0f);

        // The pack's timber frame stands on the wall plane and its plaster is recessed PlasterDepth behind it:
        // paintings hang on the plaster, shelves and the board hang on the frame.
        float depth = onPlaster ? PlasterDepth : 0f;
        Bounds b = LocalBounds(go);
        Vector3 d = new Vector3(0f, height - b.center.y, 0f);
        switch (wall)
        {
            case Wall.North: d.z = HalfZ + depth - 0.01f - b.max.z; d.x = along - b.center.x; break;
            case Wall.South: d.z = -HalfZ - depth + 0.01f - b.min.z; d.x = along - b.center.x; break;
            case Wall.East: d.x = HalfX + depth - 0.01f - b.max.x; d.z = along - b.center.z; break;
            default: d.x = -HalfX - depth + 0.01f - b.min.x; d.z = along - b.center.z; break;
        }
        Shift(go, d);
        return go;
    }

    /// <summary>Adds one invisible BoxCollider sibling matching the item's (axis-aligned) render bounds.</summary>
    private static void AddBlocker(GameObject go, float pad = 0f)
    {
        if (go == null) return;
        Bounds b = RenderBounds(go);
        GameObject blocker = new GameObject(go.name + "_Blocker");
        blocker.transform.SetParent(go.transform.parent, false);
        blocker.transform.position = b.center;
        BoxCollider box = blocker.AddComponent<BoxCollider>();
        box.size = b.size + Vector3.one * pad;
    }

    /// <summary>Keeps one of the pack's own lights and retunes it. Everything else the pack brought gets pruned in BuildLights.</summary>
    private static Light KeepLight(GameObject go, float range, float intensity)
    {
        if (go == null) return null;
        Light light = go.GetComponentInChildren<Light>(true);
        if (light == null)
        {
            Debug.LogWarning($"Shop room: {go.name} has no Light to keep.");
            return null;
        }
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.enabled = true;
        KeptLights.Add(light);
        return light;
    }

    // ---------------------------------------------------------------- clutter helpers

    /// <summary>A row of small items along X on a support (shelves), centred on the support.</summary>
    private static void ShelfRow(Transform parent, Bounds support, string[] items, float spacing, float zOffset = 0f)
    {
        for (int i = 0; i < items.Length; i++)
        {
            float x = (i - (items.Length - 1) * 0.5f) * spacing;
            float yaw = ((i * 53) % 60) - 30;
            PlaceOnTop(parent, support, items[i], new Vector2(x, zOffset), yaw);
        }
    }

    /// <summary>Flat books stacked on a support: each one rests on the last, with a little yaw drift.</summary>
    private static GameObject BookStack(Transform parent, Bounds support, Vector2 offset, string[] books, float yaw0 = 0f)
    {
        Bounds current = support;
        GameObject last = null;
        for (int i = 0; i < books.Length; i++)
        {
            float yaw = yaw0 + (i % 2 == 0 ? 8f : -11f) * (1 + i * 0.4f);
            Vector2 o = i == 0 ? offset : new Vector2(current.center.x - support.center.x + Mathf.Sin(i * 2.1f) * 0.02f, current.center.z - support.center.z + Mathf.Cos(i * 1.7f) * 0.02f);
            last = PlaceOnTop(parent, current, "Items/Books/" + books[i], o, yaw, pitch: 90f);
            if (last != null) current = RenderBounds(last);
        }
        return last;
    }

    /// <summary>Upright books side by side along X (spines toward -Z), as on a shelf.</summary>
    private static void BookRow(Transform parent, Bounds support, Vector2 start, string[] books, float spacing = 0.05f)
    {
        for (int i = 0; i < books.Length; i++)
        {
            float lean = i == books.Length - 1 ? 8f : 0f;
            PlaceOnTop(parent, support, "Items/Books/" + books[i], new Vector2(start.x + i * spacing, start.y), 90f, roll: lean);
        }
    }

    /// <summary>Items laid out on a small grid on a table top.</summary>
    private static void TableSpread(Transform parent, Bounds support, string[] items, float spacing, int columns)
    {
        int rows = Mathf.CeilToInt(items.Length / (float)columns);
        for (int i = 0; i < items.Length; i++)
        {
            float x = ((i % columns) - (columns - 1) * 0.5f) * spacing;
            float z = ((i / columns) - (rows - 1) * 0.5f) * spacing;
            PlaceOnTop(parent, support, items[i], new Vector2(x, z), (i * 67) % 360);
        }
    }

    // ---------------------------------------------------------------- shell

    private static void BuildShell(Transform root)
    {
        Transform shell = Group(root, "Shell");

        // Floor: wood everywhere, top surface at y = 0. The stone tile is a cut-out patch of flagstones rather
        // than a full floor, so it is laid over the wood behind the counter.
        float[] xs = { -Module, 0f, Module };
        float[] zs = { -1.5f * Module, -0.5f * Module, 0.5f * Module, 1.5f * Module };
        for (int zi = 0; zi < zs.Length; zi++)
        {
            foreach (float x in xs) PlaceTile(shell, "Architecture/IntWoodFloor01", x, zs[zi], 0f, scaleMul: ArchScale);
        }
        foreach (float x in xs) PlaceTile(shell, "Architecture/StoneFloor01", x, zs[zs.Length - 1], 0.004f, scaleMul: ArchScale);

        // Ceiling: underside at y = 3 (the mesh hangs 0.07 below its pivot).
        foreach (float z in zs)
        {
            foreach (float x in xs) PlaceTile(shell, "Architecture/IntCeiling01", x, z, RoomHeight + 0.07f, scaleMul: ArchScale);
        }

        // Walls
        PlaceWallPiece(shell, "Architecture/IntWall01a", Wall.North, -Module);
        PlaceWallPiece(shell, "Architecture/IntWall01b", Wall.North, 0f);
        PlaceWallPiece(shell, "Architecture/IntWall01a", Wall.North, Module);

        PlaceWallPiece(shell, "Architecture/IntWall01b", Wall.South, -Module);
        PlaceWallPiece(shell, "Architecture/IntWall01door", Wall.South, 0f);
        PlaceWallPiece(shell, "Architecture/IntWall01b", Wall.South, Module);

        for (int i = 0; i < zs.Length; i++)
        {
            string piece = i % 2 == 0 ? "Architecture/IntWall01a" : "Architecture/IntWall01b";
            PlaceWallPiece(shell, piece, Wall.East, zs[i]);
            PlaceWallPiece(shell, piece, Wall.West, zs[i]);
        }

        // Corner posts: pushed into the corner so they cover the seam without eating the floor.
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                PlaceOnFloor(shell, "Architecture/VerticalPillarBig", new Vector3(sx * (HalfX - 0.05f), 0f, sz * (HalfZ - 0.05f)), scaleMul: ArchScale);
            }
        }

        // Wall posts on every module seam, centred on the wall plane so half is buried in it.
        foreach (float x in new[] { -1.5f, 1.5f })
        {
            PlaceOnFloor(shell, "Architecture/VerticalPillar", new Vector3(x, 0f, HalfZ), scaleMul: ArchScale);
            PlaceOnFloor(shell, "Architecture/VerticalPillar", new Vector3(x, 0f, -HalfZ), scaleMul: ArchScale);
        }
        foreach (float z in new[] { -Module, 0f, Module })
        {
            PlaceOnFloor(shell, "Architecture/VerticalPillar", new Vector3(HalfX, 0f, z), scaleMul: ArchScale);
            PlaceOnFloor(shell, "Architecture/VerticalPillar", new Vector3(-HalfX, 0f, z), scaleMul: ArchScale);
        }

        // Ceiling beams: three lines across the room, each three segments, tops against the ceiling.
        foreach (float z in new[] { -Module, 0f, Module })
        {
            foreach (float x in xs)
            {
                GameObject beam = PlaceCentred(shell, "Architecture/HorizontalPillar", new Vector3(x, RoomHeight - 0.11f, z), scaleMul: ArchScale);
                if (beam != null)
                {
                    Bounds b = LocalBounds(beam);
                    Shift(beam, new Vector3(0f, RoomHeight - b.max.y, 0f));
                }
            }
        }
    }

    /// <summary>Six invisible boxes just outside the visible surfaces; they are the room's whole collision shell.</summary>
    private static void BuildColliders(Transform root)
    {
        Transform g = Group(root, "Colliders");
        ColliderBox(g, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(2f * HalfX + 1f, 0.5f, 2f * HalfZ + 1f));
        ColliderBox(g, "Ceiling", new Vector3(0f, RoomHeight + 0.25f, 0f), new Vector3(2f * HalfX + 1f, 0.5f, 2f * HalfZ + 1f));
        ColliderBox(g, "Wall_N", new Vector3(0f, 1.5f, HalfZ + 0.25f), new Vector3(2f * HalfX + 1f, 4f, 0.5f));
        ColliderBox(g, "Wall_S", new Vector3(0f, 1.5f, -HalfZ - 0.25f), new Vector3(2f * HalfX + 1f, 4f, 0.5f));
        ColliderBox(g, "Wall_E", new Vector3(HalfX + 0.25f, 1.5f, 0f), new Vector3(0.5f, 4f, 2f * HalfZ + 1f));
        ColliderBox(g, "Wall_W", new Vector3(-HalfX - 0.25f, 1.5f, 0f), new Vector3(0.5f, 4f, 2f * HalfZ + 1f));
    }

    /// <summary>
    /// Plain slabs just behind the pack walls and under the floor. The wall modules leave gaps above and below
    /// their timber rails (it looked straight out at the sky), so this gives those gaps a solid back.
    /// </summary>
    private static void BuildBackdrop(Transform root, Material material)
    {
        Transform g = Group(root, "Backdrop");
        const float inset = 0.37f; // the plaster sits ~0.33 m behind the wall plane; the slab goes just behind it
        const float thick = 0.2f;
        float h = RoomHeight + 0.3f;
        Primitive(g, PrimitiveType.Cube, "Back_N", new Vector3(0f, h * 0.5f - 0.1f, HalfZ + inset + thick * 0.5f), new Vector3(2f * HalfX + 2f, h, thick), material);
        Primitive(g, PrimitiveType.Cube, "Back_S", new Vector3(0f, h * 0.5f - 0.1f, -HalfZ - inset - thick * 0.5f), new Vector3(2f * HalfX + 2f, h, thick), material);
        Primitive(g, PrimitiveType.Cube, "Back_E", new Vector3(HalfX + inset + thick * 0.5f, h * 0.5f - 0.1f, 0f), new Vector3(thick, h, 2f * HalfZ + 2f), material);
        // The floor tiles stop at the wall plane but the plaster is recessed behind it: close the gap underneath too.
        Primitive(g, PrimitiveType.Cube, "Back_Floor", new Vector3(0f, -0.06f, 0f), new Vector3(2f * HalfX + 2f, 0.1f, 2f * HalfZ + 2f), material);
        Primitive(g, PrimitiveType.Cube, "Back_W",new Vector3(-HalfX - inset - thick * 0.5f, h * 0.5f - 0.1f, 0f), new Vector3(thick, h, 2f * HalfZ + 2f), material);
    }

    private static void ColliderBox(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.AddComponent<BoxCollider>().size = size;
    }

    // ---------------------------------------------------------------- door and counter

    /// <summary>Frame and leaf; returns the leaf's hinge pivot (ShopRoom.doorHinge), or null if the pack is missing.</summary>
    private static Transform BuildDoor(Transform root)
    {
        Transform g = Group(root, "Door");

        // The frame sits flush with the inner face of the south wall, in the door module's opening.
        GameObject frame = PlaceOnFloor(g, "Architecture/Door", new Vector3(0f, 0f, -HalfZ), scaleMul: ArchScale);
        if (frame == null) return null;
        Bounds fb = LocalBounds(frame);
        Shift(frame, new Vector3(0f, 0f, -HalfZ - fb.min.z + 0.02f));
        fb = LocalBounds(frame);

        // The pack's Door model is frame AND a closed panel fused into one mesh, so it can't open - and it hides the
        // separate leaf below. It is only a measuring stick for where the leaf goes; the wall module's door opening
        // already has its own frame, so it is removed again.
        Object.DestroyImmediate(frame);

        // Closed leaf, centred in the opening, on a hinge pivot at its edge so ShopRoom can swing it open.
        GameObject leaf = PlaceCentred(g, "Architecture/WoodDoor01", new Vector3(fb.center.x, fb.min.y + 0.5f * 1.93f * ArchScale, fb.center.z), scaleMul: ArchScale);
        if (leaf == null) return null;
        Transform hinge = ShopRoom.CreateDoorHinge(_room, leaf.transform);
        MakeMovable(hinge);
        return hinge;
    }

    /// <summary>
    /// Clears every static flag under root. A static leaf is merged into a combined mesh by static batching when
    /// the scene loads, after which turning its hinge moves nothing on screen - the door would never open.
    /// </summary>
    internal static void MakeMovable(Transform root)
    {
        if (root == null) return;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
    }

    private static GameObject[] BuildCounter(Transform root)
    {
        Transform g = Group(root, "Counter");

        GameObject left = PlaceOnFloor(g, "Furniture/WoodTableSquare01b", new Vector3(-0.75f, 0f, 4f));
        GameObject right = PlaceOnFloor(g, "Furniture/WoodTableSquare01b", new Vector3(0.75f, 0f, 4f));
        // Long axis along X: if the prefab came out the other way, turn both.
        if (left != null && RenderBounds(left).size.x < RenderBounds(left).size.z)
        {
            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
            left = PlaceOnFloor(g, "Furniture/WoodTableSquare01b", new Vector3(-0.75f, 0f, 4f), 90f);
            right = PlaceOnFloor(g, "Furniture/WoodTableSquare01b", new Vector3(0.75f, 0f, 4f), 90f);
        }

        // The old counter's exact footprint, so the zone and the salesman do not move.
        ColliderBox(g, "CounterBlocker", new Vector3(0f, 0.525f, 4f), new Vector3(3.2f, 1.05f, 0.9f));
        return new[] { left, right };
    }

    private static void DressCounter(Transform parent, GameObject[] tables)
    {
        Bounds top = Union(tables);

        KeepLight(PlaceOnTop(parent, top, "Items/Lights/Oillamp01_on", new Vector2(-1.25f, 0f)), 5f, 1.8f);
        PlaceOnTop(parent, top, "Items/Misc/Globe", new Vector2(1.2f, 0.1f), 30f);
        PlaceOnTop(parent, top, "Items/Lab/LabSetup02", new Vector2(-0.75f, 0.15f));

        GameObject b1 = PlaceOnTop(parent, top, "Items/Books/Book03", new Vector2(0.7f, -0.1f), 10f, pitch: 90f);
        PlaceOnTop(parent, RenderBounds(b1), "Items/Books/Book07", Vector2.zero, -15f, pitch: 90f);
        PlaceOnTop(parent, top, "Items/Organic/Skull01", new Vector2(0.95f, 0.2f), 200f);

        PlaceOnTop(parent, top, "Items/Bottles/Flask02", new Vector2(-0.45f, -0.2f));
        PlaceOnTop(parent, top, "Items/Bottles/Flask05", new Vector2(-0.3f, -0.05f));
        PlaceOnTop(parent, top, "Items/Bottles/Flask07", new Vector2(-0.5f, 0.1f));

        PlaceOnTop(parent, top, "Items/Ingredients/ClutterBowl03Ing01", new Vector2(0.45f, 0.2f));
        PlaceOnTop(parent, top, "Items/Ingredients/ClutterBowl03Ing05", new Vector2(-1.0f, -0.2f));
        PlaceOnTop(parent, top, "Items/Dishes/ClutterTanker01", new Vector2(1.35f, -0.2f), 40f);
    }

    // ---------------------------------------------------------------- back wall

    private static void BuildBackWall(Transform root)
    {
        Transform g = Group(root, "BackWall");

        GameObject lower = PlaceOnWall(g, "Furniture/WoodShelf01", Wall.North, 0f, 1.45f);
        GameObject upper = PlaceOnWall(g, "Furniture/WoodShelf01", Wall.North, 0f, 2.15f);
        ShelfRow(g, RenderBounds(lower), new[]
        {
            "Items/Bottles/Bottle01", "Items/Bottles/Bottle02b", "Items/Bottles/Bottle03", "Items/Bottles/Bottle04b",
            "Items/Bottles/Flask01", "Items/Bottles/Flask03", "Items/Bottles/Flask04", "Items/Bottles/Flask06",
        }, 0.2f);
        ShelfRow(g, RenderBounds(upper), new[]
        {
            "Items/Organic/JarBrain", "Items/Organic/Jar01", "Items/Organic/JarBaby01", "Items/Organic/JarBaby02",
            "Items/Bottles/Bottle01b", "Items/Bottles/Bottle02", "Items/Lights/Candle01c",
        }, 0.24f);

        GameObject cup1 = PlaceAgainstWall(g, "Furniture/WoodCupboard01", Wall.North, -3.2f);
        GameObject cup3 = PlaceAgainstWall(g, "Furniture/WoodCupboard03", Wall.North, 3.2f);
        AddBlocker(cup1);
        AddBlocker(cup3);
        PlaceOnTop(g, cup1, "Items/Misc/Basket02", new Vector2(-0.3f, 0f));
        BookRow(g, RenderBounds(cup1), new Vector2(0.05f, 0f), new[] { "Book09d", "Book09e", "Book10", "Book10b", "Book10c", "Book11" });
        PlaceOnTop(g, cup3, "Items/Organic/DragonSkull", new Vector2(-0.2f, 0f), 180f);
        BookRow(g, RenderBounds(cup3), new Vector2(0.3f, 0f), new[] { "Book12", "Book13", "Book14", "Book08" });

        GameObject stove = PlaceAgainstWall(g, "Furniture/StoveLit", Wall.North, -2.0f);
        AddBlocker(stove);
        KeepLight(stove, 3.5f, 2f);

        GameObject basket = PlaceOnFloor(g, "Items/Misc/Basket01", new Vector3(-1.2f, 0f, 5.5f));
        PlaceOnTop(g, basket, "Items/Misc/Log01", new Vector2(-0.04f, 0.02f), 20f, lift: -0.06f);
        PlaceOnTop(g, basket, "Items/Misc/Log02", new Vector2(0.05f, -0.03f), -30f, lift: -0.06f);
        PlaceOnTop(g, basket, "Items/Misc/Log03", new Vector2(0f, 0.06f), 75f, lift: -0.02f);

        GameObject caldron = PlaceOnFloor(g, "Items/Lab/CaldronTripod01_Bubbles", new Vector3(1.9f, 0f, 5.5f));
        AddBlocker(caldron);
    }

    // ---------------------------------------------------------------- lab side (west)

    private static void BuildLabSide(Transform root)
    {
        Transform g = Group(root, "LabSide");

        GameObject sink = PlaceAgainstWall(g, "Furniture/TableSink", Wall.West, 2.2f, 0.1f);
        GameObject alembic = PlaceAgainstWall(g, "Furniture/Alembic02", Wall.West, -0.4f, 0.1f);
        GameObject bench = PlaceAgainstWall(g, "Furniture/Workbench", Wall.West, -2.6f, 0.1f);
        GameObject stove = PlaceAgainstWall(g, "Furniture/Stove", Wall.West, -4.2f, 0.1f);
        foreach (GameObject f in new[] { sink, alembic, bench, stove }) AddBlocker(f);

        PlaceOnWall(g, "Furniture/Board", Wall.West, -2.6f, 1.9f);

        // Workbench: x is depth (wall at -x), z runs along the bench.
        Bounds top = RenderBounds(bench);
        PlaceOnTop(g, top, "Items/Lab/LabSetup01", new Vector2(-0.12f, -0.6f));
        PlaceOnTop(g, top, "Items/Lab/LabSetup03", new Vector2(-0.1f, -0.2f));
        PlaceOnTop(g, top, "Items/Lab/CaldronTripod02", new Vector2(-0.1f, 0.2f));
        PlaceOnTop(g, top, "Items/Lab/Caldron01", new Vector2(0.1f, 0.5f));
        PlaceOnTop(g, top, "Items/Bottles/Flask02", new Vector2(0.18f, -0.7f), 20f);
        PlaceOnTop(g, top, "Items/Dishes/ClutterBowl01", new Vector2(0.2f, -0.45f));
        PlaceOnTop(g, top, "Items/Ingredients/ClutterBowl03Ing02", new Vector2(0.2f, -0.15f));
        PlaceOnTop(g, top, "Items/Ingredients/ClutterBowl03Ing03", new Vector2(0.22f, 0.15f));
        PlaceOnTop(g, top, "Items/Ingredients/ClutterBowl03Ing04", new Vector2(0.2f, 0.7f));
        KeepLight(PlaceOnTop(g, top, "Items/Lights/Candle01b_on", new Vector2(0.15f, 0.34f)), 4f, 1.4f);
        BookStack(g, top, new Vector2(0.18f, 0.5f), new[] { "Book06b", "Book06c" });

        Bounds sinkTop = RenderBounds(sink);
        PlaceOnTop(g, sinkTop, "Items/Dishes/ClutterPichet01", new Vector2(0f, -0.8f));
        PlaceOnTop(g, sinkTop, "Items/Dishes/ClutterPichet03", new Vector2(0f, -0.4f));
        PlaceOnTop(g, sinkTop, "Items/Dishes/ClutterBowl02", new Vector2(0f, 0.05f));
        PlaceOnTop(g, sinkTop, "Items/Dishes/ClutterGlass01", new Vector2(0f, 0.45f));
        PlaceOnTop(g, sinkTop, "Items/Dishes/ClutterGlass02", new Vector2(0f, 0.8f));

        PlaceOnTop(g, stove, "Items/Organic/Brain", Vector2.zero, 60f);

        PlaceOnFloor(g, "Items/Lab/CaldronTripod03", new Vector3(-3.3f, 0f, 0.95f));
        GameObject basket = PlaceOnFloor(g, "Items/Misc/Basket03", new Vector3(-3.3f, 0f, -1.52f));
        for (int i = 0; i < 3; i++)
        {
            PlaceOnTop(g, basket, "Items/Organic/Bulb01", new Vector2(-0.07f + i * 0.07f, 0.04f * (i - 1)), i * 90f, lift: -0.1f);
        }
        PlaceOnTop(g, basket, "Items/Organic/Apple", new Vector2(-0.06f, -0.07f), lift: -0.1f);
        PlaceOnTop(g, basket, "Items/Organic/Apple", new Vector2(0.07f, -0.08f), 120f, lift: -0.1f);

        PlaceOnWall(g, "Items/Misc/painting04", Wall.West, 0.9f, 2.1f, onPlaster: true);
    }

    // ---------------------------------------------------------------- library side (east)

    private static void BuildLibrarySide(Transform root)
    {
        Transform g = Group(root, "LibrarySide");

        GameObject cup2 = PlaceAgainstWall(g, "Furniture/WoodCupboard02", Wall.East, 2.4f, 0.1f, extraYaw: 180f); // this one's front is local +Z, unlike the other cupboards
        GameObject drawer1 = PlaceAgainstWall(g, "Furniture/Drawer01", Wall.East, 0.9f, 0.1f);
        GameObject drawer3 = PlaceAgainstWall(g, "Furniture/Drawer03", Wall.East, -0.6f, 0.1f);
        GameObject wood1 = PlaceAgainstWall(g, "Furniture/WoodDrawer01", Wall.East, -2.1f, 0.1f);
        GameObject drawer2 = PlaceAgainstWall(g, "Furniture/Drawer02", Wall.East, -3.6f, 0.1f);
        foreach (GameObject f in new[] { cup2, drawer1, drawer3, wood1, drawer2 }) AddBlocker(f);

        // Each long book block runs the length of its furniture; small things sit on top of the block.
        GameObject b4 = PlaceOnTop(g, cup2, "Items/Books/Books4", Vector2.zero, align: true);
        GameObject b2 = PlaceOnTop(g, drawer1, "Items/Books/Books2", Vector2.zero, align: true);
        GameObject b1 = PlaceOnTop(g, drawer3, "Items/Books/Books1", Vector2.zero, align: true);
        GameObject b3 = PlaceOnTop(g, wood1, "Items/Books/Books3", Vector2.zero, align: true);
        GameObject b5 = PlaceOnTop(g, drawer2, "Items/Books/Books5", Vector2.zero, align: true);

        PlaceOnTop(g, b4, "Items/Organic/Skull02", new Vector2(0f, -0.15f), 90f);
        KeepLight(PlaceOnTop(g, b1, "Items/Lights/Oillamp01_on", new Vector2(0f, 0.45f)), 5f, 1.6f);
        PlaceOnTop(g, b1, "Items/Lights/Candle01", new Vector2(0f, -0.4f));
        PlaceOnTop(g, b1, "Items/Organic/Skull01b", new Vector2(0f, 0f), 80f);
        PlaceOnTop(g, b2, "Items/Lights/Candle01d", new Vector2(0f, 0.3f));
        PlaceOnTop(g, b3, "Items/Dishes/ClutterPichet02", new Vector2(0f, 0.1f));
        PlaceOnTop(g, b5, "Items/Organic/JarBaby03", new Vector2(0f, -0.5f));

        // Floor pile of flat books near the corner chairs.
        BookStack(g, new Bounds(new Vector3(3.5f, 0f, -2.9f) + _room.position, new Vector3(0.4f, 0f, 0.4f)), Vector2.zero,
            new[] { "Book01", "Book02", "Book04", "Book05", "Book05b" });

        PlaceOnWall(g, "Items/Misc/Painting01", Wall.East, 0.9f, 2.25f, onPlaster: true);
        PlaceOnWall(g, "Items/Misc/painting02", Wall.East, -2.1f, 1.75f, onPlaster: true);
        PlaceOnWall(g, "Items/Misc/painting03", Wall.East, -0.9f, 2.1f, onPlaster: true);

        // A reading table out in the free strip between the aisle and the drawers.
        GameObject reading = PlaceOnFloor(g, "Furniture/WoodTableSquare01", new Vector3(2.85f, 0f, 0.6f));
        AddBlocker(reading);
        PlaceOnTop(g, reading, "Items/Weapons/Dagger01", new Vector2(0f, 0.25f), 35f);
    }

    // ---------------------------------------------------------------- corners

    private static void BuildCorners(Transform root)
    {
        Transform g = Group(root, "Corners");

        // South-east: a sitting corner.
        GameObject table = PlaceOnFloor(g, "Furniture/table03", new Vector3(3.0f, 0f, -4.85f));
        AddBlocker(table);
        PlaceOnFloor(g, "Furniture/Chair01", new Vector3(2.2f, 0f, -4.85f), 90f);
        PlaceOnFloor(g, "Furniture/Chair01", new Vector3(3.0f, 0f, -3.8f), 180f);
        PlaceOnFloor(g, "Furniture/WoodStool", new Vector3(2.6f, 0f, -3.1f));
        TableSpread(g, RenderBounds(table), new[]
        {
            "Items/Dishes/ClutterPlate01", "Items/Dishes/ClutterPlate01", "Items/Dishes/ClutterBowl03", "Items/Dishes/ClutterPichet01",
            "Items/Dishes/ClutterGlass01", "Items/Dishes/ClutterTanker01", "Items/Organic/Apple", "Items/Lights/Candle01b",
            "Items/Ingredients/ClutterBowl03Ing06", "Items/Ingredients/ClutterBowl03Ing07", "Items/Ingredients/ClutterBowl03Ing03b", "Items/Ingredients/ClutterBowl03Ing04b",
        }, 0.3f, 4);
        BookStack(g, RenderBounds(table), new Vector2(0.45f, 0.45f), new[] { "Book05c", "Book06" });

        // South-west: a storage corner.
        GameObject trunk = PlaceOnFloor(g, "Items/Misc/Trunk01", new Vector3(-3.9f, 0f, -5.4f), 90f);
        PlaceOnTop(g, trunk, "Items/Weapons/Axe01", Vector2.zero, 20f);
        GameObject table1 = PlaceOnFloor(g, "Furniture/Table01", new Vector3(-2.8f, 0f, -5.55f), 90f);
        AddBlocker(table1);
        PlaceOnTop(g, table1, "Items/Weapons/Arrow01", new Vector2(-0.25f, 0.1f), 5f);
        PlaceOnTop(g, table1, "Items/Weapons/Arrow01", new Vector2(-0.25f, 0.0f), -4f);
        PlaceOnTop(g, table1, "Items/Weapons/Arrow01", new Vector2(-0.25f, -0.1f), 8f);
        PlaceOnTop(g, table1, "Items/Lights/Oillamp01", new Vector2(0.4f, 0.05f));
        PlaceOnTop(g, table1, "Items/Bottles/Bottle03b", new Vector2(0.05f, -0.1f));
        PlaceOnTop(g, table1, "Items/Bottles/Bottle04", new Vector2(0.12f, 0.12f));
        BookStack(g, RenderBounds(table1), new Vector2(-0.1f, 0f), new[] { "Book09", "Book09b", "Book09c" });
        PlaceOnFloor(g, "Furniture/WoodStool", new Vector3(-2.4f, 0f, -4.5f));

        // Weapons leaning against the west wall, tilted into it (they lie along Z by default).
        string[] leaners = { "Items/Weapons/Staff01", "Items/Weapons/Bow01", "Items/Weapons/Sword01" };
        for (int i = 0; i < leaners.Length; i++)
        {
            PlaceOnFloor(g, leaners[i], new Vector3(-4.3f, 0f, -4.85f - i * 0.2f), -90f, pitch: -78f);
        }

        // Staff-only rail either side of the counter: a fence run along Z with a post at each end.
        foreach (float sx in new[] { -1f, 1f })
        {
            GameObject rail = PlaceOnFloor(g, "Architecture/IntWoodFence01b", new Vector3(sx * 1.95f, 0f, 4.5f), 90f, ArchScale);
            AddBlocker(rail, 0.05f);
            PlaceOnFloor(g, "Architecture/IntWoodFence01pillar", new Vector3(sx * 1.95f, 0f, 3.8f), 0f, ArchScale);
            PlaceOnFloor(g, "Architecture/IntWoodFence01pillar", new Vector3(sx * 1.95f, 0f, 5.18f), 0f, ArchScale);
            // Closes the little gap between the counter end and the rail.
            ColliderBox(g, "RailGap", new Vector3(sx * 1.78f, 0.5f, 3.95f), new Vector3(0.4f, 1f, 0.4f));
        }
    }

    // ---------------------------------------------------------------- carpets

    private static void BuildCarpets(Transform root)
    {
        Transform g = Group(root, "Carpets");
        PlaceOnFloor(g, "Furniture/Carpet03", new Vector3(0f, 0.005f, -1.5f));
        PlaceOnFloor(g, "Furniture/Carpet01", new Vector3(0f, 0.005f, 2.4f), 90f);
        PlaceOnFloor(g, "Furniture/Carpet02", new Vector3(0f, 0.005f, -4.9f));
    }

    // ---------------------------------------------------------------- lights and audit

    /// <summary>Drops every pack Light except the ones KeepLight chose, then adds the three fill lights.</summary>
    private static void BuildLights(Transform root)
    {
        foreach (Light l in root.GetComponentsInChildren<Light>(true))
        {
            if (!KeptLights.Contains(l)) Object.DestroyImmediate(l);
        }

        Transform lights = Group(root, "Lights");
        PointLight(lights, "CentreFill", new Vector3(0f, 2.6f, -1f), new Color(1f, 0.82f, 0.62f), 8f, 0.7f);
        PointLight(lights, "CounterLight", new Vector3(0f, 2.5f, 3.2f), Amber, 5f, 1.4f);
        PointLight(lights, "DoorLight", new Vector3(0f, 1.5f, -5.5f), HatchGreen, 5f, 1f);
    }

    /// <summary>Checks the plan's assertions and returns a multi-line report; problems are also logged as warnings.</summary>
    public static string Audit(Transform root)
    {
        StringBuilder sb = new StringBuilder();
        Vector3 origin = root.position;

        int outside = 0;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.transform.parent != null && r.transform.parent.name == "Backdrop") continue;
            Bounds b = r.bounds;
            Vector3 lo = b.min - origin;
            Vector3 hi = b.max - origin;
            if (lo.x < -HalfX - 0.4f || hi.x > HalfX + 0.4f || lo.z < -HalfZ - 0.4f || hi.z > HalfZ + 0.4f || hi.y > RoomHeight + 0.4f)
            {
                outside++;
                Debug.LogWarning($"Shop room: {r.name} leaves the room (bounds {b.min - origin} to {b.max - origin}).", r);
            }
        }
        sb.AppendLine($"renderers outside room: {outside}");

        int rigid = root.GetComponentsInChildren<Rigidbody>(true).Length;
        int meshCol = root.GetComponentsInChildren<MeshCollider>(true).Length;
        int vol = 0;
        int missing = 0;
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null) missing++;
            else if (c.GetType().Name == "VolumetricLight") vol++;
        }
        sb.AppendLine($"rigidbodies {rigid}, mesh colliders {meshCol}, volumetric lights {vol}, missing scripts {missing}");

        Light[] lights = root.GetComponentsInChildren<Light>(true);
        int shadowed = 0;
        foreach (Light l in lights) if (l.shadows != LightShadows.None) shadowed++;
        sb.AppendLine($"lights {lights.Length} (shadowed {shadowed})");

        int badMats = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null) continue;
            bool particle = r is ParticleSystemRenderer;
            foreach (Material m in r.sharedMaterials)
            {
                // The pack leaves an unused Default-Material slot on some bottles and a null trail slot on particles; URP renders both harmlessly.
                if (particle && m == null) continue;
                if (m != null && m.name == "Default-Material") continue;
                if (m == null ||m.shader == null || m.shader.name.StartsWith("Hidden/") || !m.shader.name.StartsWith("Universal Render Pipeline/"))
                {
                    badMats++;
                    Debug.LogWarning($"Shop room: {r.name} has a non-URP / missing material ({(m != null ? m.name : "null")}).", r);
                }
            }
        }
        sb.AppendLine($"non-URP materials: {badMats}");

        Bounds aisle = new Bounds(origin + new Vector3(0f, 1.5f, -1.25f), new Vector3(4f, 2.8f, 9.4f));
        List<string> blockers = new List<string>();
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            if (c.name == "Body") continue; // the salesman is at z 5.1, outside the aisle anyway
            if (c.bounds.Intersects(aisle)) blockers.Add(c.name);
        }
        sb.AppendLine("aisle colliders: " + (blockers.Count == 0 ? "none" : string.Join(", ", blockers)));
        foreach (string b in blockers) Debug.LogWarning("Shop room: collider in the keep-clear aisle: " + b);

        return sb.ToString();
    }
}
