using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 코드로 만드는 UI 공통 도우미: 캔버스, 둥근 패널, 라벨, 버튼, 대화 상자.
/// 한글은 Windows 기본 글꼴(맑은 고딕). 둥근 모서리 스프라이트는 씬의 UI 컴포넌트가 Rounded 에 넣어 준다.
/// 일시정지(Time.timeScale = 0) 중에도 움직이도록 애니메이션은 unscaled 시간을 쓴다.
/// </summary>
public static class UiKit
{
    public static readonly Color Ink = new(0.18f, 0.24f, 0.33f);
    public static readonly Color Muted = new(0.35f, 0.42f, 0.5f);
    public static readonly Color Accent = new(0.22f, 0.56f, 0.9f);
    public static readonly Color Soft = new(0.9f, 0.93f, 0.96f);
    public static readonly Color Card = Color.white;
    public static readonly Color Glass = new(1, 1, 1, 0.85f);
    public static readonly Color Dim = new(0.08f, 0.14f, 0.24f, 0.45f);

    public static Sprite Rounded { get; set; }
    /// <summary>Codex 로 만든 UI 그림. null 이면 둥근 사각형만 쓴다.</summary>
    public static UiSkin Skin { get; set; }

    /// <summary>씬의 UI 컴포넌트가 Awake 에서 부른다.</summary>
    public static void Init(Sprite rounded, UiSkin skin)
    {
        Rounded = rounded;
        Skin = skin;
    }

    public static Image Icon(Transform parent, Sprite sprite, float size)
    {
        var img = new GameObject("Icon", typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        var le = img.GetComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        img.enabled = sprite != null;
        return img;
    }

    /// <summary>화면 위 정보 배지 (보석 수, 스테이지, 시간). 스킨이 있으면 크림색 알약 그림.</summary>
    public static RectTransform Badge(Transform parent, string name, float height)
    {
        var rt = Panel(parent, name, Glass);
        if (Skin != null && Skin.buttonSecondary != null)
        {
            var img = rt.GetComponent<Image>();
            img.sprite = Skin.buttonSecondary;
            img.color = Color.white;
            img.pixelsPerUnitMultiplier = Skin.buttonSecondary.rect.height / height;
        }
        return rt;
    }

    /// <summary>대화 상자·스테이지 카드처럼 큰 카드. 스킨 패널 그림이 있으면 그것을 쓴다.</summary>
    public static RectTransform CardPanel(Transform parent, string name)
    {
        if (Skin == null || Skin.panel == null) return Panel(parent, name, Card);
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Skin.panel;
        img.type = Image.Type.Sliced;
        // 그림 모서리(눈꽃 장식)가 화면에서 약 44 단위가 되도록
        img.pixelsPerUnitMultiplier = Mathf.Max(1f, Skin.panel.border.x / 44f);
        img.color = Color.white;
        return img.rectTransform;
    }

    static Font font;
    public static Font Font => font != null ? font
        : font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Segoe UI", "Arial" }, 32);

    // ------------------------------------------------------------ 기본 요소
    public static Canvas CreateCanvas(Transform parent, string name = "Canvas", int sortingOrder = 0)
    {
        var canvas = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.transform.SetParent(parent, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        EnsureEventSystem(parent);
        return canvas;
    }

    public static void EnsureEventSystem(Transform parent)
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(parent, false);
    }

    public static RectTransform Panel(Transform parent, string name, Color color, float roundness = 0.35f)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Rounded;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = roundness;     // 작을수록 모서리가 둥글다
        img.color = color;
        return img.rectTransform;
    }

