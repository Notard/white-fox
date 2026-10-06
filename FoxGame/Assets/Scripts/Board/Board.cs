using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MapData 로 타일·블록·소품을 배치하고, 칸 좌표 ↔ 월드 좌표 변환과 판 상태(무너진 칸, 문 열림)를 맡는다.
/// 보드 중심이 이 오브젝트 위치이며, 1층 타일 윗면 높이는 y = 0. 한 층 올라갈 때마다 FloorHeight 만큼 높아진다.
/// 2층 이상 칸은 흙 블록을 쌓고 맨 위에 타일(잔디·얼음·금 간 잔디)을 얹는다.
/// 장치: 레버(당기면 기울어짐), 누름판(눌리면 내려감), 미는 블록(밀리고, 구멍에 떨어져 메움),
/// 솟는 다리(켜지면 아래에서 솟아오름), 승강 땅(오르내림). 모습은 Update 에서 판 상태를 따라간다.
/// </summary>
public class Board : MonoBehaviour
{
    public float cellSize = 1.5f;
    [Tooltip("타일 사이 틈 (칸 크기 대비 비율)")]
    public float gap = 0.03f;
    public GameObject tilePrefab;
    public GameObject iceTilePrefab;
    public GameObject crumbleTilePrefab;
    public GameObject blockPrefab;
    public GameObject obstaclePrefab;
    public GameObject switchPrefab;
    public GameObject doorPrefab;
    public GameObject stairsPrefab;
    [Header("장치")]
    public GameObject leverPrefab;
    public GameObject platePrefab;
    public GameObject pushBlockPrefab;
    [Tooltip("채널 표시 고리 (다리·승강 땅 위)")]
    public Material runeMaterial;
    [Tooltip("블록이 한 칸 밀리는 시간 (여우 걸음과 맞춤)")]
    public float pushDuration = 0.32f;
    [Tooltip("승강 땅이 오르내리는 속도 (층/초)")]
    public float liftFloorsPerSecond = 2.2f;
    [Header("섬 지형 (있으면 타일 대신)")]
    public Material terrainMaterial;
    [Tooltip("칸 가장자리에 흩어 놓을 장식 (풀숲, 꽃, 자갈)")]
    public GameObject[] decorPrefabs;

    public bool UsesTerrain => terrainMaterial != null;

    /// <summary>섬 밑부분 높이 (카메라 맞춤용).</summary>
    public float BottomY => UsesTerrain ? -(TerrainBuilder.BaseDepth + TerrainBuilder.UnderDepth * 0.7f) : -0.75f;

    /// <summary>한 층의 높이 = 타일(블록) 모델 높이 0.5 × 배치 크기.</summary>
    public float FloorHeight => 0.5f * cellSize * (1 - gap);

    /// <summary>가장 높은 칸의 윗면 높이 (카메라 맞춤용).</summary>
    public float TopY => (Map.HighestFloor - 1) * FloorHeight;

    public MapData Map { get; private set; }
    /// <summary>판 상태: 무너진 칸, 문 열림. Build 할 때 처음 상태로.</summary>
    public PuzzleState State { get; private set; } = new();
    /// <summary>여우가 서 있는 칸 (누름판 눌림 연출용). 떨어지면 null.</summary>
    public Vector2Int? FoxCell { get; private set; }

    /// <summary>채널 색: 1 주황, 2 파랑, 3 초록, 4 노랑.</summary>
    public static Color ChannelColor(int channel) => channel switch
    {
        1 => new Color(1f, 0.55f, 0.2f),
        2 => new Color(0.25f, 0.58f, 1f),
        3 => new Color(0.25f, 0.82f, 0.5f),
        4 => new Color(1f, 0.85f, 0.2f),
        _ => Color.white,
    };

    /// <summary>승강 땅이 오르내리는 속도 (월드 단위/초). 여우도 같은 속도로 따라간다.</summary>
    public float LiftSpeed => liftFloorsPerSecond * FloorHeight;

    public int Width => Map.Width;
    public int Height => Map.Height;

    /// <summary>보드가 차지하는 가로·세로 크기 (월드 단위).</summary>
    public Vector2 Size => new Vector2(Width, Height) * cellSize;

    public CellType GetCell(Vector2Int c) => Map.TypeAt(c);

    public bool IsInside(Vector2Int c) => Map.IsInside(c);

