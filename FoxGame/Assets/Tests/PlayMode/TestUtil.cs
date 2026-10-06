using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>PlayMode 테스트 공용: 테스트용 저장 키, Ready 생략, 씬 전환 대기, 화면 캡처.</summary>
public static class TestUtil
{
    /// <summary>실제 진행을 건드리지 않도록 테스트 저장 키로 바꾸고 비운다. Ready 는 생략.</summary>
    public static void FreshSave()
    {
        SaveData.UseKey("FoxGame.Save.Test");
        SaveData.ResetProgress();
        GameManager.ReadyDuration = 0;
        Time.timeScale = 1;
    }

    /// <summary>지정한 씬이 활성화되고 한 프레임 더 지날 때까지 기다린다.</summary>
    public static IEnumerator WaitForScene(string name, float timeout = 5f)
    {
        float t = 0;
        while (SceneManager.GetActiveScene().name != name && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;
        yield return null;
    }

    /// <summary>UI(오버레이 캔버스)까지 포함해 메인 카메라 화면을 Screenshots/파일 로 저장.</summary>
    public static void Capture(string file)
    {
        var cam = Camera.main;
        const int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;

        var canvases = Object.FindObjectsByType<Canvas>();
        var modes = new RenderMode[canvases.Length];
        var distances = new float[canvases.Length];
        for (int i = 0; i < canvases.Length; i++)
        {
            modes[i] = canvases[i].renderMode;
            distances[i] = canvases[i].planeDistance;
            if (modes[i] != RenderMode.ScreenSpaceOverlay) continue;
            canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
            canvases[i].worldCamera = cam;
            canvases[i].planeDistance = 1 + 0.01f * (10 - canvases[i].sortingOrder);
        }
        Canvas.ForceUpdateCanvases();

        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = modes[i];
            canvases[i].planeDistance = distances[i];
        }

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
        Object.Destroy(rt);
        Object.Destroy(tex);
    }
}
