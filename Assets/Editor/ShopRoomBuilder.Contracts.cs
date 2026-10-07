using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F87: the contract board on the shop's south wall, left of the door. A full Build > Shop Room builds it with the room;
/// <see cref="AddContractBoard"/> adds it to an existing ShopRoom without rebuilding anything (the ProjectSetup piece
/// "Shop contract board" runs it from Build Missing Pieces). Idempotent: a room that already has one is left alone.
/// </summary>
public static partial class ShopRoomBuilder
{
    // The free stretch of the south wall: between the door (x +-0.7) and the table/stool in the west corner (x < -2.3).
    private const float BoardAlong = -3.0f;
    private const float BoardHeight = 1.7f;

    /// <summary>Non-null when the open scene has a ShopRoom without a contract board (used by ProjectSetup).</summary>
    public static string ContractBoardProblem()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return null; // reported by "Shop room"
        return shop.HasContractBoard ? null : "the shop has no contract board (F87: the salesman's side jobs)";
    }

    /// <summary>Dialog-free migration: adds the board and wires ShopRoom.contractZone. Returns false if it was already there.</summary>
    public static bool AddContractBoard()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null || shop.HasContractBoard) return false;

        _room = shop.transform;
        UsedPrefabs.Clear();
        Collider zone = BuildContractBoard(shop.transform);

        SerializedObject so = new SerializedObject(shop);
        so.FindProperty("contractZone").objectReferenceValue = zone;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Contract board added to the ShopRoom. Save the scene (Ctrl+S) to keep it.", shop);
        return true;
    }

    /// <summary>The board, a few pinned notices, its sign and the trigger zone in front of it. Returns the zone.</summary>
    private static Collider BuildContractBoard(Transform root)
    {
        Transform g = Group(root, "ContractBoard");

        // The pack's own notice board (the same piece the lab wall has), hung on the south wall.
        GameObject board = PlaceOnWall(g, "Furniture/Board", Wall.South, BoardAlong, BoardHeight);
        Bounds b = board != null ? RenderBounds(board) : new Bounds(root.TransformPoint(new Vector3(BoardAlong, BoardHeight, -5.95f)), new Vector3(0.9f, 1.1f, 0.05f));

        // Pinned notices: thin cream cards (a Quad would only render from one side).
        Material paper = LoadOrCreate("Shop_Paper", new Color(0.78f, 0.72f, 0.58f), null);
        Vector3 front = b.center + new Vector3(0f, 0f, b.extents.z + 0.015f);
        float[] dx = { -0.22f, 0.05f, 0.25f };
        float[] dy = { 0.16f, -0.12f, 0.2f };
        float[] roll = { 6f, -9f, 4f };
        for (int i = 0; i < 3; i++)
        {
            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = "Notice" + i;
            Object.DestroyImmediate(card.GetComponent<Collider>());
            card.transform.SetParent(g, false);
            card.transform.position = front + new Vector3(dx[i], dy[i], 0f);
            card.transform.rotation = Quaternion.Euler(0f, 0f, roll[i]);
            card.transform.localScale = new Vector3(0.2f, 0.27f, 0.01f);
            card.GetComponent<MeshRenderer>().sharedMaterial = paper;
        }

        BuildSign(root, "ContractSign", new Vector3(BoardAlong, 2.5f, -5.92f), Quaternion.Euler(0f, 180f, 0f), "CONTRACTS", Amber, 6f, 2.6f);

        Transform points = root.Find("Points");
        if (points == null) points = Group(root, "Points");
        // x -2.3..-1.35, z -5.3..-3.8: inside the stool/door gap, and clear of the door zone (x > -1.3) so E at the door still descends.
        return BuildZone(points, "ContractZone", new Vector3(-3.0f, 1.25f, -4.2f), new Vector3(1.4f, 2.5f, 1.2f));
    }
}
