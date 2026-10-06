using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum GameState
{
    /// <summary>"준비… 시작!" 동안. 움직일 수 없다.</summary>
    Ready,
    Playing,
    Cleared,
    /// <summary>떨어짐 (Fail).</summary>
    GameOver,
}

/// <summary>
/// 현재 스테이지를 불러와 보드를 만들고 Ready → Playing → Clear/Fail 을 진행한다.
/// 보석 획득, 시간 재기, 기록 저장·해금, 일시정지(Esc), 재시작(R), 화면 이동(SceneFlow).
/// </summary>
public class GameManager : MonoBehaviour
{
    public Board board;
    public FoxController fox;
    public Gem gemPrefab;

    /// <summary>Ready 시간 (초). 테스트에서는 0 으로 둔다.</summary>
    public static float ReadyDuration = 1.3f;
    [Tooltip("클리어하면 카메라가 여우 쪽으로 다가가는 정도 (0~1)")]
    public float clearZoom = 0.85f;

    public GameState State { get; private set; } = GameState.Ready;
    public bool IsPaused { get; private set; }
    public int Collected { get; private set; }
    public int Total { get; private set; }
    /// <summary>Playing 부터 잰 시간 (일시정지 중에는 멈춤).</summary>
    public float Elapsed { get; private set; }
    /// <summary>클리어했을 때 이번 기록이 최고 기록인지.</summary>
    public bool IsNewRecord { get; private set; }
    /// <summary>이 스테이지의 최고 기록 (클리어 후에는 이번 기록 포함).</summary>
    public float? BestTime => SaveData.Current.BestTime(SaveData.StageId(Map));

    public MapData Map { get; private set; }
    public int StageIndex => StageProgress.Current;
    public int StageCount => StageLibrary.Count;
    public bool IsLastStage => StageProgress.IsLast;

    public event Action<int, int> GemsChanged;        // (모은 수, 전체 수)
    public event Action<GameState> StateChanged;
    public event Action<bool> PauseChanged;

    readonly Dictionary<Vector2Int, Gem> gems = new();

    void Awake()
    {
        Time.timeScale = 1;
        if (board == null) board = FindAnyObjectByType<Board>();
        if (fox == null) fox = FindAnyObjectByType<FoxController>();

        StageProgress.Current = Mathf.Clamp(StageProgress.Current, 0, Mathf.Max(0, StageCount - 1));
        Map = StageLibrary.Load(StageProgress.Current);
        board.Build(Map);
        var fit = Camera.main != null ? Camera.main.GetComponent<CameraFit>() : null;
        if (fit != null) fit.Fit(board);
    }

    void Start()
    {
        foreach (var cell in Map.Gems)
        {
            var gem = Instantiate(gemPrefab, board.CellToWorld(cell), Quaternion.Euler(0, cell.x * 37 + cell.y * 53, 0), transform);
            gem.name = $"Gem_{cell.x}_{cell.y}";
            gem.Cell = cell;
            gem.board = board;   // 승강 땅 위 보석은 땅을 따라 오르내린다
            gems[cell] = gem;
        }
        Total = gems.Count;
        fox.Entered += OnFoxEntered;
        fox.Arrived += OnFoxArrived;
        fox.Fell += OnFoxFell;
        GemsChanged?.Invoke(Collected, Total);

        fox.InputEnabled = false;
        SetState(GameState.Ready);
        if (ReadyDuration <= 0) BeginPlaying();
        else StartCoroutine(ReadyThenPlay());
    }

    IEnumerator ReadyThenPlay()
    {
        yield return new WaitForSeconds(ReadyDuration);
        BeginPlaying();
    }

    void BeginPlaying()
    {
        fox.InputEnabled = true;
        SetState(GameState.Playing);
    }

    void OnDestroy()
    {
        if (fox == null) return;
        fox.Entered -= OnFoxEntered;
        fox.Arrived -= OnFoxArrived;
        fox.Fell -= OnFoxFell;
    }

    void Update()
    {
        if (State == GameState.Playing && !IsPaused) Elapsed += Time.deltaTime;

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame && State == GameState.Playing) SetPaused(!IsPaused);
        if (kb.rKey.wasPressedThisFrame && !IsPaused && State != GameState.Cleared) Restart();
    }

    // ------------------------------------------------------------ 진행
    /// <summary>여우가 칸에 들어설 때마다 (얼음에서 미끄러지며 지나가는 칸 포함).</summary>
    void OnFoxEntered(Vector2Int cell)
    {
        if (State != GameState.Playing) return;
        if (!gems.TryGetValue(cell, out var gem)) return;
        gems.Remove(cell);
        gem.Collect();
        Collected++;
        GemsChanged?.Invoke(Collected, Total);
        // 클리어는 이 행동의 턴이 끝난 뒤 (Arrived) — 그 사이 적과 부딪히면 실패
    }

    void OnFoxArrived(Vector2Int cell)
    {
        if (State == GameState.Playing && Collected >= Total) StartCoroutine(Clear());
    }

    IEnumerator Clear()
    {
        IsNewRecord = SaveData.Current.RecordClear(StageIndex, SaveData.StageId(Map), Elapsed);
        fox.InputEnabled = false;
        SetState(GameState.Cleared);
        // 카메라가 여우 쪽으로 다가간다
        var zoom = Camera.main != null ? Camera.main.GetComponent<CameraZoom>() : null;
        if (zoom != null) zoom.SetZoom(Mathf.Max(zoom.Target, clearZoom));
        yield return new WaitForSeconds(0.35f);
        fox.Celebrate();
    }

    void OnFoxFell()
    {
        if (State != GameState.Playing) return;
        fox.InputEnabled = false;
        SetState(GameState.GameOver);
    }

    void SetState(GameState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    public void SetPaused(bool paused)
    {
        if (paused && State != GameState.Playing) return;
        if (paused == IsPaused) return;
        IsPaused = paused;
        Time.timeScale = paused ? 0 : 1;
        fox.InputEnabled = !paused && State == GameState.Playing;
        PauseChanged?.Invoke(paused);
    }

    // ------------------------------------------------------------ 화면 이동
    /// <summary>같은 스테이지를 처음부터 (Fail "다시 하기", R).</summary>
    public void Restart() => SceneFlow.PlayStage(StageIndex);

    /// <summary>Clear "다음 스테이지". 마지막 스테이지면 엔딩으로.</summary>
    public void Continue()
    {
        if (IsLastStage) SceneFlow.GoMenu(MenuPage.Ending);
        else SceneFlow.PlayStage(StageIndex + 1);
    }

    /// <summary>Clear "스테이지 선택".</summary>
    public void ToStageSelect() => SceneFlow.GoMenu(MenuPage.StageSelect);

    /// <summary>Fail "그만하기", 일시정지 "메인으로".</summary>
    public void ToMain() => SceneFlow.GoMenu(MenuPage.Main);
}
