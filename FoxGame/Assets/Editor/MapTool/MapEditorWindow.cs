using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 맵툴 (메뉴 FoxGame/맵툴). 스테이지 목록 관리, 붓으로 칸 칠하기, 크기·이름, 검증, 저장,
/// 씬에 미리보기, 이 스테이지로 플레이. 스테이지 JSON 은 이 창에서만 고친다.
/// 단축키: 1~8 바닥 붓, 9 보석, 0 시작, H 높이 붓, Q/E 높이 붓 층 내리기·올리기, Ctrl+Z / Ctrl+Y 실행 취소·다시 실행, Ctrl+S 저장.
/// 장치: 레버·누름판·솟는 다리·승강 땅·블록 붓, C 로 채널 1~4 바꾸기 (승강 땅은 높이 붓 층이 켜졌을 때 층).
/// 움직이는 것(눈덩이·부엉이·발판): 오른쪽에서 추가한 뒤 그리드에서 칸을 차례로 눌러 길을 그린다 (Esc / Enter 로 끝).
/// </summary>
public class MapEditorWindow : EditorWindow
{
    struct Brush
    {
        public char symbol;
        public string label;
        public KeyCode key;
    }

    static readonly Brush[] Brushes =
    {
        new() { symbol = MapData.Ground, label = "땅 (지우개)", key = KeyCode.Alpha1 },
        new() { symbol = MapData.Hole, label = "구멍", key = KeyCode.Alpha2 },
        new() { symbol = MapData.Obstacle, label = "장애물", key = KeyCode.Alpha3 },
        new() { symbol = MapData.Ice, label = "얼음", key = KeyCode.Alpha4 },
        new() { symbol = MapData.Crumble, label = "무너지는 칸", key = KeyCode.Alpha5 },
        new() { symbol = MapData.Switch, label = "스위치", key = KeyCode.Alpha6 },
        new() { symbol = MapData.Door, label = "문", key = KeyCode.Alpha7 },
        new() { symbol = MapData.Stairs, label = "계단", key = KeyCode.Alpha8 },
        new() { symbol = MapData.Gem, label = "보석", key = KeyCode.Alpha9 },
        new() { symbol = MapData.Start, label = "시작", key = KeyCode.Alpha0 },
        new() { symbol = FloorBrush, label = "높이", key = KeyCode.H },
        new() { symbol = MapData.Lever, label = "레버", key = KeyCode.None },
        new() { symbol = MapData.Plate, label = "누름판", key = KeyCode.None },
        new() { symbol = MapData.Bridge, label = "솟는 다리", key = KeyCode.None },
        new() { symbol = MapData.Lift, label = "승강 땅", key = KeyCode.None },
        new() { symbol = MapData.Block, label = "미는 블록", key = KeyCode.None },
    };

    static bool IsMechanism(char symbol) =>
        symbol == MapData.Lever || symbol == MapData.Plate || symbol == MapData.Bridge || symbol == MapData.Lift;

    /// <summary>높이 붓을 나타내는 내부 기호 (맵 기호와 겹치지 않음).</summary>
    const char FloorBrush = 'H';

    static readonly Color GrassColor = new(0.36f, 0.62f, 0.3f);
    static readonly Color DirtColor = new(0.55f, 0.36f, 0.2f);
    static readonly Color HoleColor = new(0.13f, 0.18f, 0.25f);
    static readonly Color StoneColor = new(0.62f, 0.64f, 0.7f);
    static readonly Color GemColor = new(0.25f, 0.7f, 1f);
    static readonly Color BadColor = new(0.95f, 0.25f, 0.25f);

    readonly StageFiles files = new();
    List<string> stagePaths = new();
    List<string> stageLabels = new();
    int selected = -1;
    MapEditorSession session;
    char brush = MapData.Obstacle;
    int floorBrush = 2;
    int channel = 1;
    bool painting;
    Vector2Int? hover;
    int newWidth, newHeight;
    Vector2 listScroll, sideScroll;

    [MenuItem("FoxGame/맵툴 %#m", priority = 0)]
    public static MapEditorWindow Open()
    {
        var w = GetWindow<MapEditorWindow>();
        w.titleContent = new GUIContent("맵툴");
        w.minSize = new Vector2(860, 680);
        w.Show();
        return w;
    }

    /// <summary>Unity.exe -projectPath FoxGame -executeMethod MapEditorWindow.OpenOnLaunch : 에디터를 열면서 맵툴도 연다.</summary>
    public static void OpenOnLaunch() => EditorApplication.delayCall += () => Open();

