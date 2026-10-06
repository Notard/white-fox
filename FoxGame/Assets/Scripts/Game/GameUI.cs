using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 화면 UI (코드로 만듦): 보석 개수, 스테이지 이름, 시간, 일시정지 버튼, 조작 안내,
/// "준비… 시작!", Clear / Fail / 일시정지 창.
/// </summary>
public class GameUI : MonoBehaviour
{
    public GameManager game;
    public Sprite gemIcon;
    public Sprite roundedSprite;
    public UiSkin skin;

    Text gemText, stageText, timeText, readyText;
    RectTransform gemBadge;
    Dialog result, pause;
    Button pauseButton;

    public Dialog ResultDialog => result;
    public Dialog PauseDialog => pause;

    void Awake()
    {
        if (game == null) game = FindAnyObjectByType<GameManager>();
        UiKit.Init(roundedSprite, skin);
        if (skin != null) skin.ApplySky(Camera.main, transform);
        Build();
        game.GemsChanged += OnGemsChanged;
        game.StateChanged += OnStateChanged;
        game.PauseChanged += OnPauseChanged;
    }

    // 스테이지는 GameManager.Awake 에서 불러오므로 Start 에서 표시
    void Start()
    {
        stageText.text = $"스테이지 {game.StageIndex + 1}   ·   {game.Map.name}";
        var badge = (RectTransform)stageText.transform.parent;
        badge.sizeDelta = new Vector2(stageText.preferredWidth + 56, badge.sizeDelta.y);
    }

    void OnDestroy()
    {
        if (game == null) return;
        game.GemsChanged -= OnGemsChanged;
        game.StateChanged -= OnStateChanged;
        game.PauseChanged -= OnPauseChanged;
    }

    void Update() => timeText.text = UiKit.FormatTime(game.Elapsed);

    // ------------------------------------------------------------ 구성
    void Build()
    {
        var canvas = UiKit.CreateCanvas(transform).transform;

        // 보석 개수 (왼쪽 위)
        gemBadge = UiKit.Badge(canvas, "GemBadge", 70);
        UiKit.Anchor(gemBadge, new Vector2(0, 1), new Vector2(28, -24), new Vector2(200, 70));
        var icon = UiKit.Icon(gemBadge, skin != null && skin.iconGem != null ? skin.iconGem : gemIcon, 56);
        UiKit.Anchor(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(56, 56), new Vector2(0, 0.5f));
        gemText = UiKit.Label(gemBadge, "0 / 0", 34, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
        UiKit.Anchor(gemText.rectTransform, new Vector2(0, 0.5f), new Vector2(84, 0), new Vector2(110, 60), new Vector2(0, 0.5f));

        // 스테이지 (가운데 위)
        var stageBadge = UiKit.Badge(canvas, "StageBadge", 54);
        UiKit.Anchor(stageBadge, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(360, 54), new Vector2(0.5f, 1));
        stageText = UiKit.Label(stageBadge, "", 24, UiKit.Ink);
        UiKit.Stretch(stageText.rectTransform);

        // 시간 + 일시정지 버튼 (오른쪽 위)
        var timeBadge = UiKit.Badge(canvas, "TimeBadge", 70);
        UiKit.Anchor(timeBadge, new Vector2(1, 1), new Vector2(-108, -24), new Vector2(196, 70));
        var clock = UiKit.Icon(timeBadge, skin != null ? skin.iconClock : null, 44);
        UiKit.Anchor(clock.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(44, 44), new Vector2(0, 0.5f));
        timeText = UiKit.Label(timeBadge, "0:00.0", 30, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
        UiKit.Anchor(timeText.rectTransform, new Vector2(0, 0.5f), new Vector2(clock.enabled ? 72 : 26, 0), new Vector2(110, 60), new Vector2(0, 0.5f));

        var pauseRt = UiKit.Panel(canvas, "PauseButton", skin != null && skin.iconPause != null ? Color.clear : UiKit.Glass);
        UiKit.Anchor(pauseRt, new Vector2(1, 1), new Vector2(-24, -24), new Vector2(70, 70));
        pauseButton = pauseRt.gameObject.AddComponent<Button>();
        pauseButton.targetGraphic = pauseRt.GetComponent<Image>();
        pauseButton.onClick.AddListener(() => game.SetPaused(true));
        var pauseIcon = UiKit.Icon(pauseRt, skin != null ? skin.iconPause : null, 66);
        UiKit.Anchor(pauseIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66, 66));
        if (!pauseIcon.enabled) UiKit.Stretch(UiKit.Label(pauseRt, "II", 30, UiKit.Ink).rectTransform);

        // 조작 안내 (아래)
        var hint = UiKit.Label(canvas, "WASD 이동   ·   Space 점프   ·   Q 기다리기   ·   휠 확대·축소   ·   R 다시 시작   ·   Esc 일시정지", 22, new Color(1, 1, 1, 0.95f));
        UiKit.Anchor(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(800, 40), new Vector2(0.5f, 0));
        var hintLine = hint.gameObject.AddComponent<Outline>();       // 밝은 하늘 위에서도 읽히게
        hintLine.effectColor = new Color(0.12f, 0.33f, 0.6f, 0.9f);
        hintLine.effectDistance = new Vector2(2, -2);

        // 준비… 시작!
        readyText = UiKit.Label(canvas, "", 84, Color.white);
        UiKit.Anchor(readyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(800, 140));
        var o = readyText.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.15f, 0.42f, 0.75f, 0.9f);
        o.effectDistance = new Vector2(3, -3);

        result = new Dialog(this, canvas, "Result");
        pause = new Dialog(this, canvas, "Pause");
    }

