using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F91 (plannings/intake-room-polish-plan.md): the Intake as a dirty Ward admissions bay. Metric-UV shell boxes in the
/// Ward materials, pillars, ceiling fixtures from the Ward lamp model, the kit shutter and bell, pack furniture, decals,
/// signs, dust and drips, and the IntakeAmbience wiring. Adds no Light: the room's budget is already at the Audit cap.
/// Nothing here moves a tutorial point; every standing prop gets a blocker, corridor E gets decals and particles only.
/// </summary>
public static partial class IntakeRoomBuilder
{
    private const string WardFolder = "Assets/SourceFiles/Themes/1_Ward/";
    private static readonly Color ColdWhite = new Color(0.80f, 0.92f, 1.0f);

    private static Vector3 _table1, _table1Max, _table2, _table2Max;
    private static readonly List<TMP_Text> _flickerSigns = new List<TMP_Text>();
    private static readonly List<Transform> _soundPoints = new List<Transform>();
    private static Transform _decals, _props;

    // ---------------------------------------------------------------- shell: metric UVs (D2)

    /// <summary>
    /// Swaps the box's cube mesh for one whose UVs are room-local metres (vertical faces /(5,4), horizontal /4.5), so the
    /// Ward materials' own tiling reads at the maze's density and neighbouring boxes line up. The box's scale, position
    /// and BoxCollider are untouched. The mesh lives in the scene, not as an asset.
    /// </summary>
    private static void MetricBox(GameObject go)
    {
        Vector3 c = go.transform.localPosition;
        Vector3 s = go.transform.localScale;
        // face: normal, tangent1, tangent2 with t1 x t2 = normal
        Vector3[][] faces =
        {
            new[] { Vector3.right, Vector3.up, Vector3.forward },
            new[] { Vector3.left, Vector3.forward, Vector3.up },
            new[] { Vector3.up, Vector3.forward, Vector3.right },
            new[] { Vector3.down, Vector3.right, Vector3.forward },
            new[] { Vector3.forward, Vector3.right, Vector3.up },
            new[] { Vector3.back, Vector3.up, Vector3.right },
        };

        List<Vector3> verts = new List<Vector3>(24);
        List<Vector3> norms = new List<Vector3>(24);
        List<Vector2> uvs = new List<Vector2>(24);
        List<int> tris = new List<int>(36);
        foreach (Vector3[] f in faces)
        {
            Vector3 n = f[0], t1 = f[1], t2 = f[2];
            int start = verts.Count;
            Vector2[] corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (Vector2 q in corners)
            {
                Vector3 p = n * 0.5f + t1 * (q.x * 0.5f) + t2 * (q.y * 0.5f);
                Vector3 local = c + Vector3.Scale(p, s);
                verts.Add(p);
                norms.Add(n);
                if (Mathf.Abs(n.y) > 0.5f) uvs.Add(new Vector2(local.x / 4.5f, local.z / 4.5f));
                else if (Mathf.Abs(n.x) > 0.5f) uvs.Add(new Vector2(local.z / 5f, local.y / 4f));
                else uvs.Add(new Vector2(local.x / 5f, local.y / 4f));
            }
            // Unity front faces wind clockwise seen from outside; the reverse order showed the box's far, unlit inner faces (black in Play).
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        Mesh mesh = new Mesh { name = "IntakeBox_" + go.name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private static Material WardMaterial(string name, Material fallback)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>($"{WardFolder}Materials/{name}.mat");
        if (m == null) Debug.LogWarning($"Build Intake Room: Ward material '{name}' not found, using the flat Intake one.");
        return m != null ? m : fallback;
    }

    // ---------------------------------------------------------------- pillars (D3)

    private static void BuildPillars()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardFolder + "Prefabs/Ward_Pillar.prefab");
        if (prefab == null) { Debug.LogWarning("Build Intake Room: Ward_Pillar prefab missing, no pillars."); return; }
        Transform group = Group(_root, "Pillars");

        const float half = 0.28f; // 0.8 x the prefab's 0.7 m: proud of a 0.2 m wall by 0.18 m each side
        // A -> B doorway (wall z 3.5..3.7), B -> C doorway (z 12.0..12.2): beside the opening, never in it.
        foreach (float sx in new[] { -1f, 1f })
        {
            Pillar(prefab, group, new Vector3(sx * (0.8f + half), 0f, 3.6f), true);
            Pillar(prefab, group, new Vector3(sx * (1.0f + half), 0f, 12.1f), true);
        }
        // B -> E side opening (east wall x 2.0..2.2, gap z 7.8..10.2). Corridor E gets no colliders at all.
        Pillar(prefab, group, new Vector3(2.1f, 0f, 7.8f - half), false);
        Pillar(prefab, group, new Vector3(2.1f, 0f, 10.2f + half), false);
        // A's corners (the south-west one is the bed's head).
        float c = 3.5f - half;
        Pillar(prefab, group, new Vector3(c, 0f, -c), true);
        
        Pillar(prefab, group, new Vector3(c, 0f, c), true);
    }

