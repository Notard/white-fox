# White Fox 전용 Unity MCP

AI(Claude Code)가 Unity 에디터에서 White Fox 를 직접 켜고, 보고, 플레이하는 도구입니다. 기성 Unity MCP 패키지를 쓰지 않고 이 게임에 필요한 것만 직접 만들었습니다.

```
Claude Code ──stdio──▶ tools/UnityMcp (.NET 콘솔 MCP 서버) ──TCP 127.0.0.1:7801──▶ FoxGame/Assets/Editor/Mcp (에디터 브리지)
```

## 여는 도구

| 도구 | 하는 일 | 연결된 코드 |
| --- | --- | --- |
| `play_mode` | Play 켜기·끄기·상태 (켤 때는 브리지가 다시 응답할 때까지 기다림) | `EditorApplication.isPlaying` |
| `load_stage` | 스테이지 열기 (1부터), 시작 연출이 끝나면 상태를 돌려줌 | `SceneFlow.PlayStage` |
| `get_game_state` | 스테이지·상태·턴·보석, 여우 위치·방향, 칸마다 바닥·층·위에 있는 것의 글자 격자, 적·발판의 다음 턴 위치 | `GameManager`, `Board.State`, `MoveRules.Floor` |
| `act` | w a s d / jump / wait 한 행동. 턴이 끝날 때까지 기다린 뒤 결과와 새 상태 | `FoxController.TryMove / TryJump / TryWait` |
| `read_console` | 최근 에러·경고 (원하면 일반 로그도) | `Application.logMessageReceived` |
| `screenshot` | 게임 화면 + UI 를 PNG 이미지로 (AI 가 화면을 직접 봄) | 테스트의 `TestUtil.Capture` 와 같은 방식 |

## 일부러 열지 않은 것

| 열지 않은 것 | 이유 |
| --- | --- |
| 여우 위치를 직접 바꾸기 | AI 도 플레이어와 같은 입구(키 입력)만 쓴다. 순간이동이 되면 플레이 결과를 믿을 수 없다 |
| 스테이지 파일 저장, 맵툴 쓰기 | 원본 데이터는 사람이 저장한다 |
| 진행 초기화, 실제 저장 기록 | MCP 로 연 스테이지는 테스트 저장 키(`FoxGame.Save.Mcp`)를 쓴다 |
| 씬 저장, 빌드, 임의 코드 실행 | 에이전트가 에디터에서 무엇이든 할 수 있게 된다 |
| 다른 PC 에서 접속 | 브리지는 127.0.0.1 에서만 듣는다 |

## 설치

```powershell
dotnet publish tools/UnityMcp -c Release -o tools/UnityMcp/bin/publish
```

저장소 루트의 `.mcp.json` 에 `unity` 서버가 등록되어 있습니다. 저장소 루트에서 Claude Code 를 켜고 `/mcp` 로 도구 6개가 보이는지 확인합니다. Unity 에디터에 FoxGame 을 열어 두면 브리지가 자동으로 켜집니다 (`[InitializeOnLoad]`).

## 검사

- `dotnet test tools/UnityMcp.Tests`: Unity 없이 가짜 브리지로 MCP 서버 검사 6개 (명령 전달, 한글, 잘못된 행동 거절, 브리지 없을 때 죽지 않음, Play 전환 기다리기, 이미지 반환).
- 실제 에디터 확인 (2026-11-03, Unity 6000.5.7f1): stdio 로 서버를 띄워 `play_mode start` → `load_stage 1` → `act` 14번으로 스테이지 1 클리어 (4/4, 14턴). 그동안 콘솔 에러·경고 없음, 실제 저장 키는 바뀌지 않음.

## 구조에서 지킨 것

- 받기는 백그라운드 스레드, Unity API 는 `EditorApplication.update`(메인 스레드)에서만.
- 명령마다 새 연결. Play 에 들어가면 도메인이 다시 불려 브리지가 재시작되기 때문.
- 도메인 리로드 직전에 리스너를 닫는다 (`AssemblyReloadEvents.beforeAssemblyReload`).
- MCP 서버는 stdout 에 아무것도 쓰지 않는다 (MCP 통신 전용). 로그는 stderr.
- 브리지가 꺼져 있어도 서버는 죽지 않고 이유를 문장으로 돌려준다.

## 다음에 더할 수 있는 도구

- `validate_stage(n)`: `MapValidator` 결과 (먹을 수 없는 보석).
- `solve_stage(n)`: 최단 풀이의 수와 행동 순서. 16개 스테이지 난이도 곡선을 숫자로.
- `run_tests`: EditMode·PlayMode 테스트 100개 실행과 결과 요약.