    // ------------------------------------------------------------ 이벤트
    void OnGemsChanged(int collected, int total)
    {
        gemText.text = $"{collected} / {total}";
        if (collected > 0) StartCoroutine(UiKit.Punch(gemBadge));
    }

    void OnStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Ready:
                StartCoroutine(ReadyBanner());
                break;
            case GameState.Cleared:
                pauseButton.interactable = false;
                string time = UiKit.FormatTime(game.Elapsed);
                string record = game.IsNewRecord ? $"기록 {time}   ★ 새 기록!" : $"기록 {time}   (최고 {UiKit.FormatTime(game.BestTime ?? game.Elapsed)})";
                if (game.IsLastStage)
                    result.Show("마지막 스테이지 클리어!", UiKit.Accent, record, 1.0f,
                        ("엔딩 보기", game.Continue, true), ("스테이지 선택", game.ToStageSelect, false));
                else
                    result.Show("스테이지 클리어!", UiKit.Accent, record, 1.0f,
                        ("다음 스테이지", game.Continue, true), ("스테이지 선택", game.ToStageSelect, false));
                break;
            case GameState.GameOver:
                pauseButton.interactable = false;
                result.Show("앗, 떨어졌어요!", UiKit.Ink, "다시 도전해 볼까요?", 0.4f,
                    ("다시 하기", game.Restart, true), ("그만하기", game.ToMain, false));
                break;
        }
    }

    void OnPauseChanged(bool paused)
    {
        if (paused)
            pause.Show("일시정지", UiKit.Ink, $"스테이지 {game.StageIndex + 1}   ·   {UiKit.FormatTime(game.Elapsed)}", 0,
                ("계속", () => game.SetPaused(false), true), ("메인으로", game.ToMain, false));
        else pause.Hide();
    }

    IEnumerator ReadyBanner()
    {
        if (GameManager.ReadyDuration <= 0) yield break;
        readyText.text = "준비…";
        yield return Pop(readyText.rectTransform);
        while (game.State == GameState.Ready) yield return null;
        readyText.text = "시작!";
        yield return Pop(readyText.rectTransform);
        yield return new WaitForSeconds(0.35f);
        readyText.text = "";
    }

    static IEnumerator Pop(RectTransform rt)
    {
        for (float t = 0; t < 1; t += Time.unscaledDeltaTime / 0.25f)
        {
            float s = t < 0.6f ? Mathf.Lerp(0.5f, 1.15f, t / 0.6f) : Mathf.Lerp(1.15f, 1f, (t - 0.6f) / 0.4f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;
    }
}