    private static void Pillar(GameObject prefab, Transform parent, Vector3 pos, bool collider)
    {
        GameObject p = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(p, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        p.name = "Pillar";
        p.transform.localPosition = pos;
        p.transform.localScale = new Vector3(0.8f, 0.75f, 0.8f);
        if (!collider) foreach (Collider col in p.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
    }

    // ---------------------------------------------------------------- fixtures (D4)

    private static Material GlassMat(string name, Color emission, Material fallback)
    {
        Material src = AssetDatabase.LoadAssetAtPath<Material>(WardFolder + "Materials/Ward_Glass.mat");
        if (src == null) return fallback;
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(src) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission * 2.2f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(Color.black, emission, 0.35f));
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A Ward_Lamp strip mounted flat on the ceiling, glass facing down. Returns the glass renderer.</summary>
    private static Renderer BuildCeilingFixture(string name, Vector3 ceilingPos, float yaw, Material glass, Material fallback)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardFolder + "Prefabs/Ward_Lamp.prefab");
        if (prefab == null)
        {
            Debug.LogWarning("Build Intake Room: Ward_Lamp prefab missing, using a plain lamp box.");
            GameObject box = Prim(_root, PrimitiveType.Cube, name, ceilingPos + Vector3.down * 0.07f, new Vector3(0.7f, 0.1f, 0.4f), fallback);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            return box.GetComponent<Renderer>();
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        go.transform.localPosition = ceilingPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
        Transform g = go.transform.Find("Glass");
        Renderer r = g != null ? g.GetComponent<Renderer>() : go.GetComponentInChildren<Renderer>();
        r.sharedMaterial = glass;
        MakeMovable(go.transform);
        return r;
    }

    /// <summary>LampC becomes an oil lamp hanging from a hook. The pivot (at the ceiling) sways in IntakeAmbience.</summary>
    private static void BuildHangingLamp(Light lampC, Vector3 ceilingPos, Mats m, out Transform pivot, out Renderer glow)
    {
        pivot = Group(_root, "HangingLamp");
        pivot.localPosition = ceilingPos;
        glow = null;
        GameObject lamp = AlchemistPack.PlaceCentred(pivot, "Items/Lights/Oillamp01_on", new Vector3(0f, -0.5f, 0f), Quaternion.identity);
        if (lamp == null)
        {
            Debug.LogWarning("Build Intake Room: Oillamp01_on missing, LampC just hangs bare.");
            return;
        }
        lamp.name = "OilLamp";
        // top of the lamp 0.12 m under the ceiling
        Bounds b = AlchemistPack.RenderBounds(lamp);
        lamp.transform.position += Vector3.up * (_root.position.y + ceilingPos.y - 0.12f - b.max.y);
        b = AlchemistPack.RenderBounds(lamp);
        lampC.transform.SetParent(pivot, true);
        lampC.transform.position = b.center;
        glow = lamp.GetComponentInChildren<Renderer>();
        MakeMovable(pivot);
    }

    // ---------------------------------------------------------------- kit pieces: bell, shutter (D4)

    private static void StripToVisual(GameObject go)
    {
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        AlchemistPack.Strip(go, true);
    }

    private static IntakeTarget BuildBellKit(InteractableKit kit, Mats m)
    {
        if (kit == null || kit.CallBell == null)
        {
            Debug.LogWarning("Build Intake Room: no CallBell template, the bell stays the old primitive.");
            return null;
        }

        Transform bellRoot = Group(_root, "Bell");
        bellRoot.localPosition = new Vector3(2.2f, 1.75f, 3.2f);

        GameObject visual = Object.Instantiate(kit.CallBell.gameObject, bellRoot);
        visual.name = "Visual";
        visual.SetActive(true);
        StripToVisual(visual);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * 1.8f;

        // The target: same object as the trigger, ~0.8 m world radius like before.
        GameObject hit = new GameObject("Bell");
        hit.transform.SetParent(bellRoot, false);
        SphereCollider sphere = hit.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.8f;
        IntakeTarget target = hit.AddComponent<IntakeTarget>();
        MakeMovable(bellRoot);

        // It is a desk bell (the "bracket" is its plunger), so it stands on a short bare shelf. The full 1.8 m shelf
        // ran into the doorway jamb pillar and the corner pillar, and the bell sank into it; this one is 1.08 m,
        // centred between them (free span x 1.36..2.94), its plank top at the bell's old bottom, the bell stood on it.
        GameObject shelf = AlchemistPack.Place(_props, "Furniture/WoodShelf01", new Vector3(2.15f, 1.5f, 3.3f), Quaternion.LookRotation(Vector3.back), 0.6f);
        if (shelf != null)
        {
            shelf.name = "BellShelf";
            Bounds sb = AlchemistPack.RenderBounds(shelf);
            shelf.transform.position += new Vector3(0f, _root.position.y + 1.64f - sb.max.y, _root.position.z + 3.5f - sb.max.z);
            sb = AlchemistPack.RenderBounds(shelf);
            Bounds vb = AlchemistPack.RenderBounds(visual);
            // The bell is deeper than the plank, so it goes flush against the wall rather than centred (which put it into the wall).
            bellRoot.position += new Vector3(sb.center.x - vb.center.x, sb.max.y - vb.min.y, sb.max.z - 0.005f - vb.max.z);
        }
        return target;
    }

    private static Transform BuildShutterKit(InteractableKit kit, Mats m)
    {
        if (kit == null || kit.ShutterDoor == null)
        {
            Debug.LogWarning("Build Intake Room: no ShutterDoor template, the shutter stays the old primitive.");
            return null;
        }

        Transform shutter = Group(_root, "Shutter");
        shutter.localPosition = Vector3.zero;

        GameObject visual = Object.Instantiate(kit.ShutterDoor.gameObject, shutter);
        visual.name = "Visual";
        visual.SetActive(true);
        StripToVisual(visual);
        Transform status = visual.transform.Find("StatusLight");
        if (status != null) Object.DestroyImmediate(status.gameObject);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        Bounds b = AlchemistPack.RenderBounds(visual);
        if (b.size.x < 0.01f || b.size.y < 0.01f) { Object.DestroyImmediate(shutter.gameObject); return null; }
        visual.transform.localScale = new Vector3(1.6f / b.size.x, 2.4f / b.size.y, Mathf.Min(1f, 0.18f / Mathf.Max(0.001f, b.size.z)));
        b = AlchemistPack.RenderBounds(visual);
        visual.transform.position += _root.TransformPoint(new Vector3(0f, 1.2f, 3.6f)) - b.center;

        GameObject blocker = new GameObject("Blocker");
        blocker.transform.SetParent(shutter, false);
        blocker.transform.localPosition = new Vector3(0f, 1.2f, 3.6f);
        blocker.AddComponent<BoxCollider>().size = new Vector3(1.6f, 2.4f, 0.2f);
        MakeMovable(shutter);
        return shutter;
    }

    // ---------------------------------------------------------------- placement helpers

    private static void TrimVariants(GameObject go)
    {
        foreach (ModelVariants mv in go.GetComponentsInChildren<ModelVariants>(true))
        {
            mv.Apply(0);
            List<GameObject> drop = new List<GameObject>();
            foreach (Transform child in mv.transform) if (!child.gameObject.activeSelf) drop.Add(child.gameObject);
            foreach (GameObject d in drop) Object.DestroyImmediate(d);
        }
    }

    private static GameObject SpawnComposed(string rel, Quaternion rotation)
    {
        string full = rel.Contains("/") ? rel : "Alchemist/" + rel;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + full + ".prefab");
        if (prefab == null) { Debug.LogWarning($"Build Intake Room: prefab '{full}' not found."); return null; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _props);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = rotation;
        TrimVariants(go);
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        AlchemistPack.Strip(go, true);
        AlchemistPack.FixDefaultMaterials(go);
        return go;
    }

