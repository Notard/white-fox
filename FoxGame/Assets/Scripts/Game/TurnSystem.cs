using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 턴 진행 (TurnRules) 과 움직이는 것들의 모습: 눈덩이는 굴러가고, 부엉이는 날며 흔들리고, 발판은 떠서 움직인다.
/// 여우가 한 번 행동할 때마다 FoxController 가 Advance 를 부른다. 발판에 탄 여우는 같이 옮긴다.
/// 적·발판이 다닐 길은 바닥에 옅은 점으로 보여 준다.
/// </summary>
public class TurnSystem : MonoBehaviour
{
    public Board board;
    public GameObject snowballPrefab;
    public GameObject owlPrefab;
    public GameObject platformPrefab;
    public Material pathDotMaterial;
    [Tooltip("움직이는 것들이 한 칸 가는 시간")]
    public float moveDuration = 0.24f;
    public float owlHeight = 0.35f;

    class View
    {
        public Mover mover;
        public Transform t;
        public float phase;   // 부엉이마다 날갯짓 박자를 다르게
        public Transform roll; // 눈덩이: 공 가운데를 축으로 구르는 부분
    }

    readonly List<View> views = new();
    public int Turn => board.State.turn;

    void Awake()
    {
        if (board == null) board = FindAnyObjectByType<Board>();
    }

    void Start()
    {
        if (board.Map?.movers == null) return;
        foreach (var m in board.Map.movers)
        {
            var prefab = m.kind == Mover.Owl ? owlPrefab : m.kind == Mover.Platform ? platformPrefab : snowballPrefab;
            if (prefab == null || m.path.Count == 0) continue;
            var go = Instantiate(prefab, transform);
            go.name = $"{m.kind}_{views.Count}";
            go.transform.localScale = Vector3.one * board.cellSize;
            var v = new View { mover = m, t = go.transform, phase = views.Count * 1.9f };
            if (m.kind == Mover.Snowball) v.roll = MakeRollPivot(go.transform);
            views.Add(v);
            Place(v, m.PositionAt(board.State.turn), 0);
            // 처음에는 다음에 갈 쪽을 본다 (길이 한 칸이면 카메라 쪽)
            var next = m.PositionAt(board.State.turn + 1) - m.PositionAt(board.State.turn);
            if (m.kind != Mover.Platform) Face(v.t, next == Vector2Int.zero ? Vector3.back : new Vector3(next.x, 0, next.y));
            DrawPath(m);
        }
    }

    Vector3 Where(View v, Vector2Int cell) =>
        board.CellToWorld(cell) + (v.mover.kind == Mover.Owl ? Vector3.up * owlHeight * board.cellSize : Vector3.zero);

    void Place(View v, Vector2Int cell, float time)
    {
        var p = Where(v, cell);
        if (v.mover.kind == Mover.Owl) p += Vector3.up * Mathf.Sin(time * 3f + v.phase) * 0.06f;
        v.t.position = p;
    }

    void Update()
    {
        // 부엉이는 제자리에서도 날갯짓하듯 위아래로
        if (moving) return;
        foreach (var v in views)
            if (v.mover.kind == Mover.Owl) Place(v, v.mover.PositionAt(board.State.turn), Time.time);
    }

    bool moving;