    /// <summary>스테이지 JSON 을 더블클릭하면 텍스트 편집기 대신 맵툴로 연다.</summary>
    [OnOpenAsset]
    static bool OnOpenAsset(EntityId entityId, int line)
    {
        var path = AssetDatabase.GetAssetPath(EditorUtility.EntityIdToObject(entityId));
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".json") ||
            Path.GetDirectoryName(path).Replace('\\', '/') != StageLibrary.AssetFolder)
            return false;
        var w = Open();
        w.Select(w.stagePaths.IndexOf(path));
        return true;
    }

    void OnEnable()
    {
        saveChangesMessage = "저장하지 않은 스테이지 변경이 있습니다. 저장할까요?";
        RefreshList();
        if (selected < 0 && stagePaths.Count > 0) Select(0, force: true);
    }

    void RefreshList()
    {
        stagePaths = files.List();
        stageLabels = new List<string>();
        for (int i = 0; i < stagePaths.Count; i++)
        {
            string label;
            try
            {
                var m = files.Load(stagePaths[i]);
                label = $"{i + 1}. {m.name}  ({m.Width}×{m.Height}{(m.HighestFloor > 1 ? $", {m.HighestFloor}층" : "")})";
            }
            catch (MapFormatException e)
            {
                label = $"{i + 1}. ⚠ 읽기 실패";
                Debug.LogError(e.Message);
            }
            stageLabels.Add(label);
        }
        if (session != null) selected = stagePaths.IndexOf(session.Path);
    }

    // ------------------------------------------------------------ 스테이지 선택 / 파일 작업
    bool ConfirmLeave()
    {
        if (session == null || !session.IsDirty) return true;
        int choice = EditorUtility.DisplayDialogComplex("맵툴",
            $"'{session.Map.name}' 의 변경을 저장할까요?", "저장", "취소", "저장 안 함");
        if (choice == 1) return false;
        if (choice == 0) return SaveCurrent();
        return true;
    }

    public void SelectStage(int index) => Select(index);

    void Select(int index, bool force = false)
    {
        if (index < 0 || index >= stagePaths.Count) return;
        if (!force && index == selected) return;
        if (!force && !ConfirmLeave()) return;
        try
        {
            session = new MapEditorSession(stagePaths[index], files.Load(stagePaths[index]));
            selected = index;
            newWidth = session.Map.Width;
            newHeight = session.Map.Height;
            hasUnsavedChanges = false;
        }
        catch (MapFormatException e)
        {
            EditorUtility.DisplayDialog("맵툴", "스테이지 파일을 읽지 못했습니다.\n\n" + e.Message, "확인");
        }
        GUI.FocusControl(null);
        Repaint();
    }

    bool SaveCurrent()
    {
        if (session == null) return true;
        if (!session.Validation.IsValid)
        {
            EditorUtility.DisplayDialog("맵툴", "검증을 통과하지 못해 저장할 수 없습니다.\n\n" +
                string.Join("\n", session.Validation.errors), "확인");
            return false;
        }
        files.Save(session.Path, session.Map);
        session.MarkSaved(session.Path);
        hasUnsavedChanges = false;
        RefreshList();
        ShowNotification(new GUIContent("저장했습니다"), 1.0);
        return true;
    }

    public override void SaveChanges()
    {
        if (SaveCurrent()) base.SaveChanges();
    }

    void CreateStage(MapData from)
    {
        if (!ConfirmLeave()) return;
        var map = from != null ? from.Clone() : MapData.CreateEmpty(4, 4);
        if (from != null)
        {
            map.name = from.name + " 복사본";
            map.id = null;   // 복사본은 새 기록
        }
        else map.Set(new Vector2Int(3, 3), MapData.Gem);   // 바로 저장할 수 있게 보석 하나
        var path = files.Create(map);
        RefreshList();
        Select(stagePaths.IndexOf(path), force: true);
    }

    void DeleteStage()
    {
        if (session == null) return;
        if (!EditorUtility.DisplayDialog("맵툴", $"스테이지 {selected + 1} '{session.Map.name}' 을(를) 삭제할까요?\n되돌릴 수 없습니다.", "삭제", "취소"))
            return;
        int index = selected;
        files.Delete(index);
        session = null;
        selected = -1;
        RefreshList();
        if (stagePaths.Count > 0) Select(Mathf.Min(index, stagePaths.Count - 1), force: true);
    }

    void MoveStage(int delta)
    {
        if (session == null || !ConfirmLeave()) return;
        int to = files.Move(selected, delta);
        RefreshList();
        Select(to, force: true);
    }

    // ------------------------------------------------------------ 씬 미리보기 / 플레이
    void PreviewInScene()
    {
        var board = FindAnyObjectByType<Board>();
        if (board == null)
        {
            EditorUtility.DisplayDialog("맵툴", "열린 씬에 Board 가 없습니다. Assets/Scenes/Main.unity 를 열어 주세요.", "확인");
            return;
        }
        board.Build(session.Map.Clone());
        var fit = Camera.main != null ? Camera.main.GetComponent<CameraFit>() : null;
        if (fit != null) fit.Fit(board);
        var fox = FindAnyObjectByType<FoxController>();
        if (fox != null) fox.transform.position = board.CellToWorld(session.Map.StartCell);
        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void PlayStage()
    {
        if (session.IsDirty && !SaveCurrent()) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetInt(StageProgress.PlayStageKey, selected);
        EditorApplication.isPlaying = true;
    }

    // ------------------------------------------------------------ 그리기
    void OnGUI()
    {
        HandleShortcuts();
        if (session != null) hasUnsavedChanges = session.IsDirty;

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawLeft();
            DrawGrid();
            DrawRight();
        }
    }

    void DrawLeft()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(210)))
        {
            GUILayout.Label("스테이지", EditorStyles.boldLabel);
            using (var s = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.ExpandHeight(true)))
            {
                listScroll = s.scrollPosition;
                for (int i = 0; i < stagePaths.Count; i++)
                {
                    bool on = i == selected;
                    string label = stageLabels[i] + (on && session != null && session.IsDirty ? " *" : "");
                    if (GUILayout.Toggle(on, label, "Button", GUILayout.Height(24)) && !on) Select(i);
                }
                if (stagePaths.Count == 0) EditorGUILayout.HelpBox("스테이지가 없습니다. '새 스테이지'를 눌러 만드세요.", MessageType.Info);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("새 스테이지")) CreateStage(null);
                using (new EditorGUI.DisabledScope(session == null))
                    if (GUILayout.Button("복제")) CreateStage(session.Map);
            }
            using (new EditorGUI.DisabledScope(session == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selected <= 0))
                    if (GUILayout.Button("▲ 앞으로")) MoveStage(-1);
                using (new EditorGUI.DisabledScope(selected >= stagePaths.Count - 1))
                    if (GUILayout.Button("▼ 뒤로")) MoveStage(1);
                if (GUILayout.Button("삭제")) DeleteStage();
            }

            GUILayout.Space(10);
            GUILayout.Label("붓", EditorStyles.boldLabel);
            foreach (var b in Brushes)
            {
                var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
                bool on = brush == b.symbol;
                if (GUI.Toggle(r, on, "", "Button") && !on) brush = b.symbol;
                if (b.symbol == FloorBrush) DrawFloorIcon(new Rect(r.x + 6, r.y + 4, 18, 18), floorBrush);
                else if (b.symbol == MapData.Gem || b.symbol == MapData.Start || b.symbol == MapData.Block)
                {
                    DrawSymbol(new Rect(r.x + 6, r.y + 4, 18, 18), MapData.Ground, 1, false);
                    DrawItem(new Rect(r.x + 6, r.y + 4, 18, 18), b.symbol, false);
                }
                else DrawSymbol(new Rect(r.x + 6, r.y + 4, 18, 18), b.symbol, 1, false);
                if (IsMechanism(b.symbol)) Outline(new Rect(r.x + 6, r.y + 4, 18, 18), Board.ChannelColor(channel), 2);
                string label = b.symbol == FloorBrush ? $"높이 {floorBrush}층"
                    : b.symbol == MapData.Lift ? $"승강 땅 (켜지면 {floorBrush}층)"
                    : IsMechanism(b.symbol) ? $"{b.label} · 채널 {channel}" : b.label;
                GUI.Label(new Rect(r.x + 32, r.y + 4, r.width - 60, 18), label, on ? EditorStyles.boldLabel : EditorStyles.label);
                GUI.Label(new Rect(r.xMax - 22, r.y + 4, 18, 18), KeyLabel(b.key), EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("층", GUILayout.Width(18));
                int f = EditorGUILayout.IntSlider(floorBrush, MapData.MinFloor, MapData.MaxFloor);
                if (f != floorBrush) { floorBrush = f; brush = FloorBrush; }
            }
            EditorGUILayout.LabelField("Q / E: 층 내리기 / 올리기", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("채널", GUILayout.Width(30));
                for (int ch = 1; ch <= MapData.MaxChannel; ch++)
                {
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = Board.ChannelColor(ch);
                    if (GUILayout.Toggle(channel == ch, ch.ToString(), "Button", GUILayout.Height(22)) && channel != ch) channel = ch;
                    GUI.backgroundColor = prev;
                }
            }
            EditorGUILayout.LabelField("C: 채널 바꾸기 · 같은 색 장치가 같은 색 땅을 바꿉니다", EditorStyles.miniLabel);
            GUILayout.Space(6);
        }
    }

    void DrawGrid()
    {
        var area = GUILayoutUtility.GetRect(200, 10000, 200, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(area, new Color(0.17f, 0.19f, 0.22f));
        if (session == null) return;

        var map = session.Map;
        float cell = Mathf.Min((area.width - 40) / map.Width, (area.height - 70) / map.Height, 64);
        var size = new Vector2(cell * map.Width, cell * map.Height);
        var origin = new Vector2(area.x + (area.width - size.x) / 2, area.y + (area.height - size.y) / 2 - 10);
        var unreachable = new HashSet<Vector2Int>(session.Validation.unreachableGems);

        hover = null;
        var e = Event.current;
        Vector2 Center(Vector2Int c) => new(origin.x + (c.x + 0.5f) * cell, origin.y + (map.Height - 1 - c.y + 0.5f) * cell);
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var c = new Vector2Int(x, y);
            // y=0 이 화면 아래(카메라 쪽)
            var r = new Rect(origin.x + x * cell + 2, origin.y + (map.Height - 1 - y) * cell + 2, cell - 4, cell - 4);
            DrawSymbol(r, map.SymbolAt(c), map.FloorAt(c), true);
            DrawItem(r, map.ItemAt(c), true);
            if (map.TypeAt(c) != CellType.Hole && (map.FloorAt(c) > 1 || brush == FloorBrush))
                GUI.Label(new Rect(r.x + 3, r.y + 1, r.width, 16), $"{map.FloorAt(c)}층", FloorLabel());
            if (MapData.HasChannel(map.TypeAt(c)) && map.ChannelAt(c) > 0)
            {
                Outline(r, Board.ChannelColor(map.ChannelAt(c)), Mathf.Max(2, cell / 22f));
                if (map.TypeAt(c) == CellType.Lift)
                    GUI.Label(new Rect(r.x, r.yMax - 18, r.width - 3, 16), $"{map.FloorAt(c)}→{map.FloorOnAt(c)}층", RightMini());
            }
            if (unreachable.Contains(c)) Outline(r, BadColor, 3);
            if (r.Contains(e.mousePosition))
            {
                hover = c;
                Outline(r, Color.white, 2);
            }
        }

        DrawMoverPaths(map, Center, cell);

        string brushText = brush == FloorBrush ? $"높이 {floorBrush}층" : BrushLabel(brush);
        if (session.Drawing >= 0) brushText = $"{MoverLabel(map.movers[session.Drawing].kind)} 길 그리기 (이웃 칸을 차례로, 마지막 칸 다시 누르면 지움, Esc 끝)";
        string info = hover is { } h ? $"x {h.x}, y {h.y}, {map.FloorAt(h)}층   ·   {brushText}" : $"{brushText}   ·   누르거나 끌어서 칠하기";
        GUI.Label(new Rect(area.x, origin.y + size.y + 10, area.width, 20), info, CenteredMini());
        GUI.Label(new Rect(area.x, area.y + 6, area.width, 20), "↑ 먼 쪽", CenteredMini());
        GUI.Label(new Rect(area.x, origin.y + size.y + 28, area.width, 20), "↓ 카메라 쪽", CenteredMini());

        // 길 그리기 중에는 누를 때마다 길에 칸을 더한다
        if (session.Drawing >= 0)
        {
            if (hover is { } pc && e.button == 0 && e.type == EventType.MouseDown)
            {
                session.ClickPath(pc);
                e.Use();
            }
            if (e.type == EventType.MouseMove) Repaint();
            return;
        }

        // 칠하기: 누르면 실행 취소 기록 1번, 끄는 동안 계속 칠함
        if (hover is { } target && e.button == 0)
        {
            if (e.type == EventType.MouseDown)
            {
                session.RecordUndo();
                painting = true;
                PaintAt(target);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && painting)
            {
                PaintAt(target);
                e.Use();
            }
        }
        if (e.type == EventType.MouseUp) painting = false;
        if (e.type == EventType.MouseMove) Repaint();
    }

    void PaintAt(Vector2Int cell)
    {
        if (brush == FloorBrush) session.PaintFloor(cell, floorBrush);
        else if (IsMechanism(brush)) session.PaintMechanism(cell, brush, channel, floorBrush);
        else session.Paint(cell, brush);
    }

    void DrawRight()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
        using (var s = new EditorGUILayout.ScrollViewScope(sideScroll))
        {
            sideScroll = s.scrollPosition;
            if (session == null)
            {
                EditorGUILayout.HelpBox("왼쪽에서 스테이지를 고르거나 새로 만드세요.", MessageType.Info);
                return;
            }
            var map = session.Map;
            GUILayout.Label($"스테이지 {selected + 1}  ·  {Path.GetFileName(session.Path)}{(session.IsDirty ? "  (저장 안 됨)" : "")}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("이름", map.name);
            if (EditorGUI.EndChangeCheck())
            {
                session.RecordUndo();
                session.Rename(name);
            }

            GUILayout.Space(6);
            GUILayout.Label($"크기 (가로 × 세로, {MapData.MinSize}~{MapData.MaxSize})", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newWidth = Mathf.Clamp(EditorGUILayout.IntField(newWidth, GUILayout.Width(50)), MapData.MinSize, MapData.MaxSize);
                GUILayout.Label("×", GUILayout.Width(12));
                newHeight = Mathf.Clamp(EditorGUILayout.IntField(newHeight, GUILayout.Width(50)), MapData.MinSize, MapData.MaxSize);
                using (new EditorGUI.DisabledScope(newWidth == map.Width && newHeight == map.Height))
                    if (GUILayout.Button("크기 적용")) session.Resize(newWidth, newHeight);
            }
            EditorGUILayout.LabelField("왼쪽 아래(카메라 쪽)를 기준으로 늘리거나 자릅니다.", EditorStyles.miniLabel);

            GUILayout.Space(8);
            DrawMoverList(map);

            GUILayout.Space(8);
            GUILayout.Label("검증", EditorStyles.boldLabel);
            var v = session.Validation;
            EditorGUILayout.HelpBox($"보석 {map.Gems.Count}개 · 시작 {map.Find(MapData.Start).Count}개 · 가장 높은 칸 {map.HighestFloor}층", MessageType.None);
            if (v.IsValid) EditorGUILayout.HelpBox("모든 보석에 갈 수 있습니다. 저장할 수 있습니다.", MessageType.Info);
            foreach (var err in v.errors) EditorGUILayout.HelpBox(err, MessageType.Error);
            foreach (var warn in v.warnings) EditorGUILayout.HelpBox(warn, MessageType.Warning);

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!session.CanUndo))
                    if (GUILayout.Button("실행 취소")) session.Undo();
                using (new EditorGUI.DisabledScope(!session.CanRedo))
                    if (GUILayout.Button("다시 실행")) session.Redo();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!session.IsDirty))
                    if (GUILayout.Button("되돌리기")) session.Revert();
                using (new EditorGUI.DisabledScope(!session.IsDirty || !v.IsValid))
                    if (GUILayout.Button("저장", GUILayout.Height(28))) SaveCurrent();
            }
            GUILayout.Space(8);
            if (GUILayout.Button("씬에 미리보기")) PreviewInScene();
            using (new EditorGUI.DisabledScope(!v.IsValid || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("이 스테이지로 플레이 ▶", GUILayout.Height(30))) PlayStage();

            GUILayout.Space(8);
            EditorGUILayout.HelpBox("단축키: 1~8 바닥 · 9 보석 · 0 시작 · H 높이 · Q/E 층 내리기/올리기 · C 채널 · Ctrl+Z 실행 취소 · Ctrl+Y 다시 실행 · Ctrl+S 저장", MessageType.None);
            EditorGUILayout.HelpBox("장치: 레버는 부딪혀 당기고(켜짐↔꺼짐), 누름판은 여우·블록이 올라가 있는 동안 켜집니다. 같은 채널의 솟는 다리는 켜지면 땅, 승강 땅은 켜지면 정한 층이 됩니다. 블록은 밀 수 있고 구멍에 밀면 메웁니다.", MessageType.None);
            EditorGUILayout.HelpBox($"이동 규칙: 걸어서 {MoveRules.WalkClimb}층, 점프로 바로 앞 칸 {MoveRules.JumpClimb}층까지 뛰어오를 수 있고, 내려가는 건 몇 층이든 됩니다. 앞 칸이 높지 않으면 점프는 두 칸 앞으로 갑니다.", MessageType.None);
        }
    }

    void HandleShortcuts()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || session == null) return;
        if (EditorGUIUtility.editingTextField) return;
        bool ctrl = e.control || e.command;
        if (session.Drawing >= 0 && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            session.EndDrawing();
            e.Use();
            return;
        }
        if (ctrl && e.keyCode == KeyCode.Z) { session.Undo(); e.Use(); }
        else if (ctrl && e.keyCode == KeyCode.Y) { session.Redo(); e.Use(); }
        else if (ctrl && e.keyCode == KeyCode.S) { if (session.IsDirty) SaveCurrent(); e.Use(); }
        else if (!ctrl && e.keyCode == KeyCode.Q) { floorBrush = Mathf.Max(MapData.MinFloor, floorBrush - 1); if (brush != MapData.Lift) brush = FloorBrush; e.Use(); }
        else if (!ctrl && e.keyCode == KeyCode.E) { floorBrush = Mathf.Min(MapData.MaxFloor, floorBrush + 1); if (brush != MapData.Lift) brush = FloorBrush; e.Use(); }
        else if (!ctrl && e.keyCode == KeyCode.C) { channel = channel % MapData.MaxChannel + 1; e.Use(); }
        else if (!ctrl)
            foreach (var b in Brushes)
                if (b.key != KeyCode.None && e.keyCode == b.key) { brush = b.symbol; e.Use(); }
    }

    // ------------------------------------------------------------ 움직이는 것
    static readonly Color SnowballColor = new(0.86f, 0.93f, 1f);
    static readonly Color OwlColor = new(0.9f, 0.62f, 0.32f);
    static readonly Color PlatformColor = new(0.35f, 0.85f, 1f);

    static Color MoverColor(string kind) => kind == Mover.Owl ? OwlColor : kind == Mover.Platform ? PlatformColor : SnowballColor;
    static string MoverLabel(string kind) => kind == Mover.Owl ? "부엉이" : kind == Mover.Platform ? "발판" : "눈덩이";

    void DrawMoverList(MapData map)
    {
        GUILayout.Label("움직이는 것 (한 턴에 한 칸, 길을 왕복)", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(session.Drawing >= 0))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 눈덩이")) session.AddMover(Mover.Snowball);
            if (GUILayout.Button("+ 부엉이")) session.AddMover(Mover.Owl);
            if (GUILayout.Button("+ 발판")) session.AddMover(Mover.Platform);
        }
        for (int i = 0; i < map.movers.Count; i++)
        {
            var m = map.movers[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                var r = GUILayoutUtility.GetRect(14, 18, GUILayout.Width(14));
                EditorGUI.DrawRect(new Rect(r.x, r.y + 3, 12, 12), MoverColor(m.kind));
                string where = m.path.Count > 0 ? $"({m.path[0].x},{m.path[0].y}) 부터 {m.path.Count}칸" : "길 없음";
                GUILayout.Label($"{i + 1}. {MoverLabel(m.kind)} · {where}", session.Drawing == i ? EditorStyles.boldLabel : EditorStyles.label);
                if (session.Drawing == i)
                {
                    if (GUILayout.Button("완료", GUILayout.Width(44))) session.EndDrawing();
                }
                else using (new EditorGUI.DisabledScope(session.Drawing >= 0))
                {
                    if (GUILayout.Button("길", GUILayout.Width(30))) session.BeginDrawing(i);
                    if (GUILayout.Button("삭제", GUILayout.Width(40))) session.RemoveMover(i);
                }
            }
        }
        if (session.Drawing >= 0)
            EditorGUILayout.HelpBox("그리드에서 출발 칸부터 이웃 칸을 차례로 누르세요. 마지막 칸을 다시 누르면 지웁니다. Esc 나 Enter, '완료' 로 끝냅니다.", MessageType.Info);
        else if (map.movers.Count == 0)
            EditorGUILayout.LabelField("눈덩이: 땅 위를 굴러 다님 · 부엉이: 구멍 위도 날아 다님 · 발판: 구멍 위를 오가며 여우를 태움", EditorStyles.wordWrappedMiniLabel);
    }

    /// <summary>길: 색 선 + 출발 칸에 큰 점, 끝 칸에 작은 점. 그리는 중인 길은 더 굵게.</summary>
    void DrawMoverPaths(MapData map, System.Func<Vector2Int, Vector2> center, float cell)
    {
        if (Event.current.type != EventType.Repaint) return;
        for (int i = 0; i < map.movers.Count; i++)
        {
            var m = map.movers[i];
            if (m.path.Count == 0) continue;
            var color = MoverColor(m.kind);
            bool active = session.Drawing == i;
            // 같은 칸을 지나는 길이 겹치지 않게 조금씩 비켜 그림
            var shift = new Vector2(i % 3 - 1, (i / 3) % 3 - 1) * cell * 0.07f;
            var pts = new Vector3[m.path.Count];
            for (int k = 0; k < pts.Length; k++) pts[k] = center(m.path[k]) + shift;
            Handles.color = color;
            if (pts.Length > 1) Handles.DrawAAPolyLine(active ? 7 : 4, pts);
            float big = cell * 0.16f, small = cell * 0.08f;
            EditorGUI.DrawRect(new Rect(pts[0].x - big / 2, pts[0].y - big / 2, big, big), color);
            Outline(new Rect(pts[0].x - big / 2, pts[0].y - big / 2, big, big), Color.black, 1);
            if (pts.Length > 1) EditorGUI.DrawRect(new Rect(pts[^1].x - small / 2, pts[^1].y - small / 2, small, small), color);
            var label = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            label.normal.textColor = Color.black;
            GUI.Label(new Rect(pts[0].x - 20, pts[0].y + big / 2, 40, 14), MoverLabel(m.kind), label);
        }
    }

    // ------------------------------------------------------------ 칸 그림
    static readonly Color IceColor = new(0.62f, 0.85f, 0.98f);
    static readonly Color CrackColor = new(0.32f, 0.2f, 0.1f);
    static readonly Color SwitchColor = new(0.95f, 0.45f, 0.62f);
    static readonly Color DoorColor = new(0.55f, 0.48f, 0.9f);

    /// <summary>바닥 그림. 높은 층일수록 잔디가 밝아지고 아래 흙 띠가 두꺼워진다.</summary>
    static readonly Color WoodColor = new(0.72f, 0.5f, 0.3f);

    static void DrawSymbol(Rect r, char symbol, int floor, bool large)
    {
        if (symbol == MapData.Bridge)
        {
            // 꺼지면 구멍, 켜지면 솟는 돌기둥: 구멍 위에 점선 네모
            EditorGUI.DrawRect(r, HoleColor);
            float u0 = Mathf.Max(1, r.width / 24f);
            for (int i = 0; i < 4; i++)
            {
                float k = 0.12f + i * 0.2f;
                EditorGUI.DrawRect(new Rect(r.x + r.width * k, r.y + r.height * 0.15f, r.width * 0.12f, u0 * 1.5f), StoneColor);
                EditorGUI.DrawRect(new Rect(r.x + r.width * k, r.y + r.height * 0.85f - u0 * 1.5f, r.width * 0.12f, u0 * 1.5f), StoneColor);
                EditorGUI.DrawRect(new Rect(r.x + r.width * 0.15f, r.y + r.height * k, u0 * 1.5f, r.height * 0.12f), StoneColor);
                EditorGUI.DrawRect(new Rect(r.x + r.width * 0.85f - u0 * 1.5f, r.y + r.height * k, u0 * 1.5f, r.height * 0.12f), StoneColor);
            }
            if (large) GUI.Label(r, "다리", CenteredMini());
            return;
        }
        if (symbol == MapData.Hole)
        {
            EditorGUI.DrawRect(r, HoleColor);
            Outline(r, new Color(0.35f, 0.55f, 0.75f), 1);
            if (large) GUI.Label(r, "구멍", CenteredMini());
            return;
        }
        float lift = (floor - 1) / (float)(MapData.MaxFloor - 1);
        var top = symbol == MapData.Ice ? IceColor : GrassColor;
        EditorGUI.DrawRect(r, Color.Lerp(top, new Color(0.88f, 0.96f, 0.75f), lift * 1.6f));
        float dirt = Mathf.Max(2, r.height * (0.1f + 0.05f * (floor - 1)));
        dirt = Mathf.Min(dirt, r.height * 0.45f);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - dirt, r.width, dirt), DirtColor);
        var inner = new Rect(r.x + r.width * 0.22f, r.y + r.height * 0.18f, r.width * 0.56f, r.height * 0.56f);
        float u = Mathf.Max(1, r.width / 24f);   // 선 두께
        switch (symbol)
        {
            case MapData.Obstacle:
                EditorGUI.DrawRect(inner, StoneColor);
                break;
            case MapData.Ice:
                // 반짝이는 줄 두 개
                EditorGUI.DrawRect(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.28f, r.width * 0.35f, u), Color.white);
                EditorGUI.DrawRect(new Rect(r.x + r.width * 0.45f, r.y + r.height * 0.5f, r.width * 0.35f, u), Color.white);
                break;
            case MapData.Crumble:
                // 금: 지그재그 선
                for (int i = 0; i < 4; i++)
                    EditorGUI.DrawRect(new Rect(r.x + r.width * (0.18f + 0.16f * i), r.y + r.height * (i % 2 == 0 ? 0.25f : 0.45f), r.width * 0.18f, u * 1.4f), CrackColor);
                EditorGUI.DrawRect(new Rect(r.x + r.width * 0.5f, r.y + r.height * 0.3f, u * 1.4f, r.height * 0.35f), CrackColor);
                break;
            case MapData.Switch:
                EditorGUI.DrawRect(new Rect(inner.x + inner.width * 0.1f, inner.y + inner.height * 0.1f, inner.width * 0.8f, inner.height * 0.8f), StoneColor);
                EditorGUI.DrawRect(new Rect(inner.x + inner.width * 0.25f, inner.y + inner.height * 0.25f, inner.width * 0.5f, inner.height * 0.5f), SwitchColor);
                break;
            case MapData.Door:
                for (int i = 0; i < 3; i++)
                    EditorGUI.DrawRect(new Rect(inner.x + inner.width * (0.05f + 0.35f * i), inner.y, inner.width * 0.22f, inner.height), DoorColor);
                EditorGUI.DrawRect(new Rect(inner.x, inner.y, inner.width, inner.height * 0.15f), DoorColor);
                break;
            case MapData.Lever:
                EditorGUI.DrawRect(new Rect(inner.x, inner.y + inner.height * 0.7f, inner.width, inner.height * 0.3f), StoneColor);
                var lm = GUI.matrix;
                GUIUtility.RotateAroundPivot(25, new Vector2(inner.center.x, inner.y + inner.height * 0.75f));
                EditorGUI.DrawRect(new Rect(inner.center.x - u, inner.y + inner.height * 0.1f, u * 2, inner.height * 0.65f), WoodColor);
                GUI.matrix = lm;
                EditorGUI.DrawRect(new Rect(inner.center.x + inner.width * 0.1f, inner.y, inner.width * 0.25f, inner.height * 0.25f), Color.white);
                break;
            case MapData.Plate:
                EditorGUI.DrawRect(new Rect(inner.x, inner.y + inner.height * 0.2f, inner.width, inner.height * 0.6f), StoneColor);
                EditorGUI.DrawRect(new Rect(inner.x + inner.width * 0.15f, inner.y + inner.height * 0.3f, inner.width * 0.7f, inner.height * 0.4f), Color.white);
                break;
            case MapData.Lift:
                // 위아래 화살표
                EditorGUI.DrawRect(new Rect(inner.center.x - u, inner.y, u * 2, inner.height), StoneColor);
                for (int i = 0; i < 3; i++)
                {
                    float w = inner.width * (0.15f + 0.15f * i);
                    EditorGUI.DrawRect(new Rect(inner.center.x - w / 2, inner.y + u * 2 * i, w, u * 1.6f), StoneColor);
                    EditorGUI.DrawRect(new Rect(inner.center.x - w / 2, inner.yMax - u * 2 * (i + 1), w, u * 1.6f), StoneColor);
                }
                break;
            case MapData.Stairs:
                for (int i = 0; i < 3; i++)
                    EditorGUI.DrawRect(new Rect(inner.x + inner.width * 0.34f * i, inner.y + inner.height * (0.66f - 0.33f * i), inner.width * 0.33f, inner.height * (0.34f + 0.33f * i)), StoneColor);
                break;
        }
    }

    /// <summary>바닥 위에 겹쳐 그리는 물건 (보석·시작).</summary>
    static void DrawItem(Rect r, char item, bool large)
    {
        var inner = new Rect(r.x + r.width * 0.22f, r.y + r.height * 0.18f, r.width * 0.56f, r.height * 0.56f);
        switch (item)
        {
            case MapData.Gem:
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45, inner.center);
                var d = new Rect(inner.center.x - inner.width * 0.3f, inner.center.y - inner.height * 0.3f, inner.width * 0.6f, inner.height * 0.6f);
                EditorGUI.DrawRect(d, GemColor);
                Outline(d, Color.white, Mathf.Max(1, r.width / 40f));
                GUI.matrix = m;
                break;
            case MapData.Block:
                EditorGUI.DrawRect(inner, WoodColor);
                float bu = Mathf.Max(1, r.width / 30f);
                EditorGUI.DrawRect(new Rect(inner.x, inner.y + inner.height * 0.33f, inner.width, bu), DirtColor);
                EditorGUI.DrawRect(new Rect(inner.x, inner.y + inner.height * 0.66f, inner.width, bu), DirtColor);
                EditorGUI.DrawRect(new Rect(inner.x, inner.y, inner.width, inner.height * 0.15f), Color.white);
                Outline(inner, DirtColor, bu);
                break;
            case MapData.Start:
                var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = large ? Mathf.RoundToInt(r.height * 0.3f) : 11 };
                style.normal.textColor = Color.white;
                var shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0, 0, 0, 0.5f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), large ? "시작" : "S", shadow);
                GUI.Label(r, large ? "시작" : "S", style);
                break;
        }
    }

    static GUIStyle rightMini;
    static GUIStyle RightMini()
    {
        if (rightMini == null)
        {
            rightMini = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.LowerRight };
            rightMini.normal.textColor = new Color(0.1f, 0.1f, 0.2f);
        }
        return rightMini;
    }

    static string KeyLabel(KeyCode k) => k == KeyCode.None ? "" :
        k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9 ? ((int)k - (int)KeyCode.Alpha0).ToString() : k.ToString();

    /// <summary>높이 붓 아이콘: 계단 모양 블록 + 층 숫자.</summary>
    static void DrawFloorIcon(Rect r, int floor)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y + r.height * 0.55f, r.width * 0.34f, r.height * 0.45f), DirtColor);
        EditorGUI.DrawRect(new Rect(r.x + r.width * 0.33f, r.y + r.height * 0.28f, r.width * 0.34f, r.height * 0.72f), DirtColor);
        EditorGUI.DrawRect(new Rect(r.x + r.width * 0.66f, r.y, r.width * 0.34f, r.height), DirtColor);
        EditorGUI.DrawRect(new Rect(r.x + r.width * 0.66f, r.y, r.width * 0.34f, r.height * 0.2f), GrassColor);
    }

    static GUIStyle floorLabel;
    static GUIStyle FloorLabel()
    {
        if (floorLabel == null)
        {
            floorLabel = new GUIStyle(EditorStyles.miniBoldLabel);
            floorLabel.normal.textColor = new Color(0.1f, 0.18f, 0.08f);
        }
        return floorLabel;
    }

    static void Outline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    static GUIStyle centeredMini;
    static GUIStyle CenteredMini()
    {
        if (centeredMini == null)
        {
            centeredMini = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            centeredMini.normal.textColor = new Color(0.82f, 0.87f, 0.92f);   // 어두운 그리드 배경 위
        }
        return centeredMini;
    }

    static string BrushLabel(char symbol)
    {
        foreach (var b in Brushes) if (b.symbol == symbol) return b.label;
        return symbol.ToString();
    }
}