    /// <summary>
    /// A composed wall prop (+Z = front, back at the wall) against an axis-aligned wall. plane = the wall face coordinate
    /// (x for an east/west wall, z for north/south), along = the position along the wall, bottomY = room-local y of the
    /// piece's lowest point. inward is the direction the front faces.
    /// </summary>
    private static GameObject PlaceOnWall(string rel, Vector3 inward, float plane, float along, float bottomY, Transform unused, bool blocker, bool hung)
    {
        GameObject go = SpawnComposed(rel, Quaternion.LookRotation(inward));
        if (go == null) return null;
        Bounds b = AlchemistPack.RenderBounds(go);
        Vector3 shift = Vector3.zero;
        if (Mathf.Abs(inward.x) > 0.5f)
        {
            float w = _root.position.x + plane;
            shift.x = inward.x > 0f ? w - b.min.x : w - b.max.x;
            shift.z = _root.position.z + along - b.center.z;
        }
        else
        {
            float w = _root.position.z + plane;
            shift.z = inward.z > 0f ? w - b.min.z : w - b.max.z;
            shift.x = _root.position.x + along - b.center.x;
        }
        shift.y = _root.position.y + bottomY - b.min.y;
        go.transform.position += shift;
        if (blocker) AddBlocker(go, go.name + "Blocker");
        return go;
    }

