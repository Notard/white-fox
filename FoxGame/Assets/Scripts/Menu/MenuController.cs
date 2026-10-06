using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Menu 씬: 메인(시작·이어하기·설정·종료), 스테이지 선택(해금된 것만, 최고 기록), 설정(음량, 진행 초기화), 엔딩.
/// 뒤에서는 스테이지 1 보드 위의 여우를 카메라가 천천히 돌며 보여 준다.
/// Esc: 메인이 아닌 화면에서는 메인으로. 메인에서는 아무것도 하지 않는다 (종료는 버튼으로만).
/// </summary>
public class MenuController : MonoBehaviour
{
    public Board board;
    public Animator fox;
    public Sprite roundedSprite;
    public Sprite gemIcon;
    public UiSkin skin;
    [Tooltip("카메라가 보드를 도는 속도 (도/초)")]
    public float orbitSpeed = 6f;

    public MenuPage Page { get; private set; }
    public Button ContinueButton { get; private set; }
    public readonly List<Button> StageButtons = new();
    public Dialog Confirm { get; private set; }

    readonly Dictionary<MenuPage, CanvasGroup> pages = new();
    readonly Dictionary<MenuPage, Selectable> firstFocus = new();
    Transform canvas;
    Text continueLabel, endingStats, volumeLabel;
    RectTransform stageGrid;
    Slider volume;
    Button startButton;
    Camera cam;
    Vector3 orbitCenter, orbitOffset;
    float orbitAngle;
    Coroutine celebrate;

    static readonly int JumpId = Animator.StringToHash("Jump");

    void Awake()
    {
        Time.timeScale = 1;
        UiKit.Init(roundedSprite, skin);
        cam = Camera.main;
        if (skin != null) skin.ApplySky(cam, transform);
        SetUpBackground();
        canvas = UiKit.CreateCanvas(transform).transform;
        BuildMain();
        BuildStageSelect();
        BuildSettings();
        BuildEnding();
        Confirm = new Dialog(this, canvas, "Confirm");
    }

    void Start() => ShowPage(SceneFlow.TakeMenuPage());

    // ------------------------------------------------------------ 배경 (보드 + 여우 + 도는 카메라)
    void SetUpBackground()
    {
        if (board == null || StageLibrary.Count == 0) return;
        board.Build(StageLibrary.Load(0));
        if (fox != null)
        {
            fox.transform.position = board.CellToWorld(board.Map.StartCell);
            fox.transform.rotation = Quaternion.Euler(0, 180, 0);
        }
        var fit = cam.GetComponent<CameraFit>();
        if (fit != null) fit.Fit(board);
        orbitCenter = board.transform.position + Vector3.up * 0.3f;
        orbitOffset = cam.transform.position - orbitCenter;
        orbitOffset *= 1.1f;    // 메뉴에서는 보드 전체가 여유 있게 보이도록 조금 멀리
    }

    void LateUpdate()
    {
        if (board == null || cam == null) return;
        orbitAngle += orbitSpeed * Time.unscaledDeltaTime;
        var pos = orbitCenter + Quaternion.Euler(0, Mathf.Sin(orbitAngle * Mathf.Deg2Rad) * 28f, 0) * orbitOffset;
        cam.transform.position = pos;
        cam.transform.LookAt(orbitCenter);
        // 메뉴 버튼이 왼쪽에 있으니 보드가 화면 오른쪽에 오도록 카메라를 왼쪽으로 민다
        cam.transform.position -= cam.transform.right * board.Size.x * 0.42f;
    }

    // ------------------------------------------------------------ 화면 전환
    public void ShowPage(MenuPage page)
    {
        Page = page;
        foreach (var kv in pages) UiKit.SetVisible(kv.Value, false);
        if (page == MenuPage.Main) RefreshMain();
        if (page == MenuPage.StageSelect) RefreshStages();
        if (page == MenuPage.Ending) RefreshEnding();
        StartCoroutine(UiKit.FadeIn(pages[page], 0.2f));
        if (firstFocus.TryGetValue(page, out var f)) UiKit.Focus(f);

        if (celebrate != null) StopCoroutine(celebrate);
        celebrate = page == MenuPage.Ending && fox != null ? StartCoroutine(Celebrate()) : null;
        var expression = fox != null ? fox.GetComponent<FoxExpression>() : null;
        if (expression != null) expression.SetMood(page == MenuPage.Ending ? FoxMood.Happy : FoxMood.Normal);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (Confirm.IsOpen) { Confirm.Hide(); UiKit.Focus(firstFocus[Page]); }
        else if (Page != MenuPage.Main) ShowPage(MenuPage.Main);
    }

    public void StartGame() => ShowPage(MenuPage.StageSelect);