    // 칸마다 만든 오브젝트 (무너질 때 함께 떨어뜨림)
    readonly Dictionary<Vector2Int, List<GameObject>> columns = new();
    readonly List<Transform> doors = new();
    readonly List<Transform> switches = new();

    // 장치 모습
    class LeverView { public Vector2Int cell; public Transform handle; public Quaternion rest; public Vector3 axis; public float angle; }
    class PlateView { public Vector2Int cell; public Transform top; public Vector3 rest; public float depth; }
    class BlockView { public Transform t; public bool busy; }
    class BridgeView { public Vector2Int cell; public Transform column; public Vector3 rest; public Renderer[] renderers; public GameObject ghost; public float offset; }
    class LiftView { public Vector2Int cell; public Transform column; public float fullHeight; public float height; }
    readonly List<LeverView> levers = new();
    readonly List<PlateView> plates = new();
    readonly Dictionary<Vector2Int, BlockView> blockViews = new();
    readonly List<BlockView> filledViews = new();
    readonly List<BridgeView> bridges = new();
    readonly List<LiftView> lifts = new();
    const float LeverAngle = 32f;

    /// <summary>칸 윗면 가운데의 월드 좌표 (판 상태 반영: 승강 땅·메운 구멍). 맵 밖이나 구멍은 1층 높이로 본다.</summary>
    public Vector3 CellToWorld(Vector2Int c)
    {
        // 구멍은 바닥 높이가 없지만, 발판이 다니는 구멍은 그 칸 층 높이에 발판이 뜬다
        bool standable = Map.IsInside(c) && (Map.TypeAt(c) != CellType.Hole || OnPlatformPath(c) || State.filled.ContainsKey(c));
        int floor = standable ? MoveRules.Floor(Map, State, c) : MapData.MinFloor;
        return transform.position + new Vector3(
            (c.x - (Width - 1) / 2f) * cellSize,
            (floor - 1) * FloorHeight,
            (c.y - (Height - 1) / 2f) * cellSize);
    }

    bool OnPlatformPath(Vector2Int c)
    {
        if (Map.movers == null) return false;
        foreach (var m in Map.movers)
            if (m.IsPlatform && m.path.Contains(c)) return true;
        return false;
    }

