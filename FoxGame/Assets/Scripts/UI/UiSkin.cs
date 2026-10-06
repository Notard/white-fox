using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 그림 모음 (Codex 로 만든 그림, Assets/Textures/UI/gen). 비어 있는 칸은 기본 둥근 사각형으로 대신한다.
/// 패널·버튼 스프라이트는 9-slice 테두리가 설정되어 있어야 한다 (ProjectSetup 이 설정).
/// </summary>
[CreateAssetMenu(menuName = "FoxGame/UI Skin", fileName = "UiSkin")]
public class UiSkin : ScriptableObject
{
    public Sprite panel;
    public Sprite buttonPrimary;
    public Sprite buttonSecondary;
    public Sprite logo;
    public Sprite iconLock;
    public Sprite iconClock;
    public Sprite iconStar;
    public Sprite iconPause;
    public Sprite iconGem;
    public Texture2D sky;

    /// <summary>카메라 뒤 가장 먼 곳에 하늘 그림을 깐다.</summary>
    public void ApplySky(Camera cam, Transform parent)
    {
        if (sky == null || cam == null) return;
        var canvas = new GameObject("Sky", typeof(Canvas)).GetComponent<Canvas>();
        canvas.transform.SetParent(parent, false);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = cam.farClipPlane * 0.9f;
        canvas.sortingOrder = -100;
        var img = new GameObject("Image", typeof(RawImage)).GetComponent<RawImage>();
        img.transform.SetParent(canvas.transform, false);
        img.texture = sky;
        img.raycastTarget = false;
        UiKit.Stretch(img.rectTransform);
        // 화면비가 달라도 하늘이 늘어나 보이지 않게 가운데를 잘라 쓴다
        var fitter = img.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = (float)sky.width / sky.height;
    }
}