    public void ContinueGame()
    {
        int i = SceneFlow.ContinueStage();
        if (i >= 0) SceneFlow.PlayStage(i);
        else ShowPage(MenuPage.StageSelect);   // 모두 깼으면 고르게 한다
    }

    public void PlayStage(int index)
    {
        if (SaveData.Current.IsUnlocked(index)) SceneFlow.PlayStage(index);
    }

    // ------------------------------------------------------------ 메인
    void BuildMain()
    {
        var page = UiKit.Screen(canvas, "Main", Color.clear);
        pages[MenuPage.Main] = page;

        if (skin != null && skin.logo != null)
        {
            var logo = UiKit.Icon(page.transform, skin.logo, 520);
            UiKit.Anchor(logo.rectTransform, new Vector2(0, 1), new Vector2(56, -40), new Vector2(540, 170));
        }
        else
        {
            var title = UiKit.Label(page.transform, "White Fox", 96, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(80, -60), new Vector2(600, 130));
            var o = title.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.15f, 0.42f, 0.75f);
            o.effectDistance = new Vector2(4, -4);
        }
        var sub = UiKit.Label(page.transform, "하얀 여우의 보석 모으기", 28, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        UiKit.Anchor(sub.rectTransform, new Vector2(0, 1), new Vector2(100, skin != null && skin.logo != null ? -228 : -282), new Vector2(600, 40));
        var subLine = sub.gameObject.AddComponent<Outline>();        // 밝은 하늘 위에서도 읽히게
        subLine.effectColor = new Color(0.12f, 0.36f, 0.66f, 0.95f);
        subLine.effectDistance = new Vector2(2, -2);

        var col = UiKit.Column(page.transform, "Buttons", 16);
        UiKit.Anchor(col, new Vector2(0, 0), new Vector2(92, 80), new Vector2(300, 320), new Vector2(0, 0));
        var start = startButton = UiKit.Button(col, "시작", StartGame, true, 300, 66);
        ContinueButton = UiKit.Button(col, "이어하기", ContinueGame, false, 300, 66);
        continueLabel = ContinueButton.GetComponentInChildren<Text>();
        UiKit.Button(col, "설정", () => ShowPage(MenuPage.Settings), false, 300, 66);
        UiKit.Button(col, "종료", SceneFlow.Quit, false, 300, 66);
        firstFocus[MenuPage.Main] = start;
    }

    void RefreshMain()
    {
        bool any = SaveData.Current.records.Count > 0;
        ContinueButton.interactable = any;
        int next = SceneFlow.ContinueStage();
        continueLabel.text = !any ? "이어하기" : next >= 0 ? $"이어하기  ·  스테이지 {next + 1}" : "이어하기  ·  모두 클리어";
        firstFocus[MenuPage.Main] = any ? ContinueButton : startButton;
    }