    // ---------------------------------------------------------------- decals

    private static GameObject PlaceDecal(string prefabName, Vector3 localPos, Vector3 normal, float size, float roll = 0f, float aspect = 1f)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{WardFolder}Prefabs/{prefabName}.prefab");
        if (prefab == null) { Debug.LogWarning($"Build Intake Room: decal '{prefabName}' not found."); return null; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _decals);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        go.name = prefabName;
        Transform quad = go.transform.childCount > 0 ? go.transform.GetChild(0) : go.transform;
        quad.localScale = new Vector3(size * aspect, size, 1f);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (normal == Vector3.up)
        {
            go.transform.localRotation = Quaternion.Euler(0f, roll, 0f);
            go.transform.localPosition = localPos + Vector3.up * 0.012f;
        }
        else
        {
            go.transform.localRotation = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, roll);
            go.transform.localPosition = localPos + normal * 0.015f;
        }
        return go;
    }

    // ---------------------------------------------------------------- signs

    private static TextMeshPro WallSign(string name, Vector3 pos, Vector3 inward, string text, Color color, float fontSize, float roll, bool flicker)
    {
        Quaternion rot = Quaternion.LookRotation(-inward) * Quaternion.Euler(0f, 0f, roll);
        TextMeshPro t = BuildSign(_root, name, pos + inward * 0.02f, text, color, fontSize, rot);
        if (flicker) _flickerSigns.Add(t);
        return t;
    }

    // ---------------------------------------------------------------- particles (D8)

    private static void BuildParticles()
    {
        Transform group = Group(_root, "Particles");
        GameObject dust = AssetDatabase.LoadAssetAtPath<GameObject>(WardFolder + "Prefabs/Ward_Dust.prefab");
        Material dustMat = null;
        if (dust != null)
        {
            ParticleSystemRenderer pr = dust.GetComponent<ParticleSystemRenderer>();
            if (pr != null) dustMat = pr.sharedMaterial;
            // room, centre (x,z), size (x,z)
            (string, Vector2, Vector2)[] rooms =
            {
                ("DustA", new Vector2(0f, 0f), new Vector2(7f, 7f)),
                ("DustB", new Vector2(0f, 7.85f), new Vector2(4f, 8.3f)),
                ("DustC", new Vector2(0f, 15.2f), new Vector2(6f, 6f)),
            };
            foreach ((string, Vector2, Vector2) r in rooms)
            {
                GameObject d = (GameObject)PrefabUtility.InstantiatePrefab(dust, group);
                PrefabUtility.UnpackPrefabInstance(d, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                d.name = r.Item1;
                foreach (MonoBehaviour mb in d.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb); // DustMotes would re-parent it onto the camera
                d.transform.localPosition = new Vector3(r.Item2.x, 1.5f, r.Item2.y);
                ParticleSystem ps = d.GetComponent<ParticleSystem>();
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(r.Item3.x, 2.4f, r.Item3.y);
                var em = ps.emission;
                float baseVol = 7f * 3f * 7f;
                em.rateOverTime = 25f * (r.Item3.x * 2.4f * r.Item3.y) / baseVol;
                var main = ps.main;
                main.prewarm = true;
            }
        }

        // One drip in corridor E: from a stain on the east wall near the ceiling to a damp patch on the floor.
        GameObject drip = new GameObject("Drip");
        drip.transform.SetParent(group, false);
        drip.transform.localPosition = new Vector3(4.2f, 2.75f, 7.4f);
        ParticleSystem dps = drip.GetComponent<ParticleSystem>();
        if (dps == null) dps = drip.AddComponent<ParticleSystem>();
        dps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var dm = dps.main;
        dm.loop = true;
        dm.duration = 5f;
        dm.startLifetime = 0.8f;
        dm.startSpeed = 0f;
        dm.startSize = 0.025f;
        dm.startColor = new Color(0.7f, 0.78f, 0.8f, 0.8f);
        dm.gravityModifier = 1f;
        dm.maxParticles = 8;
        dm.simulationSpace = ParticleSystemSimulationSpace.World;
        var de = dps.emission;
        de.rateOverTime = 0.8f;
        var ds = dps.shape;
        ds.shapeType = ParticleSystemShapeType.Box;
        ds.scale = new Vector3(0.04f, 0.01f, 0.04f);
        ParticleSystemRenderer dr = drip.GetComponent<ParticleSystemRenderer>();
        dr.renderMode = ParticleSystemRenderMode.Stretch;
        dr.lengthScale = 3f;
        if (dustMat != null) dr.sharedMaterial = dustMat;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dps.Play();
    }

    // ---------------------------------------------------------------- the dressing list

    private static void BuildDressing(GameObject rootGo)
    {
        BuildPillars();

        // ---------------- room A: the admissions bay
        PlaceOnWall("Alch_Cupboard2", Vector3.forward, -3.5f, -1.0f, 0f, null, true, false);
        PlaceOnWall("PackDressing/Pack_Socket", Vector3.forward, -3.5f, -3.0f, 0.3f, null, false, true);
        PlaceOnWall("PackDressing/Pack_AirVent", Vector3.forward, -3.5f, 0.3f, 2.25f, null, false, true);
        PlaceOnWall("Alch_Drawer1", Vector3.back, 3.5f, -2.1f, 0f, null, true, false);

        PlaceOnTable("Alch_BookPileA", _table1, _table1Max, 2.95f, -2.35f); // clear of the bottle at z -1.9
        PlaceOnTable("Alch_Flasks", _table2, _table2Max, 2.9f, 1.55f);

        // floor and wall dirt
        PlaceDecal("Ward_FloorGrime", new Vector3(0.4f, 0f, -2.3f), Vector3.up, 1.6f, 20f);
        PlaceDecal("Ward_FloorGrime", new Vector3(-0.6f, 0f, 2.1f), Vector3.up, 1.8f, 140f);
        PlaceDecal("Ward_FloorGrime", new Vector3(1.9f, 0f, 3.0f), Vector3.up, 1.3f, 75f);
        PlaceDecal("Ward_BloodSmear", new Vector3(-1.1f, 0f, -3.0f), Vector3.up, 1.3f, 100f);
        PlaceDecal("Ward_DragMarks", new Vector3(-1.4f, 0f, 2.0f), Vector3.up, 2.6f, -55f);
        PlaceDecal("Ward_Grime", new Vector3(-3.5f, 1.2f, -2.2f), Vector3.right, 1.4f);
        PlaceDecal("Ward_Grime", new Vector3(-3.5f, 1.2f, 0.6f), Vector3.right, 1.4f);
        PlaceDecal("Ward_Grime", new Vector3(-0.2f, 1.0f, -3.5f), Vector3.forward, 1.6f, 15f);
        PlaceDecal("Ward_Grime", new Vector3(3.5f, 2.0f, 2.9f), Vector3.left, 1.3f, -20f);
        PlaceDecal("Ward_Drip", new Vector3(0.8f, 2.4f, -3.5f), Vector3.forward, 0.6f);
        PlaceDecal("Ward_Handprint", new Vector3(1.9f, 1.15f, 3.5f), Vector3.back, 0.5f, 15f);

        WallSign("StayInBedSign", new Vector3(-3.5f, 2.15f, -0.8f), Vector3.right, "STAY IN YOUR BED", new Color(0.72f, 0.2f, 0.16f), 1.15f, 4f, false);
        WallSign("WardSign", new Vector3(-0.6f, 2.15f, -3.5f), Vector3.forward, "INTAKE  ·  WARD 1", new Color(0.78f, 0.76f, 0.66f), 1.5f, 0f, true);

        // ---------------- room B: the holding room
        GameObject bedB = PlaceOnWall("PackDressing/Pack_MorgueBed", Vector3.left, 2.0f, 5.5f, 0f, null, true, false);
        // Over the second morgue bed: the north part of this wall (z 10.76..12) is too short for the 1.8 m shelf,
        // which ran into the side-opening pillar.
        SeatShelfItems(PlaceOnWall("Alch_SpecimenShelf", Vector3.left, 2.0f, 5.5f, 1.15f, null, false, true));
        PlaceOnWall("PackDressing/Pack_FuseBox", Vector3.right, -2.0f, 7.2f, 1.5f, null, false, true);

        PlaceDecal("Ward_ClawMarks", new Vector3(-2.0f, 1.45f, 9.8f), Vector3.right, 0.9f);
        PlaceDecal("Ward_BloodPool", new Vector3(0.6f, 0f, 10.8f), Vector3.up, 1.0f, 30f);
        PlaceDecal("Ward_Grime", new Vector3(-2.0f, 1.8f, 6.4f), Vector3.right, 1.3f);
        PlaceDecal("Ward_Grime", new Vector3(-1.9f, 1.6f, 12.0f), Vector3.back, 1.2f);
        WallSign("HoldingSign", new Vector3(0f, 2.7f, 3.7f), Vector3.forward, "HOLDING", new Color(0.78f, 0.76f, 0.66f), 1.6f, 0f, false);

        // ---------------- corridor E: decals and particles only
        PlaceDecal("Ward_Handprint", new Vector3(4.4f, 1.35f, 6.4f), Vector3.left, 0.45f, 10f);
        PlaceDecal("Ward_Handprint", new Vector3(4.4f, 1.25f, 8.4f), Vector3.left, 0.5f, -10f);
        PlaceDecal("Ward_Handprint", new Vector3(4.4f, 1.4f, 10.3f), Vector3.left, 0.5f, 25f);
        PlaceDecal("Ward_Drip", new Vector3(4.4f, 2.5f, 7.4f), Vector3.left, 0.6f);
        PlaceDecal("Ward_FloorGrime", new Vector3(4.2f, 0f, 7.4f), Vector3.up, 0.8f, 0f);
        PlaceDecal("Ward_BloodSmear", new Vector3(3.9f, 0f, 10.8f), Vector3.up, 1.2f, 80f);
        WallSign("NoEntrySign", new Vector3(3.3f, 1.9f, 11.5f), Vector3.back, "NO ENTRY", new Color(0.85f, 0.12f, 0.08f), 1.9f, 0f, true);

        // ---------------- room C: the star room
        PlaceOnWall("Alch_Painting1", Vector3.right, -3.0f, 16.4f, 0.8f, null, false, true);
        PlaceOnWall("Alch_Painting3", Vector3.left, 3.0f, 16.4f, 0.65f, null, false, true);
        SeatShelfItems(PlaceOnWall("Alch_WoodShelf", Vector3.right, -3.0f, 13.6f, 1.1f, null, false, true));
        PlaceOnWall("Alch_Cupboard3", Vector3.left, 3.0f, 13.5f, 0f, null, true, false);
        PlaceOnWall("Alch_TrunkStack", Vector3.left, 3.0f, 17.2f, 0f, null, true, false);
        PlaceOnWall("Alch_CrateStack", Vector3.right, -3.0f, 17.0f, 0f, null, true, false);
        GameObject candles = PlaceProp("Alchemist/Alch_Candles", new Vector3(-2.6f, 0f, 13.0f), 0f);
        if (candles != null) AddBlocker(candles, "CandlesBlocker");

        PlaceDecal("Ward_FloorGrime", new Vector3(1.0f, 0f, 14.2f), Vector3.up, 1.5f, 30f);
        PlaceDecal("Ward_FloorGrime", new Vector3(-1.1f, 0f, 15.4f), Vector3.up, 1.5f, 200f);
        PlaceDecal("Ward_DragMarks", new Vector3(0f, 0f, 13.2f), Vector3.up, 2.4f, 95f);

        BuildParticles();

        // ---------------- sound points: corridor E and beyond C's walls
        Transform sp = Group(_root, "SoundPoints");
        foreach (Vector3 p in new[] { new Vector3(3.3f, 1.5f, 10.8f), new Vector3(-3.6f, 1.5f, 15f), new Vector3(3.6f, 1.5f, 16f), new Vector3(0f, 1.5f, 18.9f), new Vector3(3.3f, 2.4f, 6f) })
        {
            _soundPoints.Add(Point(sp, "Sound", p, 0f));
        }

        if (bedB == null) Debug.Log("Build Intake Room: the second holding bed did not place, room B keeps its floor free.");
    }

    /// <summary>Moves go vertically so its lowest rendered point sits at room-local y.</summary>
    private static void SeatAt(GameObject go, float localY)
    {
        if (go == null) return;
        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += Vector3.up * (_root.position.y + localY - b.min.y);
    }

    /// <summary>
    /// Stands every item of a composed shelf prop on its plank: the composed prefabs sink some items a few cm into it
    /// (the specimen jars by 3-5 cm). Each direct child other than the plank is one item and moves as a whole.
    /// </summary>
    private static void SeatShelfItems(GameObject shelfProp)
    {
        if (shelfProp == null) return;
        Renderer plank = null;
        foreach (Renderer r in shelfProp.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("WoodShelf")) { plank = r; break; }
        }
        if (plank == null) return;
        float top = plank.bounds.max.y;
        foreach (Transform item in shelfProp.transform)
        {
            if (plank.transform == item || plank.transform.IsChildOf(item)) continue;
            if (item.GetComponentInChildren<Renderer>() == null) continue;
            float dy = top - AlchemistPack.RenderBounds(item.gameObject).min.y;
            if (Mathf.Abs(dy) < 0.15f) item.position += Vector3.up * dy;
        }
    }

    /// <summary>A small prop on a table top, centred at (x, z) in room space.</summary>
    private static void PlaceOnTable(string rel, Vector3 tableMin, Vector3 tableMax, float x, float z)
    {
        if (tableMax == Vector3.zero) return;
        GameObject go = SpawnComposed(rel, Quaternion.identity);
        if (go == null) return;
        Bounds b = AlchemistPack.RenderBounds(go);
        Vector3 target = _root.TransformPoint(new Vector3(x, tableMax.y, z));
        go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
    }

    // ---------------------------------------------------------------- ambience wiring (D5)

    private static void WireAmbience(GameObject rootGo, Light corridorLamp, Renderer corridorGlass, Light lampC, Renderer lampGlow, Transform hanging)
    {
        IntakeAmbience amb = rootGo.AddComponent<IntakeAmbience>();
        SerializedObject so = new SerializedObject(amb);
        SetArray(so, "swayers", new List<Object> { hanging });
        SetArray(so, "buzzLights", new List<Object> { lampC, corridorLamp });
        SetArray(so, "buzzRenderers", new List<Object> { lampGlow, corridorGlass });
        SetArray(so, "soundPoints", new List<Object>(_soundPoints.ConvertAll(t => (Object)t)));
        SetArray(so, "flickerSigns", new List<Object>(_flickerSigns.ConvertAll(t => (Object)t)));
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(SerializedObject so, string field, List<Object> values)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogError($"Build Intake Room: IntakeAmbience has no field '{field}'."); return; }
        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    // ---------------------------------------------------------------- audit: clear paths

    private static string AuditPaths(Transform root)
    {
        StringBuilder sb = new StringBuilder();
        Physics.SyncTransforms();
        List<Collider> off = new List<Collider>();
        foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
        {
            if (c.enabled && !c.isTrigger && c.transform.parent != null && c.transform.parent.name == "Shutter") { c.enabled = false; off.Add(c); }
        }
        Physics.SyncTransforms();

        Vector3[] path =
        {
            new Vector3(-1.45f, 0f, -2.2f), new Vector3(1.0f, 0f, -0.6f), new Vector3(1.3f, 0f, -1.9f), new Vector3(1.3f, 0f, -0.35f),
            new Vector3(2.8f, 0f, -0.35f), new Vector3(1.3f, 0f, -0.35f), new Vector3(1.3f, 0f, 1.2f), new Vector3(1.3f, 0f, 2.4f),
            new Vector3(0f, 0f, 3.0f), new Vector3(0f, 0f, 4.4f), new Vector3(-0.8f, 0f, 7.0f), new Vector3(-0.9f, 0f, 9.0f),
            new Vector3(0f, 0f, 10.6f), new Vector3(0f, 0f, 12.6f), new Vector3(0f, 0f, 14.8f),
        };
        int blocked = 0;
        List<string> names = new List<string>();
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector3 a = path[i], b = path[i + 1];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.25f));
            for (int s = 0; s <= steps; s++)
            {
                Vector3 p = root.TransformPoint(Vector3.Lerp(a, b, s / (float)steps));
                Vector3 lo = p + Vector3.up * 0.5f, hi = p + Vector3.up * 1.35f;
                if (!Physics.CheckCapsule(lo, hi, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                blocked++;
                foreach (Collider c in Physics.OverlapCapsule(lo, hi, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                {
                    string n = c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name;
                    if (!names.Contains(n)) names.Add(n);
                }
                if (names.Count < 8 && blocked < 40) sb.Append($" {Vector3.Lerp(a, b, s / (float)steps)}");
            }
        }
        foreach (Collider c in off) c.enabled = true;
        Physics.SyncTransforms();

        // corridor E must stay collider-free apart from its own walls
        Collider[] inE = Physics.OverlapBox(root.TransformPoint(new Vector3(3.3f, 1.5f, 8.25f)), new Vector3(0.85f, 1.4f, 2.9f), root.rotation, ~0, QueryTriggerInteraction.Ignore);
        List<string> eNames = new List<string>();
        foreach (Collider c in inE) eNames.Add(c.name);

        // nothing may poke through the ceiling or a wall
        List<string> outside = new List<string>();
        Transform props = root.Find("Props"); // may be gone or renamed after a hand edit of the prefab
        if (props != null) foreach (Transform child in props)
        {
            Bounds pb = AlchemistPack.RenderBounds(child.gameObject);
            if (pb.max.y > root.position.y + Height + 0.01f || pb.min.y < root.position.y - 0.01f) outside.Add($"{child.name} y {pb.min.y - root.position.y:F2}..{pb.max.y - root.position.y:F2}");
        }
        string result = $"  props inside shell: {(outside.Count == 0 ? "OK" : string.Join("; ", outside))}\n";
        result += $"  clear paths: {(blocked == 0 ? "OK" : "BLOCKED x" + blocked + " by " + string.Join(", ", names) + " at" + sb)}\n";
        result += $"  corridor E colliders: {(eNames.Count == 0 ? "none OK" : string.Join(", ", eNames))}\n";
        return result;
    }
}