    public static Text Label(Transform parent, string text, int size, Color color,
        FontStyle style = FontStyle.Bold, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>버튼. primary 는 파란 바탕 흰 글자, 아니면 연회색 바탕. 방향키로 골랐을 때도 색이 바뀐다.</summary>
    public static Button Button(Transform parent, string text, UnityAction onClick, bool primary = true,
        float width = 240, float height = 64, int fontSize = 26)
    {
        Color bg = primary ? Accent : Soft;
        var rt = Panel(parent, "Button_" + text, Color.white);
        rt.sizeDelta = new Vector2(width, height);
        var skinSprite = Skin == null ? null : primary ? Skin.buttonPrimary : Skin.buttonSecondary;
        if (skinSprite != null)
        {
            var im = rt.GetComponent<Image>();
            im.sprite = skinSprite;
            // 알약 그림의 높이가 버튼 높이와 같아지도록 (둥근 끝이 찌그러지지 않게)
            im.pixelsPerUnitMultiplier = skinSprite.rect.height / height;
            bg = Color.white;   // 그림 색 그대로, 상태에 따라 밝기만 바꾼다
        }
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();
        var colors = btn.colors;
        colors.normalColor = bg;
        colors.highlightedColor = Color.Lerp(bg, Color.white, 0.18f);
        colors.selectedColor = skinSprite != null ? new Color(0.86f, 0.93f, 1f)
            : primary ? Color.Lerp(bg, Color.black, 0.15f) : Color.Lerp(bg, Accent, 0.25f);
        colors.pressedColor = Color.Lerp(bg, Color.black, 0.25f);
        colors.disabledColor = new Color(bg.r, bg.g, bg.b, 0.4f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(onClick);
        var label = Label(rt, text, fontSize, primary ? Color.white : Ink);
        Stretch(label.rectTransform);
        if (primary) label.gameObject.AddComponent<Shadow>().effectColor = new Color(0.1f, 0.3f, 0.55f, 0.45f);
        return btn;
    }

    public static void SetButtonText(Button b, string text) => b.GetComponentInChildren<Text>().text = text;

    public static RectTransform Row(Transform parent, string name, float spacing = 16)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var h = rt.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        var fit = rt.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    public static RectTransform Column(Transform parent, string name, float spacing = 14)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var v = rt.GetComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        var fit = rt.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    /// <summary>화면 전체를 덮는 반투명 막 + CanvasGroup (보였다 숨겼다 할 화면 한 장).</summary>
    public static CanvasGroup Screen(Transform parent, string name, Color background)
    {
        var go = new GameObject(name, typeof(Image), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = background;
        Stretch(img.rectTransform);
        var g = go.GetComponent<CanvasGroup>();
        SetVisible(g, false);
        return g;
    }

    public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    public static void SetVisible(CanvasGroup g, bool visible)
    {
        g.alpha = visible ? 1 : 0;
        g.interactable = g.blocksRaycasts = visible;
        g.gameObject.SetActive(visible);
    }

    public static IEnumerator FadeIn(CanvasGroup g, float duration = 0.25f, float delay = 0)
    {
        g.gameObject.SetActive(true);
        g.alpha = 0;
        g.interactable = g.blocksRaycasts = true;
        if (delay > 0) yield return new WaitForSecondsRealtime(delay);
        for (float t = 0; t < 1; t += Time.unscaledDeltaTime / duration)
        {
            g.alpha = t;
            yield return null;
        }
        g.alpha = 1;
    }

    public static IEnumerator Punch(RectTransform rt, float amount = 0.15f)
    {
        for (float t = 0; t < 1; t += Time.unscaledDeltaTime / 0.25f)
        {
            rt.localScale = Vector3.one * (1 + amount * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>방향키·Enter 로 고를 수 있게 이 버튼을 선택한다.</summary>
    public static void Focus(Selectable s)
    {
        if (s == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(s.gameObject);
    }

    public static string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60);
        float s = seconds - m * 60;
        return $"{m}:{s:00.0}";
    }
}

/// <summary>제목·설명·버튼 줄로 된 가운데 대화 상자 (결과, 일시정지, 확인).</summary>
public class Dialog
{
    readonly CanvasGroup group;
    readonly RectTransform card;
    readonly Text title, body;
    readonly RectTransform buttons;
    readonly MonoBehaviour host;

    public bool IsOpen => group.gameObject.activeSelf;
    public readonly List<Button> Buttons = new();

    public Dialog(MonoBehaviour host, Transform canvas, string name)
    {
        this.host = host;
        group = UiKit.Screen(canvas, name, UiKit.Dim);
        card = UiKit.CardPanel(group.transform, "Card");
        UiKit.Anchor(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 310), new Vector2(0.5f, 0.5f));
        title = UiKit.Label(card, "", 50, UiKit.Ink);
        UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(560, 76), new Vector2(0.5f, 1));
        body = UiKit.Label(card, "", 24, UiKit.Muted, FontStyle.Normal);
        UiKit.Anchor(body.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(560, 60), new Vector2(0.5f, 1));
        buttons = UiKit.Row(card, "Buttons");
        UiKit.Anchor(buttons, new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(560, 68), new Vector2(0.5f, 0));
    }

    public string Title => title.text;

    /// <summary>buttons: (글자, 동작, 주 버튼?). 첫 버튼이 선택된 채로 열린다.</summary>
    public void Show(string titleText, Color titleColor, string bodyText, float delay, params (string text, UnityAction action, bool primary)[] items)
    {
        title.text = titleText;
        title.color = titleColor;
        body.text = bodyText;
        for (int i = buttons.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(buttons.GetChild(i).gameObject);
        Buttons.Clear();
        foreach (var (text, action, primary) in items)
            Buttons.Add(UiKit.Button(buttons, text, action, primary, primary ? 250 : 200));
        host.StartCoroutine(Open(delay));
    }

    IEnumerator Open(float delay)
    {
        yield return UiKit.FadeIn(group, 0.25f, delay);
        if (Buttons.Count > 0) UiKit.Focus(Buttons[0]);
    }

    public void Hide() => UiKit.SetVisible(group, false);
}
