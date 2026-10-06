using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MapData 로 "떠 있는 섬" 지형 메시를 만든다. 칸 경계가 보이지 않게 하나로 이어진다.
///  - 윗면: 칸 가운데는 평평(여우가 서는 곳), 가장자리로 갈수록 살짝 울퉁불퉁. 낮은 쪽 가장자리는 둥글게 처진다.
///  - 절벽: 낮은 칸·구멍·맵 밖 쪽으로 바위 벽. 위·아래 줄과 모서리 기둥은 정확히 맞물리고 가운데만 불규칙하게 튀어나온다.
///  - 밑면: 섬 가장자리에서 멀수록 깊게 매달린 바위.
///  - 무너지는 칸은 떨어질 수 있게 따로 떼어 낸 덩어리(Chunk)로 만든다.
///  - 솟는 다리·승강 땅도 움직일 수 있게 떼어 낸 기둥으로 만든다. 승강 땅은 높은 쪽 층으로 만들고 바닥(-BaseDepth)을
///    기준점으로 두어, 세로로 줄여 낮은 층을 나타낸다.
/// 이웃 칸이 공유하는 정점의 높이·노멀은 "위치만으로" 결정해서 틈이나 조명 이음매가 생기지 않는다.
/// 정점 색: R 얼음, G 금 간 땅, A 가려짐(1 = 밝음). 좌표는 Board 로컬 공간.
/// </summary>
public static class TerrainBuilder
{
    public const int Sub = 4;                  // 칸 한 변을 나누는 수
    public const float BaseDepth = 1.15f;      // 1층 윗면(0)에서 섬 바닥까지
    public const float UnderDepth = 1.5f;      // 섬 밑면이 가운데로 갈수록 더 내려가는 깊이
    const float NoiseAmp = 0.035f;
    const float EdgeDrop = 0.13f;      // 낮은 쪽 가장자리가 둥글게 처지는 정도
    const float CliffBulge = 0.13f;    // 절벽 표면이 튀어나오는 정도
    const float Lip = 0.07f;           // 잔디가 절벽 위로 튀어나오는 처마
    const float CornerRound = 0.16f;   // 바깥 모서리를 깎는 정도
    const float BottomJag = 0.4f;      // 섬 아래 경계선이 들쭉날쭉한 정도

    public class Result
    {
        public Mesh main;
        public readonly Dictionary<Vector2Int, Mesh> chunks = new();   // 무너지는 칸·다리·승강 땅: 기준점 기준 로컬 메시
        public readonly Dictionary<Vector2Int, Vector3> chunkCenters = new();   // 기준점 (다리·무너지는 칸 = 윗면 가운데, 승강 땅 = 바닥 가운데)
        public readonly Dictionary<Vector2Int, float> liftHeights = new();      // 승강 땅: 기준점에서 윗면까지 높이 (높은 쪽 층 기준)
    }

    // ------------------------------------------------------------ 지도 조회
    static MapData map;
    static float cs, fh;

    static bool Solid(Vector2Int c)
    {
        if (!map.IsInside(c)) return false;
        var t = map.TypeAt(c);
        return t != CellType.Hole && t != CellType.Crumble && t != CellType.Bridge && t != CellType.Lift;
    }

    // 승강 땅 기둥을 만드는 동안만 그 칸 층을 바꿔 본다
    static Vector2Int? overrideCell;
    static Color chunkTint;   // 덩어리 윗면 색 채널 (g = 금 간 땅)
    static int overrideFloor;
    static float TopY(Vector2Int c) => ((overrideCell == c ? overrideFloor : map.FloorAt(c)) - 1) * fh;

    /// <summary>c 에서 보아 n 쪽이 "낮다"(구멍·무너지는 칸·맵 밖·더 낮은 층) → 가장자리를 둥글게, 절벽을 세운다.</summary>
    static bool Lower(Vector2Int c, Vector2Int n) => !Solid(n) || map.FloorAt(n) < map.FloorAt(c);

