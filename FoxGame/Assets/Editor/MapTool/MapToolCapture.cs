using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 확인용: 맵툴 창을 화면 앞에 크게 띄운 뒤, 외부 캡처가 끝날 때까지 기다렸다가 에디터를 닫는다.
/// Unity.exe -projectPath FoxGame -executeMethod MapToolCapture.Run   (batchmode 아님: 창을 그려야 함)
/// Logs/maptool_ready 파일이 생기면 캡처하고, Logs/maptool_done 파일을 만들면 종료한다.
/// </summary>
public static class MapToolCapture
{
    public static void Run()
    {
        string logs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
        string ready = Path.Combine(logs, "maptool_ready"), done = Path.Combine(logs, "maptool_done");
        File.Delete(ready);
        File.Delete(done);

        var w = MapEditorWindow.Open();
        w.position = new Rect(60, 60, 1180, 680);
        w.SelectStage(7);   // 스위치·문이 있는 스테이지 8
        int frames = 0;
        double quitAt = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Tick;

        void Tick()
        {
            w.Focus();
            w.Repaint();
            if (++frames == 240)
            {
                // 캡처할 영역 (화면 픽셀): x,y,w,h
                float ppp = EditorGUIUtility.pixelsPerPoint;
                var r = w.position;
                File.WriteAllText(ready, $"{(int)(r.x * ppp)},{(int)((r.y - 24) * ppp)},{(int)(r.width * ppp)},{(int)((r.height + 24) * ppp)}");
            }
            if (File.Exists(done) || EditorApplication.timeSinceStartup > quitAt)
            {
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
        }
    }
}
