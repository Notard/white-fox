using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 칸 단위 여우 이동. WASD = 한 칸 이동, Space = 점프. 규칙은 MoveRules, 판 상태(무너진 칸·문)는 Board 가 가진다.
/// 얼음에서는 경로를 따라 미끄러지고, 지나가는 칸마다 Entered 를 보낸다 (보석 먹기).
/// </summary>
[RequireComponent(typeof(Animator))]
public class FoxController : MonoBehaviour
{
    public Board board;
    [Header("이동")]
    public float stepDuration = 0.32f;
    public float turnSpeed = 900f;        // 도/초
    [Header("점프 (Jump 클립 1.25초 기준)")]
    public float jumpTakeoff = 0.3f;      // 발이 땅에서 떨어지는 시점
    public float jumpLanding = 0.85f;     // 착지 시점
    public float jumpLength = 1.25f;
    [Header("얼음")]
    public float slideDuration = 0.13f;   // 한 칸 미끄러지는 시간
    [Header("낙하")]
    public float fallGravity = 22f;
    public float fallDuration = 1.2f;

    public Vector2Int Cell { get; private set; }
    public Vector2Int Facing { get; private set; } = Vector2Int.down;
    public bool IsBusy { get; private set; }
    public bool HasFallen { get; private set; }
    public bool InputEnabled { get; set; } = true;

    /// <summary>이동·점프가 끝나 땅에 멈췄을 때 (멈춘 칸).</summary>
    public event Action<Vector2Int> Arrived;
    /// <summary>칸에 들어설 때마다 (미끄러지며 지나가는 칸 포함). 보석 먹기용.</summary>
    public event Action<Vector2Int> Entered;
    /// <summary>구멍이나 맵 밖으로 떨어졌거나 적과 부딪혔을 때 (실패).</summary>
    public event Action Fell;
    /// <summary>걸음을 디딜 때 (위치, 방향) — 발자국 연출.</summary>
    public event Action<Vector3, Vector2Int> Stepped;
    /// <summary>점프 착지 (위치) — 먼지 연출.</summary>
    public event Action<Vector3> Landed;
    /// <summary>적과 부딪혀 실패했는지.</summary>
    public bool WasHit { get; private set; }

    static readonly int MovingId = Animator.StringToHash("Moving");
    static readonly int JumpId = Animator.StringToHash("Jump");

    Animator animator;
    FoxExpression expression;
    TurnSystem turns;
    Quaternion targetRotation;

    void Awake()
    {
        animator = GetComponent<Animator>();
        expression = GetComponent<FoxExpression>();
        if (board == null) board = FindAnyObjectByType<Board>();
        turns = FindAnyObjectByType<TurnSystem>();
        targetRotation = transform.rotation;
    }

    // 보드는 GameManager.Awake 에서 스테이지로 만들어진다
    void Start() => PlaceAt(board.Map.StartCell, MoveRules.StartFacing);

    /// <summary>지정한 칸에 바로 세운다 (시작/재시작).</summary>
    public void PlaceAt(Vector2Int cell, Vector2Int facing)
    {
        StopAllCoroutines();
        Cell = cell;
        Facing = facing;
        IsBusy = false;
        HasFallen = false;
        WasHit = false;
        transform.position = board.CellToWorld(cell);
        targetRotation = transform.rotation = FacingRotation(facing);
        animator.SetBool(MovingId, false);
        animator.Rebind();
        if (expression != null) expression.SetMood(FoxMood.Normal);
    }

    void Update()
    {
        if (HasFallen) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        if (!IsBusy) FollowGround();
        if (!InputEnabled || IsBusy) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame) { TryJump(); return; }
        if (kb.qKey.wasPressedThisFrame) { TryWait(); return; }

        Vector2Int dir = Vector2Int.zero;
        if (kb.wKey.isPressed) dir = Vector2Int.up;
        else if (kb.sKey.isPressed) dir = Vector2Int.down;
        else if (kb.aKey.isPressed) dir = Vector2Int.left;
        else if (kb.dKey.isPressed) dir = Vector2Int.right;