    static Vector3 Corner(Vector2Int c) =>   // 칸의 (x 최소, z 최소) 모서리 (로컬)
        new((c.x - map.Width / 2f) * cs, 0, (c.y - map.Height / 2f) * cs);

    public static Vector3 CellCenter(MapData m, float cellSize, float floorHeight, Vector2Int c) =>
        new((c.x - (m.Width - 1) / 2f) * cellSize, (m.FloorAt(c) - 1) * floorHeight, (c.y - (m.Height - 1) / 2f) * cellSize);

    static float Noise(float x, float z) => (Mathf.PerlinNoise(x * 1.25f + 11.3f, z * 1.25f + 7.1f) - 0.5f) * 2f * NoiseAmp;

    /// <summary>칸 안 위치(u,v ∈ 0..1)에서 울퉁불퉁함 비율: 가운데 0, 가장자리 근처 1.</summary>
    static float CenterMask(float u, float v)
    {
        float d = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f));
        return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.18f, 0.42f, d));
    }

    // ------------------------------------------------------------ 메시 쌓기
    class MeshData
    {
        public readonly List<Vector3> v = new();
        public readonly List<Vector3> n = new();
        public readonly List<Color> col = new();
        public readonly List<int> t = new();

        public int Add(Vector3 p, Vector3 normal, Color c)
        {
            v.Add(p); n.Add(normal); col.Add(c);
            return v.Count - 1;
        }

        public void Quad(int a, int b, int c, int d)   // a-b-c-d 반시계(위에서 볼 때)가 앞면
        {
            t.Add(a); t.Add(c); t.Add(b);
            t.Add(a); t.Add(d); t.Add(c);
        }

        /// <summary>
        /// 외곽선용 노멀: 같은 위치에 있는 정점들(윗면·절벽이 만나는 모서리 등)의 노멀을 평균 낸다.
        /// 셰이더 외곽선 패스가 이 방향으로 밀어 그려서, 모서리에서도 선이 끊기지 않는다. 탄젠트 채널에 담는다.
        /// </summary>
        List<Vector4> OutlineNormals()
        {
            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new(Mathf.RoundToInt(p.x * 500), Mathf.RoundToInt(p.y * 500), Mathf.RoundToInt(p.z * 500));
            for (int i = 0; i < v.Count; i++)
            {
                var k = Key(v[i]);
                sum[k] = (sum.TryGetValue(k, out var s) ? s : Vector3.zero) + n[i];
            }
            var result = new List<Vector4>(v.Count);
            for (int i = 0; i < v.Count; i++)
            {
                var a = sum[Key(v[i])].normalized;
                result.Add(new Vector4(a.x, a.y, a.z, 1));
            }
            return result;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetColors(col);
            m.SetTangents(OutlineNormals());
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // ------------------------------------------------------------ 만들기
    public static Result Build(MapData m, float cellSize, float floorHeight)
    {
        map = m; cs = cellSize; fh = floorHeight;
        var res = new Result();
        var main = new MeshData();
        var bottom = new BottomWelder();

        for (int y = 0; y < m.Height; y++)
        for (int x = 0; x < m.Width; x++)
        {
            var c = new Vector2Int(x, y);
            var type = m.TypeAt(c);
            if (type == CellType.Hole) continue;
            if (type == CellType.Crumble || type == CellType.Bridge)
            {
                chunkTint = type == CellType.Crumble ? new Color(0, 1, 0, 1) : new Color(0, 0, 0, 1);
                var center = CellCenter(m, cs, fh, c);
                res.chunks[c] = BuildChunk(c, center);
                res.chunkCenters[c] = center;
                continue;
            }
            if (type == CellType.Lift)
            {
                int high = Mathf.Max(m.FloorAt(c), m.FloorOnAt(c));
                overrideCell = c;
                overrideFloor = high;
                chunkTint = new Color(0, 0, 0, 1);
                var pivot = CellCenter(m, cs, fh, c);
                pivot.y = -BaseDepth;
                res.chunks[c] = BuildChunk(c, pivot);
                res.chunkCenters[c] = pivot;
                res.liftHeights[c] = (high - 1) * fh + BaseDepth;
                overrideCell = null;
                continue;
            }
            var tint = new Color(type == CellType.Ice ? 1 : 0, 0, 0, 1);
            AddTop(main, c, tint);
            foreach (var d in MoveRules.Directions)
                if (Lower(c, c + d)) AddWall(main, c, d, Vector3.zero, Solid(c + d) ? TopY(c + d) - 0.08f : -BaseDepth, Solid(c + d) ? 0.72f : 0.5f);
            AddBottom(bottom, c);
        }
        bottom.AppendTo(main);
        res.main = main.ToMesh("Terrain");
        return res;
    }

    /// <summary>
    /// 칸 c 의 격자 (i,j) 가 바깥(볼록) 모서리면 칸 안쪽으로 당긴 xz 보정.
    /// 둘러싼 세 칸이 모두 낮을 때만 → 그 정점을 같은 높이 칸과 공유하지 않으므로 안전.
    /// </summary>
    static Vector3 CornerPull(Vector2Int c, int i, int j)
    {
        bool cornerX = i == 0 || i == Sub, cornerZ = j == 0 || j == Sub;
        if (!cornerX || !cornerZ) return Vector3.zero;
        int dx = i == 0 ? -1 : 1, dz = j == 0 ? -1 : 1;
        if (Lower(c, c + new Vector2Int(dx, 0)) && Lower(c, c + new Vector2Int(0, dz)) && Lower(c, c + new Vector2Int(dx, dz)))
            return new Vector3(-dx, 0, -dz) * CornerRound * cs;
        return Vector3.zero;
    }

    /// <summary>
    /// 처마: 낮은 쪽 변 위의 정점(모서리 제외)을 바깥으로 민다.
    /// 이런 정점은 같은 높이 칸과 공유되지 않으므로 안전.
    /// </summary>
    static Vector3 LipPush(Vector2Int c, int i, int j)
    {
        bool edgeX = i == 0 || i == Sub, edgeZ = j == 0 || j == Sub;
        if (edgeX == edgeZ) return Vector3.zero;   // 안쪽이거나 모서리
        var dir = edgeX ? new Vector2Int(i == 0 ? -1 : 1, 0) : new Vector2Int(0, j == 0 ? -1 : 1);
        return Lower(c, c + dir) ? new Vector3(dir.x, 0, dir.y) * Lip * cs : Vector3.zero;
    }

    /// <summary>섬 바닥 경계 높이 (위치만으로, 들쭉날쭉).</summary>
    static float BottomEdgeY(Vector3 p) =>
        -BaseDepth - BottomJag * Mathf.PerlinNoise(p.x * 0.8f + 17.7f, p.z * 0.8f + 3.3f);

    /// <summary>윗면 정점 높이 (칸 c, 칸 안 격자 i,j). 같은 위치는 어느 칸에서 계산해도 같다.</summary>
    static float TopVertexY(Vector2Int c, int i, int j, out Vector3 outward)
    {
        float u = i / (float)Sub, v = j / (float)Sub;
        var p = Corner(c) + new Vector3(u * cs, 0, v * cs);
        float y = TopY(c) + Noise(p.x, p.z) * CenterMask(u, v);
        outward = Vector3.zero;

        bool edgeX = i == 0 || i == Sub, edgeZ = j == 0 || j == Sub;
        int dx = i == 0 ? -1 : 1, dz = j == 0 ? -1 : 1;
        float drop = 0;
        if (edgeX && edgeZ)
        {
            // 모서리: 둘러싼 세 칸 중 하나라도 낮으면 처진다
            var a = c + new Vector2Int(dx, 0); var b = c + new Vector2Int(0, dz); var d = c + new Vector2Int(dx, dz);
            if (Lower(c, a)) outward += new Vector3(dx, 0, 0);
            if (Lower(c, b)) outward += new Vector3(0, 0, dz);
            if (Lower(c, d) && outward == Vector3.zero) outward += new Vector3(dx, 0, dz) * 0.7f;
            if (Lower(c, a) || Lower(c, b) || Lower(c, d)) drop = EdgeDrop;
        }
        else if (edgeX || edgeZ)
        {
            var dir = edgeX ? new Vector2Int(dx, 0) : new Vector2Int(0, dz);
            if (Lower(c, c + dir)) { drop = EdgeDrop; outward = new Vector3(dir.x, 0, dir.y); }
        }
        else
        {
            // 가장자리 바로 안쪽 줄은 조금만 처져 둥근 어깨를 만든다 (칸 안쪽 점이라 공유되지 않음)
            if (i == 1 && Lower(c, c + Vector2Int.left)) drop += EdgeDrop * 0.28f;
            if (i == Sub - 1 && Lower(c, c + Vector2Int.right)) drop += EdgeDrop * 0.28f;
            if (j == 1 && Lower(c, c + Vector2Int.down)) drop += EdgeDrop * 0.28f;
            if (j == Sub - 1 && Lower(c, c + Vector2Int.up)) drop += EdgeDrop * 0.28f;
        }
        return y - drop;
    }

    static Vector3 TopNormal(Vector3 p, Vector3 outward)
    {
        const float e = 0.05f;
        float gx = (Noise(p.x + e, p.z) - Noise(p.x - e, p.z)) / (2 * e);
        float gz = (Noise(p.x, p.z + e) - Noise(p.x, p.z - e)) / (2 * e);
        return (new Vector3(-gx * 0.6f, 1, -gz * 0.6f) + outward.normalized * 0.75f).normalized;
    }

    static void AddTop(MeshData md, Vector2Int c, Color tint)
    {
        var idx = new int[Sub + 1, Sub + 1];
        for (int j = 0; j <= Sub; j++)
        for (int i = 0; i <= Sub; i++)
        {
            float y = TopVertexY(c, i, j, out var outward);
            var p = Corner(c) + new Vector3(i / (float)Sub * cs, y, j / (float)Sub * cs) + CornerPull(c, i, j) + LipPush(c, i, j);
            idx[i, j] = md.Add(p, TopNormal(p, outward), tint);
        }
        for (int j = 0; j < Sub; j++)
        for (int i = 0; i < Sub; i++)
            md.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
    }

    /// <summary>절벽 표면이 앞으로 튀어나오는 정도 (위치만으로).</summary>
    static float Bulge(Vector3 p) =>
        (Mathf.PerlinNoise(p.x * 1.7f + p.y * 0.9f + 3.1f, p.z * 1.7f - p.y * 1.3f + 5.7f) - 0.35f) * CliffBulge;

    /// <summary>칸 c 의 d 쪽 옆면에 절벽을 세운다 (윗면 가장자리 → bottomY). offset 은 덩어리 로컬 보정.</summary>
    static void AddWall(MeshData md, Vector2Int c, Vector2Int d, Vector3 offset, float bottomY, float bottomAo, bool chunkEdge = false)
    {
        // 옆면 가장자리를 따라가는 정점 순서 (바깥에서 볼 때 왼쪽 → 오른쪽)
        var points = new (int i, int j)[Sub + 1];
        for (int k = 0; k <= Sub; k++)
        {
            if (d == Vector2Int.up) points[k] = (Sub - k, Sub);
            else if (d == Vector2Int.down) points[k] = (k, 0);
            else if (d == Vector2Int.right) points[k] = (Sub, k);
            else points[k] = (0, Sub - k);
        }
        var outward = new Vector3(d.x, 0, d.y);
        float topAvg = TopY(c);
        int rows = Mathf.Max(2, Mathf.CeilToInt((topAvg - bottomY) / 0.32f));
        var idx = new int[Sub + 1, rows + 1];
        for (int k = 0; k <= Sub; k++)
        {
            var (i, j) = points[k];
            var basePos = Corner(c) + new Vector3(i / (float)Sub * cs, 0, j / (float)Sub * cs) + (chunkEdge ? Vector3.zero : CornerPull(c, i, j));
            var lip = chunkEdge ? Vector3.zero : LipPush(c, i, j);
            // 덩어리 가장자리는 BuildChunk 윗면 가장자리와 같은 식 (울퉁불퉁함 + 처짐)
            float topY = chunkEdge ? TopY(c) + Noise(basePos.x, basePos.z) - EdgeDrop * 0.6f : TopVertexY(c, i, j, out _);
            bool cornerColumn = k == 0 || k == Sub;
            // 섬 바닥까지 내려가는 절벽은 밑면 경계(들쭉날쭉)와 맞춘다. 무너지는 덩어리는 자기 평평한 바닥까지.
            float bottom = !chunkEdge && bottomY <= -BaseDepth + 0.001f ? BottomEdgeY(basePos) : bottomY;
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                var p = basePos + Vector3.up * Mathf.Lerp(topY, bottom, t);
                if (r == 0) { p += lip; idx[k, r] = md.Add(p - offset, (outward + Vector3.up * 0.6f).normalized, new Color(0, 0, 0, 1)); continue; }
                float bulge = cornerColumn ? 0 : Bulge(p) * Mathf.Sin(t * Mathf.PI);
                // 맨 윗줄 바로 아래는 안으로 들어가 잔디가 처마처럼 보이게
                if (r == 1) bulge -= 0.04f;
                p += outward * bulge;
                var normal = (outward + Vector3.down * 0.05f).normalized;
                float ao = Mathf.Lerp(1f, bottomAo, t);
                idx[k, r] = md.Add(p - offset, normal, new Color(0, 0, 0, ao));
            }
        }
        for (int k = 0; k < Sub; k++)
        for (int r = 0; r < rows; r++)
            md.Quad(idx[k, r + 1], idx[k + 1, r + 1], idx[k + 1, r], idx[k, r]);
    }

    // ------------------------------------------------------------ 밑면 (칸 사이 정점을 합쳐 매끈한 노멀)
    class BottomWelder
    {
        readonly Dictionary<Vector3Int, int> index = new();
        readonly List<Vector3> pos = new();
        readonly List<Vector3> nrm = new();
        readonly List<float> ao = new();
        readonly List<int> tris = new();

        public int Vertex(Vector3 p, float a)
        {
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000));
            if (index.TryGetValue(key, out int i)) return i;
            index[key] = pos.Count;
            pos.Add(p); nrm.Add(Vector3.zero); ao.Add(a);
            return pos.Count - 1;
        }

        public void Quad(int a, int b, int c, int d)   // 아래에서 볼 때 앞면
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        public void AppendTo(MeshData md)
        {
            for (int k = 0; k < tris.Count; k += 3)
            {
                var n = Vector3.Cross(pos[tris[k + 1]] - pos[tris[k]], pos[tris[k + 2]] - pos[tris[k]]);
                nrm[tris[k]] += n; nrm[tris[k + 1]] += n; nrm[tris[k + 2]] += n;
            }
            int baseIndex = md.v.Count;
            for (int i = 0; i < pos.Count; i++) md.Add(pos[i], nrm[i].normalized, new Color(0, 0, 0, ao[i]));
            foreach (var t in tris) md.t.Add(baseIndex + t);
        }
    }

    /// <summary>위치 p(로컬 xz)에서 섬 가장자리(구멍·맵 밖·무너지는 칸)까지 거리, 칸 단위.</summary>
    static float EdgeDistance(Vector3 p)
    {
        int cx = Mathf.FloorToInt(p.x / cs + map.Width / 2f), cz = Mathf.FloorToInt(p.z / cs + map.Height / 2f);
        float best = 3;
        for (int z = cz - 3; z <= cz + 3; z++)
        for (int x = cx - 3; x <= cx + 3; x++)
        {
            var c = new Vector2Int(x, z);
            if (Solid(c)) continue;
            var mn = Corner(c);
            float dx = Mathf.Max(mn.x - p.x, 0, p.x - (mn.x + cs));
            float dz = Mathf.Max(mn.z - p.z, 0, p.z - (mn.z + cs));
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz) / cs);
        }
        return best;
    }

    static void AddBottom(BottomWelder bw, Vector2Int c)
    {
        var idx = new int[Sub + 1, Sub + 1];
        for (int j = 0; j <= Sub; j++)
        for (int i = 0; i <= Sub; i++)
        {
            var p = Corner(c) + new Vector3(i / (float)Sub * cs, 0, j / (float)Sub * cs) + CornerPull(c, i, j);
            float dist = EdgeDistance(p);
            float depth = UnderDepth * Mathf.SmoothStep(0, 1, dist / 2.2f) * (0.7f + 0.6f * Mathf.PerlinNoise(p.x * 0.9f + 2.3f, p.z * 0.9f + 8.8f));
            p.y = BottomEdgeY(p) - depth;
            idx[i, j] = bw.Vertex(p, Mathf.Lerp(0.55f, 0.35f, dist / 2.2f));
        }
        for (int j = 0; j < Sub; j++)
        for (int i = 0; i < Sub; i++)
            bw.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
    }

    // ------------------------------------------------------------ 무너지는 칸 덩어리
    static Mesh BuildChunk(Vector2Int c, Vector3 center)
    {
        var md = new MeshData();
        var idx = new int[Sub + 1, Sub + 1];
        for (int j = 0; j <= Sub; j++)
        for (int i = 0; i <= Sub; i++)
        {
            float u = i / (float)Sub, v = j / (float)Sub;
            var p = Corner(c) + new Vector3(u * cs, 0, v * cs);
            bool edge = i == 0 || i == Sub || j == 0 || j == Sub;
            p.y = TopY(c) + Noise(p.x, p.z) * CenterMask(u, v) - (edge ? EdgeDrop * 0.6f : 0);
            idx[i, j] = md.Add(p - center, TopNormal(p, Vector3.zero), chunkTint);
        }
        for (int j = 0; j < Sub; j++)
        for (int i = 0; i < Sub; i++)
            md.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);

        foreach (var d in MoveRules.Directions)
            AddWall(md, c, d, center, -BaseDepth, 0.55f, chunkEdge: true);

        // 밑면: 납작한 바위 바닥
        var bw = new BottomWelder();
        var bi = new int[Sub + 1, Sub + 1];
        for (int j = 0; j <= Sub; j++)
        for (int i = 0; i <= Sub; i++)
        {
            var p = Corner(c) + new Vector3(i / (float)Sub * cs, 0, j / (float)Sub * cs);
            bool edge = i == 0 || i == Sub || j == 0 || j == Sub;
            p.y = -BaseDepth - (edge ? 0 : 0.35f * Mathf.PerlinNoise(p.x + 4.4f, p.z + 1.7f));
            bi[i, j] = bw.Vertex(p - center, 0.45f);
        }
        for (int j = 0; j < Sub; j++)
        for (int i = 0; i < Sub; i++)
            bw.Quad(bi[i, j], bi[i + 1, j], bi[i + 1, j + 1], bi[i, j + 1]);
        bw.AppendTo(md);
        return md.ToMesh($"Chunk_{c.x}_{c.y}");
    }

    /// <summary>칸 안 (u,v) 위치의 윗면 높이 (장식 놓기용, 처짐 무시).</summary>
    public static float SurfaceY(MapData m, float cellSize, float floorHeight, Vector2Int c, float u, float v)
    {
        map = m; cs = cellSize; fh = floorHeight;
        var p = Corner(c) + new Vector3(u * cs, 0, v * cs);
        return TopY(c) + Noise(p.x, p.z) * CenterMask(u, v);
    }
}