    public void Build(MapData map)
    {
        Map = map;
        State = PuzzleState.Initial(map, map.StartCell);
        FoxCell = map.StartCell;
        columns.Clear();
        doors.Clear();
        switches.Clear();
        levers.Clear();
        plates.Clear();
        blockViews.Clear();
        filledViews.Clear();
        bridges.Clear();
        lifts.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        TerrainBuilder.Result terrain = null;
        if (UsesTerrain)
        {
            terrain = TerrainBuilder.Build(map, cellSize, FloorHeight);
            MakeMeshObject("Terrain", terrain.main, Vector3.zero);
        }

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = new Vector2Int(x, y);
            var type = GetCell(c);
            if (type == CellType.Hole) continue;
            var column = columns[c] = new List<GameObject>();

            // 칸마다 무늬 방향을 바꿔 반복이 덜 보이게 (항상 같은 결과가 나오도록 좌표로 결정)
            int turn = (x * 7 + y * 13) % 4;
            var top = CellToWorld(c);
            if (terrain != null)
            {
                if (terrain.chunks.TryGetValue(c, out var chunk))
                {
                    var go = MakeMeshObject($"Chunk_{x}_{y}", chunk, terrain.chunkCenters[c]);
                    column.Add(go);
                    if (type == CellType.Bridge) AddBridge(c, go);
                    if (type == CellType.Lift) AddLift(c, go, terrain.liftHeights[c]);
                }
                if (type != CellType.Bridge && type != CellType.Lift) ScatterDecor(c, type);
                SpawnProps(c, type, turn);
                continue;
            }
            var tilePf = type switch
            {
                CellType.Ice when iceTilePrefab != null => iceTilePrefab,
                CellType.Crumble when crumbleTilePrefab != null => crumbleTilePrefab,
                _ => tilePrefab,
            };
            var tile = Spawn(tilePf, top, Quaternion.Euler(0, 90 * turn, 0), $"Tile_{x}_{y}");
            tile.transform.localScale = Vector3.one * cellSize * (1 - gap);
            column.Add(tile);

            // 아래층을 흙 블록으로 채움
            for (int f = 1; f < Map.FloorAt(c); f++)
            {
                var block = Spawn(blockPrefab, top - Vector3.up * f * FloorHeight, Quaternion.Euler(0, 90 * ((turn + f) % 4), 0), $"Block_{x}_{y}_{Map.FloorAt(c) - f}");
                block.transform.localScale = Vector3.one * cellSize * (1 - gap);
                column.Add(block);
            }
            SpawnProps(c, type, turn);
        }
        foreach (var b in State.blocks) SpawnBlock(b);
        UpdateViews(snap: true);
    }

    void SpawnProps(Vector2Int c, CellType type, int turn)
    {
            switch (type)
            {
                case CellType.Obstacle:
                    Prop(obstaclePrefab, c, Quaternion.Euler(0, 40 + 75 * turn, 0), "Obstacle");
                    break;
                case CellType.Switch:
                    var sw = Prop(switchPrefab, c, Quaternion.identity, "Switch");
                    if (sw != null) switches.Add(sw.transform);
                    break;
                case CellType.Door:
                    var door = Prop(doorPrefab, c, Quaternion.Euler(0, DoorYaw(c), 0), "Door");
                    if (door != null) doors.Add(door.transform);
                    break;
                case CellType.Stairs:
                    Prop(stairsPrefab, c, Quaternion.Euler(0, StairsYaw(c), 0), "Stairs");
                    break;
                case CellType.Lever:
                    AddLever(c);
                    break;
                case CellType.Plate:
                    AddPlate(c);
                    break;
            }
    }

    GameObject MakeMeshObject(string name, Mesh mesh, Vector3 localPos)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = terrainMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        // 체커 무늬 기준점: 보드의 (x 최소, z 최소) 모서리
        var block = new MaterialPropertyBlock();
        block.SetVector("_GridOrigin", transform.position - new Vector3(Width * cellSize / 2f, 0, Height * cellSize / 2f));
        block.SetFloat("_CellSize", cellSize);
        mr.SetPropertyBlock(block);
        return go;
    }

    /// <summary>칸 가장자리 근처에 풀숲·꽃·자갈을 흩어 놓는다 (가운데는 여우 자리라 비움). 칸 좌표로 결정되어 늘 같은 모습.</summary>
    void ScatterDecor(Vector2Int c, CellType type)
    {
        if (decorPrefabs == null || decorPrefabs.Length == 0) return;
        if (type == CellType.Ice || type == CellType.Door || type == CellType.Stairs) return;
        var rng = new System.Random(c.x * 73856093 ^ c.y * 19349663 ^ Map.Width * 83492791);
        int count = type == CellType.Ground ? rng.Next(2, 6) : rng.Next(0, 2);
        for (int k = 0; k < count; k++)
        {
            // 가운데(반경 0.3칸)는 피하고 가장자리 안쪽(0.12~0.88)에만
            float u, v;
            int guard = 0;
            do
            {
                u = 0.12f + (float)rng.NextDouble() * 0.76f;
                v = 0.12f + (float)rng.NextDouble() * 0.76f;
            } while (Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) < 0.3f && ++guard < 20);
            // 무너지는 칸·장애물 칸에는 자갈만 (마지막 장식)
            int pick = type == CellType.Ground ? rng.Next(decorPrefabs.Length) : decorPrefabs.Length - 1;
            var center = CellToWorld(c);
            var pos = new Vector3(center.x + (u - 0.5f) * cellSize,
                transform.position.y + TerrainBuilder.SurfaceY(Map, cellSize, FloorHeight, c, u, v),
                center.z + (v - 0.5f) * cellSize);
            var go = Spawn(decorPrefabs[pick], pos, Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), $"Decor_{c.x}_{c.y}_{k}");
            go.transform.localScale = Vector3.one * cellSize * (1.3f + (float)rng.NextDouble() * 0.8f);
            columns[c].Add(go);
        }
    }

    GameObject Prop(GameObject prefab, Vector2Int c, Quaternion rot, string name)
    {
        if (prefab == null) return null;
        var go = Spawn(prefab, CellToWorld(c), rot, $"{name}_{c.x}_{c.y}");
        go.transform.localScale = Vector3.one * cellSize;
        columns[c].Add(go);
        return go;
    }

    /// <summary>문은 양옆이 막힌 방향으로 선다 (지나가는 길을 가로막는 모양).</summary>
    float DoorYaw(Vector2Int c)
    {
        bool open(Vector2Int d) => MapData.IsStandable(Map.TypeAt(c + d));
        return open(Vector2Int.left) || open(Vector2Int.right) ? 90 : 0;
    }

    /// <summary>계단은 옆 칸 중 가장 높은 쪽으로 올라가는 모양.</summary>
    float StairsYaw(Vector2Int c)
    {
        Vector2Int best = Vector2Int.up;
        int high = int.MinValue;
        foreach (var d in MoveRules.Directions)
        {
            if (!Map.IsInside(c + d) || Map.TypeAt(c + d) == CellType.Hole) continue;
            int f = Map.FloorAt(c + d);
            if (f > high) { high = f; best = d; }
        }
        return Quaternion.LookRotation(new Vector3(best.x, 0, best.y)).eulerAngles.y;
    }

    GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, string name)
    {
        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
        else
#endif
            go = Instantiate(prefab, transform);
        go.transform.SetPositionAndRotation(pos, rot);
        go.name = name;
        return go;
    }

    // ------------------------------------------------------------ 장치 만들기
    /// <summary>모델 안에서 이름에 key 가 들어간 Transform.</summary>
    static Transform FindPart(Transform root, string key)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains(key)) return t;
        return null;
    }

    /// <summary>renderer 의 재질 중 이름에 key 가 들어간 것만 채널 색으로 칠한다.</summary>
    static void Tint(Transform root, string key, Color color)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.Contains(key)) continue;
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block, i);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 0.22f);
                r.SetPropertyBlock(block, i);
            }
        }
    }

    void AddLever(Vector2Int c)
    {
        var go = Prop(leverPrefab, c, Quaternion.identity, "Lever");
        if (go == null) return;
        Tint(go.transform, "Knob", ChannelColor(Map.ChannelAt(c)));
        var handle = FindPart(go.transform, "Handle");
        if (handle == null) return;
        // 화면에서 잘 보이게 좌우(카메라 기준)로 기운다
        levers.Add(new LeverView { cell = c, handle = handle, rest = handle.rotation, axis = Vector3.forward, angle = -LeverAngle });
    }

    void AddPlate(Vector2Int c)
    {
        var go = Prop(platePrefab, c, Quaternion.identity, "Plate");
        if (go == null) return;
        Tint(go.transform, "PlateTop", ChannelColor(Map.ChannelAt(c)));
        var top = FindPart(go.transform, "Top");
        if (top != null) plates.Add(new PlateView { cell = c, top = top, rest = top.position });
    }

    void SpawnBlock(Vector2Int c)
    {
        if (pushBlockPrefab == null) return;
        var go = Spawn(pushBlockPrefab, CellToWorld(c), Quaternion.identity, $"PushBlock_{c.x}_{c.y}");
        go.transform.localScale = Vector3.one * cellSize;
        blockViews[c] = new BlockView { t = go.transform };
    }

    void AddBridge(Vector2Int c, GameObject column)
    {
        var color = ChannelColor(Map.ChannelAt(c));
        Rune(column.transform, CellToWorld(c), color, 1f);
        var view = new BridgeView
        {
            cell = c, column = column.transform, rest = column.transform.position,
            renderers = column.GetComponentsInChildren<Renderer>(),   // 고리까지 함께 숨긴다
        };
        // 꺼져 있을 때 자리에 남는 옅은 고리
        view.ghost = Rune(transform, CellToWorld(c), color, 0.45f);
        bridges.Add(view);
    }

    void AddLift(Vector2Int c, GameObject column, float fullHeight)
    {
        var top = CellToWorld(c);
        lifts.Add(new LiftView { cell = c, column = column.transform, fullHeight = fullHeight, height = fullHeight });
        // 고리는 기둥을 따라 오르내리도록 기둥의 자식으로 (세로 줄임의 영향을 받지 않게 Update 에서 높이를 맞춘다)
        Rune(column.transform, new Vector3(top.x, column.transform.position.y + fullHeight, top.z), ChannelColor(Map.ChannelAt(c)), 1f);
    }

    /// <summary>칸 윗면에 눕힌 채널 색 고리.</summary>
    GameObject Rune(Transform parent, Vector3 at, Color color, float alpha)
    {
        if (runeMaterial == null) return null;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        if (Application.isPlaying) Destroy(q.GetComponent<Collider>());
        else DestroyImmediate(q.GetComponent<Collider>());
        q.name = "Rune";
        q.transform.SetParent(parent, true);
        q.transform.SetPositionAndRotation(at + Vector3.up * 0.04f, Quaternion.Euler(90, 0, 0));
        SetWorldScale(q.transform, cellSize * 0.62f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = runeMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
        r.SetPropertyBlock(block);
        return q;
    }

    static void SetWorldScale(Transform t, float size)
    {
        var parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(size / parentScale.x, size / parentScale.y, size / parentScale.z);
    }

    // ------------------------------------------------------------ 장치 연출 (판 상태를 따라감)
    void Update()
    {
        if (Map != null && Application.isPlaying) UpdateViews(snap: false);
    }

    /// <summary>레버 기울기, 누름판 눌림, 다리 솟음, 승강 땅 높이, 블록 높이를 판 상태 쪽으로 옮긴다. snap 이면 바로.</summary>
    void UpdateViews(bool snap)
    {
        float dt = snap ? 1000f : Time.deltaTime;
        foreach (var l in levers)
        {
            float target = State.leversOn.Contains(l.cell) ? LeverAngle : -LeverAngle;
            l.angle = Mathf.MoveTowards(l.angle, target, 260f * dt);
            l.handle.rotation = Quaternion.AngleAxis(l.angle, l.axis) * l.rest;
        }
        foreach (var p in plates)
        {
            bool pressed = State.blocks.Contains(p.cell) || FoxCell == p.cell;
            p.depth = Mathf.MoveTowards(p.depth, pressed ? 0.05f * cellSize : 0, 0.6f * dt);
            p.top.position = p.rest + Vector3.down * p.depth;
        }
        foreach (var b in bridges)
        {
            bool on = State.ChannelOn(Map.ChannelAt(b.cell));
            float drop = TerrainBuilder.BaseDepth + FloorHeight * Map.FloorAt(b.cell) + 1.5f;
            // 솟을 때는 빠르게 올라와 살짝 튀고, 꺼질 때는 가라앉는다
            b.offset = Mathf.MoveTowards(b.offset, on ? 0 : -drop, (on ? 9f : 6f) * dt);
            b.column.position = b.rest + Vector3.up * b.offset;
            bool visible = b.offset > -drop + 0.01f;
            foreach (var r in b.renderers) if (r != null) r.enabled = visible;
            if (b.ghost != null) b.ghost.SetActive(!on);
        }
        foreach (var l in lifts)
        {
            float target = (MoveRules.Floor(Map, State, l.cell) - 1) * FloorHeight + TerrainBuilder.BaseDepth;
            l.height = Mathf.MoveTowards(l.height, target, LiftSpeed * dt);
            l.column.localScale = new Vector3(1, l.height / l.fullHeight, 1);
            // 고리는 기둥 윗면에 붙어 있게 (기둥은 세로로 줄어든다)
            foreach (Transform child in l.column)
                if (child.name == "Rune")
                {
                    var pos = child.position;
                    child.position = new Vector3(pos.x, l.column.position.y + l.height + 0.04f, pos.z);
                }
        }
        // 멈춰 있는 블록은 칸 높이(승강 땅)를 따라간다
        foreach (var kv in blockViews)
        {
            if (kv.Value.busy) continue;
            var t = kv.Value.t;
            var target = CellToWorld(kv.Key);
            t.position = new Vector3(target.x, Mathf.MoveTowards(t.position.y, target.y, LiftSpeed * dt), target.z);
        }
    }

    float BlockHeight => 0.6f * cellSize;

    /// <summary>블록을 from → to 로 민다. fill 이면 끝에서 구멍으로 떨어져 윗면이 땅 높이에 맞게 박힌다.</summary>
    IEnumerator PushBlock(Vector2Int from, Vector2Int to, bool fill)
    {
        if (!blockViews.TryGetValue(from, out var view)) yield break;
        blockViews.Remove(from);
        if (!fill) blockViews[to] = view;
        else filledViews.Add(view);
        view.busy = true;
        var t = view.t;
        Vector3 a = t.position, b = CellToWorld(to);
        if (fill) b.y = a.y;   // 먼저 구멍 위로 미끄러진 뒤
        for (float k = 0; k < 1; k += Time.deltaTime / pushDuration)
        {
            var p = Vector3.Lerp(a, b, k);
            if (!fill && b.y < a.y) p.y = Mathf.Lerp(a.y, b.y, k * k);   // 낮은 땅으로는 끝에서 떨어짐
            t.position = p;
            yield return null;
        }
        t.position = b;
        if (fill) yield return DropInto(t, to);
        view.busy = false;
    }

    /// <summary>블록이 구멍으로 떨어져 윗면이 그 칸 높이에 맞게 박힌다 (쿵 하고 살짝 튐).</summary>
    IEnumerator DropInto(Transform t, Vector2Int c)
    {
        float floorY = CellToWorld(c).y - BlockHeight;
        float v = 0;
        while (t.position.y > floorY)
        {
            v += 22f * Time.deltaTime;
            t.position += Vector3.down * v * Time.deltaTime;
            yield return null;
        }
        t.position = new Vector3(t.position.x, floorY, t.position.z);
        var dust = FindAnyObjectByType<FoxTrail>();
        if (dust != null) dust.Puff(CellToWorld(c));
    }

    // ------------------------------------------------------------ 판 상태 연출
    /// <summary>이동 결과를 판 상태에 반영하고, 무너지는 칸은 바로 무너뜨린다. 문 열림은 여우가 도착한 뒤 ShowDoorsOpen 으로.</summary>
    public MoveRules.Changes ApplyMove(Vector2Int from, MoveResult r)
    {
        var ch = MoveRules.Apply(Map, State, from, r);
        FoxCell = r.outcome == MoveOutcome.Fall ? null : r.Moved ? r.cell : from;
        if (!Application.isPlaying) return ch;
        if (ch.crumbled is { } c) StartCoroutine(Collapse(c));
        if (r.pushFrom is { } pf && r.pushTo is { } pt) StartCoroutine(PushBlock(pf, pt, ch.filledHole));
        if (ch.blocksDropped != null)
            foreach (var b in ch.blocksDropped)
                if (blockViews.TryGetValue(b, out var view))
                {
                    blockViews.Remove(b);
                    filledViews.Add(view);
                    view.busy = true;
                    StartCoroutine(DropLater(view, b));
                }
        return ch;
    }

    IEnumerator DropLater(BlockView view, Vector2Int c)
    {
        yield return new WaitForSeconds(0.15f);   // 다리가 가라앉기 시작한 뒤
        yield return DropInto(view.t, c);
    }

    /// <summary>여우가 발판에 실려 가거나 떨어지는 등 ApplyMove 밖에서 칸이 바뀌었을 때.</summary>
    public void SetFoxCell(Vector2Int? cell) => FoxCell = cell;

    IEnumerator Collapse(Vector2Int c)
    {
        if (!columns.TryGetValue(c, out var parts)) yield break;
        var start = new List<Vector3>();
        foreach (var p in parts) start.Add(p.transform.position);
        // 흔들리다가
        for (float t = 0; t < 0.3f; t += Time.deltaTime)
        {
            for (int i = 0; i < parts.Count; i++)
                parts[i].transform.position = start[i] + new Vector3(Mathf.Sin(t * 80) * 0.04f, 0, Mathf.Cos(t * 70) * 0.04f);
            yield return null;
        }
        // 떨어진다
        float v = 0;
        for (float t = 0; t < 1.2f; t += Time.deltaTime)
        {
            v += 18f * Time.deltaTime;
            foreach (var p in parts)
            {
                p.transform.position += Vector3.down * v * Time.deltaTime;
                p.transform.Rotate(Vector3.right, 60 * Time.deltaTime);
            }
            yield return null;
        }
        foreach (var p in parts) Destroy(p);
        columns.Remove(c);
    }

    /// <summary>문이 땅속으로 내려가고 스위치가 눌린다.</summary>
    public void ShowDoorsOpen()
    {
        if (!Application.isPlaying) return;
        foreach (var d in doors) StartCoroutine(Sink(d, FloorHeight * 2.2f, 0.6f));
        foreach (var s in switches) StartCoroutine(Sink(s, 0.06f * cellSize, 0.15f));
    }

    static IEnumerator Sink(Transform t, float depth, float duration)
    {
        Vector3 from = t.position, to = from + Vector3.down * depth;
        for (float k = 0; k < 1; k += Time.deltaTime / duration)
        {
            t.position = Vector3.Lerp(from, to, k * k * (3 - 2 * k));
            yield return null;
        }
        t.position = to;
    }

    void OnDrawGizmos()
    {
        if (Map == null) return;
        Gizmos.color = new Color(0.4f, 0.9f, 1f);
        foreach (var g in Map.Gems)
            Gizmos.DrawWireSphere(CellToWorld(g) + Vector3.up * 0.4f, 0.2f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(CellToWorld(Map.StartCell) + Vector3.up * 0.05f, new Vector3(cellSize, 0.1f, cellSize) * 0.9f);
    }
}