        if (dir != Vector2Int.zero) TryMove(dir);
        else animator.SetBool(MovingId, false);
    }

    /// <summary>서 있는 칸 높이가 바뀌면(승강 땅) 땅과 같은 속도로 따라 오르내린다.</summary>
    void FollowGround()
    {
        var p = transform.position;
        float y = board.CellToWorld(Cell).y;
        if (Mathf.Abs(p.y - y) < 1e-4f) return;
        transform.position = new Vector3(p.x, Mathf.MoveTowards(p.y, y, board.LiftSpeed * Time.deltaTime), p.z);
    }

    // ------------------------------------------------------------ 동작
    public bool TryMove(Vector2Int dir)
    {
        if (IsBusy || HasFallen) return false;
        var from = Cell;
        var r = MoveRules.Walk(board.Map, board.State, from, dir);
        Face(r.facing);
        if (r.outcome == MoveOutcome.Blocked)
        {
            // 레버 쪽이면 당긴다 (제자리, 한 턴)
            var pulled = r.pulled != null ? board.ApplyMove(from, r) : default;
            if (r.pulled != null && expression != null) expression.Flash(FoxMood.Happy, 0.35f);
            StartCoroutine(Bump(dir, r, pulled));
            return r.pulled != null;
        }
        var changes = board.ApplyMove(from, r);
        turnFrom = from;
        StartCoroutine(Step(r, changes));
        return true;
    }

    public bool TryJump()
    {
        if (IsBusy || HasFallen) return false;
        // 막히면 제자리 점프 (MoveRules 가 제자리 칸을 돌려줌)
        var from = Cell;
        var r = MoveRules.Jump(board.Map, board.State, from, Facing);
        var changes = board.ApplyMove(from, r);
        turnFrom = from;
        StartCoroutine(Jump(r, changes));
        return true;
    }

    /// <summary>제자리에서 한 턴 기다린다 (적·발판만 움직인다).</summary>
    public bool TryWait()
    {
        if (IsBusy || HasFallen) return false;
        StartCoroutine(WaitTurn());
        return true;
    }

    IEnumerator WaitTurn()
    {
        IsBusy = true;
        animator.SetBool(MovingId, false);
        yield return EndTurn(Cell, TurnRules.Wait(Cell, Facing));
    }

    /// <summary>
    /// 행동이 끝난 뒤 턴 진행: 적·발판이 움직이고, 발판에 타 있으면 실려 가고, 적과 부딪히면 실패.
    /// 실패가 아니면 Arrived 를 보낸다.
    /// </summary>
    IEnumerator EndTurn(Vector2Int from, MoveResult r)
    {
        IsBusy = true;
        var outcome = new TurnRules.Outcome { cell = Cell };
        if (turns != null) yield return turns.Advance(from, r, transform, o => outcome = o);
        else board.State.turn++;
        Cell = outcome.cell;
        if (outcome.carried) board.SetFoxCell(Cell);
        if (outcome.hit) { yield return Hit(); yield break; }
        IsBusy = false;
        Arrived?.Invoke(Cell);
    }

    /// <summary>적과 부딪힘: 튕겨 날아가며 실패.</summary>
    IEnumerator Hit()
    {
        HasFallen = true;
        WasHit = true;
        IsBusy = true;
        animator.SetBool(MovingId, false);
        if (expression != null) expression.SetMood(FoxMood.Closed);
        Vector3 back = -new Vector3(Facing.x, 0, Facing.y);
        float vy = 7f;
        for (float t = 0; t < fallDuration; t += Time.deltaTime)
        {
            vy -= fallGravity * Time.deltaTime;
            transform.position += (Vector3.up * vy + back * 2.2f) * Time.deltaTime;
            transform.Rotate(Vector3.forward, 540f * Time.deltaTime, Space.Self);
            yield return null;
        }
        Fell?.Invoke();
    }

    /// <summary>클리어 축하: 카메라 쪽을 보고 제자리에서 계속 폴짝 뛴다.</summary>
    public void Celebrate()
    {
        InputEnabled = false;
        Face(Vector2Int.down);
        if (expression != null) expression.SetMood(FoxMood.Happy);
        StartCoroutine(CelebrateLoop());
    }

    IEnumerator CelebrateLoop()
    {
        animator.SetBool(MovingId, false);
        while (true)
        {
            animator.SetTrigger(JumpId);
            yield return new WaitForSeconds(jumpLength + 0.25f);
        }
    }

    void Face(Vector2Int dir)
    {
        Facing = dir;
        targetRotation = FacingRotation(dir);
    }

    static Quaternion FacingRotation(Vector2Int dir) =>
        Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y), Vector3.up);

    /// <summary>도착 위치. 구멍/맵 밖으로 가면 지금 높이 그대로 그 위치까지 간 뒤 떨어진다.</summary>
    Vector3 Destination(Vector2Int target, bool hole)
    {
        var to = board.CellToWorld(target);
        if (hole) to.y = transform.position.y;
        return to;
    }

    static bool IsFallCell(MoveResult r, int i) => r.outcome == MoveOutcome.Fall && i == r.path.Count - 1;

    IEnumerator Step(MoveResult r, MoveRules.Changes changes)
    {
        IsBusy = true;
        animator.SetBool(MovingId, true);
        bool hole = IsFallCell(r, 0);
        Vector3 from = transform.position, to = Destination(r.path[0], hole);
        float dy = to.y - from.y;
        var dir = r.facing;
        bool steppedHalf = false;
        Stepped?.Invoke(from, dir);
        for (float t = 0; t < 1; t += Time.deltaTime / stepDuration)
        {
            if (!steppedHalf && t > 0.5f && !hole) { steppedHalf = true; Stepped?.Invoke(Vector3.Lerp(from, to, 0.5f), dir); }
            var p = Vector3.Lerp(from, to, t);
            if (Mathf.Abs(dy) > 0.01f)
            {
                // 오를 때는 먼저 올라서고, 내려갈 때는 끝에서 내려앉는다 (턱을 뚫고 지나가지 않게)
                float k = dy > 0 ? 1 - (1 - t) * (1 - t) : t * t;
                p.y = from.y + dy * k + Mathf.Sin(t * Mathf.PI) * 0.2f;
            }
            transform.position = p;
            yield return null;
        }
        transform.position = to;
        Cell = r.path[0];
        if (!hole) Entered?.Invoke(Cell);
        yield return SlideRest(r, 1);
        yield return Finish(r, changes);
    }

    IEnumerator Jump(MoveResult r, MoveRules.Changes changes)
    {
        IsBusy = true;
        animator.SetBool(MovingId, false);
        animator.SetTrigger(JumpId);
        if (expression != null) expression.Flash(FoxMood.Happy, jumpLanding);
        bool moved = r.path.Count > 0;
        var target = moved ? r.path[0] : Cell;
        bool hole = moved && IsFallCell(r, 0);
        Vector3 from = transform.position, to = Destination(target, hole);
        float climb = Mathf.Max(0, to.y - from.y);
        for (float t = 0; t < jumpLength; t += Time.deltaTime)
        {
            float k = Mathf.InverseLerp(jumpTakeoff, jumpLanding, t);
            // 높은 곳으로 뛸 때는 오르는 높이만큼 궤적을 더 띄운다 (점프 동작 자체도 위로 뜬다)
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, k)) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * climb * 0.6f);
            // 착지 직후에 바로 다음 입력을 받을 수 있게 남은 착지 동작은 기다리지 않음
            if (t > jumpLanding + 0.12f) break;
            yield return null;
        }
        transform.position = to;
        Cell = target;
        if (!hole) Landed?.Invoke(to);
        if (moved && !hole) Entered?.Invoke(Cell);
        if (moved) yield return SlideRest(r, 1);
        yield return Finish(r, changes);
    }

    /// <summary>얼음 위를 path[index..] 를 따라 미끄러진다 (걷는 동작 없이 미끄러지듯).</summary>
    IEnumerator SlideRest(MoveResult r, int index)
    {
        if (index >= r.path.Count) yield break;
        animator.SetBool(MovingId, false);
        for (int i = index; i < r.path.Count; i++)
        {
            bool hole = IsFallCell(r, i);
            Vector3 from = transform.position, to = Destination(r.path[i], hole);
            for (float t = 0; t < 1; t += Time.deltaTime / slideDuration)
            {
                var p = Vector3.Lerp(from, to, t);
                if (to.y < from.y) p.y = Mathf.Lerp(from.y, to.y, t * t);   // 낮은 칸으로는 끝에서 내려앉음
                transform.position = p;
                yield return null;
            }
            transform.position = to;
            Cell = r.path[i];
            if (!hole) Entered?.Invoke(Cell);
        }
    }

    IEnumerator Finish(MoveResult r, MoveRules.Changes changes)
    {
        if (r.outcome == MoveOutcome.Fall) { IsBusy = false; yield return Fall(); yield break; }
        if (changes.foxFalls) { yield return new WaitForSeconds(0.12f); yield return Fall(); yield break; }   // 발밑 다리가 사라짐
        if (changes.doorsOpened) board.ShowDoorsOpen();
        yield return EndTurn(turnFrom, r);
    }

    Vector2Int turnFrom;   // 이번 행동을 시작한 칸 (턴 진행용)

    IEnumerator Bump(Vector2Int dir, MoveResult r, MoveRules.Changes changes = default)
    {
        IsBusy = true;
        animator.SetBool(MovingId, false);
        Vector3 home = board.CellToWorld(Cell);
        Vector3 push = new Vector3(dir.x, 0, dir.y) * board.cellSize * 0.12f;
        const float d = 0.22f;
        for (float t = 0; t < d; t += Time.deltaTime)
        {
            transform.position = home + push * Mathf.Sin(t / d * Mathf.PI);
            yield return null;
        }
        transform.position = home;
        if (changes.foxFalls) { yield return Fall(); yield break; }   // 레버로 발밑 다리를 꺼 버림
        yield return EndTurn(Cell, r);   // 부딪혀도(레버를 당겨도) 한 턴이 지난다
    }

    IEnumerator Fall()
    {
        HasFallen = true;
        IsBusy = true;
        if (expression != null) expression.SetMood(FoxMood.Closed);
        animator.SetBool(MovingId, false);
        Vector3 forward = new Vector3(Facing.x, 0, Facing.y);
        float vy = 1.5f;
        for (float t = 0; t < fallDuration; t += Time.deltaTime)
        {
            vy -= fallGravity * Time.deltaTime;
            transform.position += (Vector3.up * vy + forward * 0.6f) * Time.deltaTime;
            transform.Rotate(Vector3.right, 220f * Time.deltaTime, Space.Self);
            yield return null;
        }
        Fell?.Invoke();
    }
}