    /// <summary>
    /// 여우 행동 r 뒤의 턴을 진행하고 움직임을 연출한다. fox 가 발판에 타 있으면 같이 옮긴다. 끝나면 done(결과).
    /// (무너짐·스위치는 FoxController 가 이미 반영했다)
    /// </summary>
    public IEnumerator Advance(Vector2Int from, MoveResult r, Transform fox, Action<TurnRules.Outcome> done)
    {
        int before = board.State.turn;
        var outcome = TurnRules.Advance(board.Map, board.State, from, r);
        int after = board.State.turn;
        if (views.Count == 0) { done(outcome); yield break; }

        moving = true;
        var foxFrom = fox.position;
        var foxTo = outcome.carried ? board.CellToWorld(outcome.cell) : foxFrom;
        for (float k = 0; k < 1; k += Time.deltaTime / moveDuration)
        {
            float e = Mathf.SmoothStep(0, 1, k);
            foreach (var v in views)
            {
                var a = Where(v, v.mover.PositionAt(before));
                var b = Where(v, v.mover.PositionAt(after));
                var p = Vector3.Lerp(a, b, e);
                if (v.mover.kind == Mover.Owl)
                {
                    p += Vector3.up * (Mathf.Sin(Time.time * 3f + v.phase) * 0.06f + Mathf.Sin(e * Mathf.PI) * 0.15f);
                    Face(v.t, b - a);
                }
                else if (v.mover.kind == Mover.Snowball)
                {
                    Face(v.t, b - a);
                    // 한 칸에 꼭 한 바퀴 앞으로 구른다 (멈추면 화난 얼굴이 다시 앞을 봄)
                    if (v.roll != null && (b - a).sqrMagnitude > 1e-4) v.roll.localRotation = Quaternion.Euler(360f * e, 0, 0);
                }
                v.t.position = p;
            }
            if (outcome.carried) fox.position = Vector3.Lerp(foxFrom, foxTo, e);
            yield return null;
        }
        foreach (var v in views)
        {
            Place(v, v.mover.PositionAt(after), Time.time);
            if (v.roll != null) v.roll.localRotation = Quaternion.identity;
        }
        if (outcome.carried) fox.position = foxTo;
        moving = false;
        done(outcome);
    }

    const float BallRadius = 0.28f;

    /// <summary>모델을 공 가운데 높이의 빈 오브젝트 아래로 옮긴다 (바닥이 아니라 가운데를 축으로 구르게).</summary>
    static Transform MakeRollPivot(Transform root)
    {
        var pivot = new GameObject("Roll").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = Vector3.up * BallRadius;
        var children = new List<Transform>();
        foreach (Transform c in root) if (c != pivot) children.Add(c);
        foreach (var c in children) c.SetParent(pivot, true);
        return pivot;
    }

    static void Face(Transform t, Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4) return;
        var euler = t.eulerAngles;
        var target = Quaternion.LookRotation(dir).eulerAngles.y;
        t.rotation = Quaternion.Euler(euler.x, target, euler.z);
    }

    /// <summary>길을 바닥 위 옅은 점으로 (적 = 붉은 점, 발판 = 하늘색 점).</summary>
    void DrawPath(Mover m)
    {
        if (pathDotMaterial == null) return;
        var color = m.IsEnemy ? new Color(1f, 0.42f, 0.42f, 0.55f) : new Color(0.45f, 0.75f, 1f, 0.6f);
        for (int i = 0; i < m.path.Count; i++)
        {
            var a = board.CellToWorld(m.path[i]);
            // 부엉이가 날아 지나는 구멍 위에는 점을 찍지 않는다 (땅이 없음)
            bool overHole = board.Map.TypeAt(m.path[i]) == CellType.Hole && !m.IsPlatform;
            if (overHole) continue;
            Dot(a, color, 0.16f);
            if (i + 1 < m.path.Count && (m.IsPlatform || board.Map.TypeAt(m.path[i + 1]) != CellType.Hole))
            {
                var b = board.CellToWorld(m.path[i + 1]);
                for (int k = 1; k <= 2; k++) Dot(Vector3.Lerp(a, b, k / 3f), color, 0.09f);
            }
        }
    }

    void Dot(Vector3 at, Color color, float size)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "PathDot";
        q.transform.SetParent(transform, false);
        q.transform.position = at + Vector3.up * 0.03f;
        q.transform.rotation = Quaternion.Euler(90, 0, 0);
        q.transform.localScale = Vector3.one * size * board.cellSize;
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = pathDotMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }
}