    // ------------------------------------------------------------ 스테이지 선택
    void BuildStageSelect()
    {
        var page = UiKit.Screen(canvas, "StageSelect", new Color(0.08f, 0.14f, 0.24f, 0.25f));
        pages[MenuPage.StageSelect] = page;
        Header(page.transform, "스테이지 선택", MenuPage.StageSelect);

        // 스크롤 영역
        var view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        view.SetParent(page.transform, false);
        UiKit.Anchor(view, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1180, 520));
        stageGrid = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        stageGrid.SetParent(view, false);
        stageGrid.anchorMin = stageGrid.anchorMax = new Vector2(0.5f, 1);
        stageGrid.pivot = new Vector2(0.5f, 1);
        stageGrid.anchoredPosition = Vector2.zero;
        var grid = stageGrid.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(276, 160);
        grid.spacing = new Vector2(18, 18);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperCenter;
        stageGrid.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        stageGrid.sizeDelta = new Vector2(4 * 276 + 3 * 18, 0);
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = stageGrid;
        scroll.horizontal = false;
        scroll.viewport = view;
        scroll.scrollSensitivity = 30;
    }

    void RefreshStages()
    {
        for (int i = stageGrid.childCount - 1; i >= 0; i--) Destroy(stageGrid.GetChild(i).gameObject);
        StageButtons.Clear();
        var save = SaveData.Current;
        for (int i = 0; i < StageLibrary.Count; i++)
        {
            var map = StageLibrary.Load(i);
            bool open = save.IsUnlocked(i);
            var best = save.BestTime(SaveData.StageId(map));
            int index = i;

            var card = UiKit.CardPanel(stageGrid, $"Stage_{i + 1}");
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.9f, 0.96f, 1f);
            colors.selectedColor = new Color(0.82f, 0.91f, 1f);
            colors.disabledColor = new Color(1, 1, 1, 0.55f);
            btn.colors = colors;
            btn.interactable = open;
            btn.onClick.AddListener(() => PlayStage(index));
            StageButtons.Add(btn);

            var num = UiKit.Label(card, (i + 1).ToString(), 54, open ? UiKit.Accent : UiKit.Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            UiKit.Anchor(num.rectTransform, new Vector2(0, 1), new Vector2(24, -14), new Vector2(80, 70));
            var name = UiKit.Label(card, map.name, 24, open ? UiKit.Ink : UiKit.Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            UiKit.Anchor(name.rectTransform, new Vector2(0, 1), new Vector2(24, -82), new Vector2(240, 34));
            string size = $"{map.Width}×{map.Height}" + (map.HighestFloor > 1 ? $" · {map.HighestFloor}층" : "") + $" · 보석 {map.Gems.Count}";
            var info = UiKit.Label(card, size, 18, UiKit.Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            UiKit.Anchor(info.rectTransform, new Vector2(0, 1), new Vector2(24, -112), new Vector2(240, 26));

            // 오른쪽 위: 잠금 또는 최고 기록
            if (!open)
            {
                var lockIcon = UiKit.Icon(card, skin != null ? skin.iconLock : null, 46);
                UiKit.Anchor(lockIcon.rectTransform, new Vector2(1, 1), new Vector2(-20, -22), new Vector2(46, 46));
                var locked = UiKit.Label(card, lockIcon.enabled ? "" : "잠김", 20, UiKit.Muted, FontStyle.Bold, TextAnchor.MiddleRight);
                UiKit.Anchor(locked.rectTransform, new Vector2(1, 1), new Vector2(-20, -30), new Vector2(120, 30));
            }
            else
            {
                var rec = UiKit.Label(card, best.HasValue ? UiKit.FormatTime(best.Value) : "기록 없음", best.HasValue ? 22 : 17,
                    best.HasValue ? UiKit.Ink : UiKit.Muted, FontStyle.Bold, TextAnchor.MiddleRight);
                UiKit.Anchor(rec.rectTransform, new Vector2(1, 1), new Vector2(-22, -30), new Vector2(130, 30));
                if (best.HasValue)
                {
                    var clock = UiKit.Icon(card, skin != null ? skin.iconClock : null, 28);
                    UiKit.Anchor(clock.rectTransform, new Vector2(1, 1), new Vector2(-22 - rec.preferredWidth - 6, -31), new Vector2(28, 28));
                }
            }
        }
        // 이어서 할 스테이지(없으면 첫 칸)에 초점
        int next = Mathf.Max(0, SceneFlow.ContinueStage());
        firstFocus[MenuPage.StageSelect] = StageButtons.Count > next && StageButtons[next].interactable ? StageButtons[next] : firstFocus[MenuPage.StageSelect];
    }

    /// <summary>화면 위 제목 + 왼쪽 위 "메인으로" 버튼.</summary>
    void Header(Transform page, string title, MenuPage pageKey)
    {
        var t = UiKit.Label(page, title, 52, Color.white);
        UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(800, 80), new Vector2(0.5f, 1));
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.15f, 0.42f, 0.75f);
        o.effectDistance = new Vector2(3, -3);
        var back = UiKit.Button(page, "← 메인으로", () => ShowPage(MenuPage.Main), false, 200, 56, 22);
        UiKit.Anchor(back.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(36, -40), new Vector2(200, 56));
        firstFocus[pageKey] = back;
    }

    // ------------------------------------------------------------ 설정
    void BuildSettings()
    {
        var page = UiKit.Screen(canvas, "Settings", new Color(0.08f, 0.14f, 0.24f, 0.25f));
        pages[MenuPage.Settings] = page;
        Header(page.transform, "설정", MenuPage.Settings);

        var card = UiKit.CardPanel(page.transform, "Card");
        UiKit.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(640, 340));

        var vl = UiKit.Label(card, "음량", 28, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
        UiKit.Anchor(vl.rectTransform, new Vector2(0, 1), new Vector2(56, -50), new Vector2(200, 40));
        volumeLabel = UiKit.Label(card, "", 26, UiKit.Accent, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Anchor(volumeLabel.rectTransform, new Vector2(1, 1), new Vector2(-56, -50), new Vector2(120, 40));
        volume = MakeSlider(card);
        UiKit.Anchor((RectTransform)volume.transform, new Vector2(0.5f, 1), new Vector2(0, -116), new Vector2(528, 36), new Vector2(0.5f, 1));
        volume.value = SaveData.Current.volume;
        volumeLabel.text = $"{Mathf.RoundToInt(volume.value * 100)}";
        volume.onValueChanged.AddListener(v =>
        {
            SaveData.Current.SetVolume(v);
            volumeLabel.text = $"{Mathf.RoundToInt(v * 100)}";
        });
        var note = UiKit.Label(card, "음량은 저장되어 다음에도 그대로입니다.", 18, UiKit.Muted, FontStyle.Normal);
        UiKit.Anchor(note.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(560, 30), new Vector2(0.5f, 1));

        var reset = UiKit.Button(card, "진행 초기화", AskReset, false, 240, 60, 22);
        UiKit.Anchor(reset.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(240, 60), new Vector2(0.5f, 0));
    }

    void AskReset()
    {
        Confirm.Show("진행 초기화", UiKit.Ink, "해금한 스테이지와 기록을 모두 지울까요?\n음량 설정은 남습니다.", 0,
            ("지우기", () => { SaveData.ResetProgress(); Confirm.Hide(); ShowPage(MenuPage.Settings); }, true),
            ("취소", () => { Confirm.Hide(); UiKit.Focus(firstFocus[MenuPage.Settings]); }, false));
    }

    Slider MakeSlider(Transform parent)
    {
        var root = new GameObject("Volume", typeof(RectTransform), typeof(Slider)).GetComponent<RectTransform>();
        root.SetParent(parent, false);
        var bg = UiKit.Panel(root, "Background", UiKit.Soft, 0.2f);
        UiKit.Stretch(bg);
        bg.offsetMin = new Vector2(0, 8);
        bg.offsetMax = new Vector2(0, -8);
        var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(root, false);
        UiKit.Stretch(fillArea);
        fillArea.offsetMin = new Vector2(0, 8);
        fillArea.offsetMax = new Vector2(0, -8);
        var fill = UiKit.Panel(fillArea, "Fill", UiKit.Accent, 0.2f);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0, 1);
        fill.sizeDelta = Vector2.zero;
        var handleArea = new GameObject("Handle Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(root, false);
        UiKit.Stretch(handleArea);
        var handle = UiKit.Panel(handleArea, "Handle", Color.white, 0.12f);
        handle.sizeDelta = new Vector2(36, 0);
        handle.gameObject.AddComponent<Outline>().effectColor = new Color(0.2f, 0.5f, 0.85f, 0.8f);
        var s = root.GetComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0;
        s.maxValue = 1;
        return s;
    }

    // ------------------------------------------------------------ 엔딩
    void BuildEnding()
    {
        var page = UiKit.Screen(canvas, "Ending", new Color(0.08f, 0.14f, 0.24f, 0.2f));
        pages[MenuPage.Ending] = page;
        var card = UiKit.CardPanel(page.transform, "Card");
        UiKit.Anchor(card, new Vector2(0, 0.5f), new Vector2(70, 0), new Vector2(600, 420), new Vector2(0, 0.5f));

        var star = UiKit.Icon(card, skin != null ? skin.iconStar : null, 84);
        UiKit.Anchor(star.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(84, 84), new Vector2(0.5f, 1));
        var title = UiKit.Label(card, "모든 스테이지 클리어!", 46, UiKit.Accent);
        UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -116), new Vector2(560, 70), new Vector2(0.5f, 1));
        var body = UiKit.Label(card, "하얀 여우가 보석을 모두 모았어요.\n함께해 줘서 고마워요!", 24, UiKit.Ink, FontStyle.Normal);
        UiKit.Anchor(body.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -190), new Vector2(560, 70), new Vector2(0.5f, 1));
        endingStats = UiKit.Label(card, "", 22, UiKit.Muted, FontStyle.Bold);
        UiKit.Anchor(endingStats.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -262), new Vector2(560, 34), new Vector2(0.5f, 1));
        var home = UiKit.Button(card, "메인으로", () => ShowPage(MenuPage.Main), true, 260, 64);
        UiKit.Anchor(home.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(260, 64), new Vector2(0.5f, 0));
        firstFocus[MenuPage.Ending] = home;
    }

    void RefreshEnding()
    {
        var save = SaveData.Current;
        float total = 0;
        int cleared = 0, gems = 0;
        for (int i = 0; i < StageLibrary.Count; i++)
        {
            var map = StageLibrary.Load(i);
            var best = save.BestTime(SaveData.StageId(map));
            if (best.HasValue) { total += best.Value; cleared++; gems += map.Gems.Count; }
        }
        endingStats.text = $"스테이지 {cleared}개  ·  보석 {gems}개  ·  기록 합계 {UiKit.FormatTime(total)}";
    }

    IEnumerator Celebrate()
    {
        while (true)
        {
            fox.SetTrigger(JumpId);
            yield return new WaitForSecondsRealtime(1.5f);
        }
    }
}
