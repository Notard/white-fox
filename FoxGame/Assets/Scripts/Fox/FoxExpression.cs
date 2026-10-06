using System.Collections;
using UnityEngine;

public enum FoxMood
{
    Normal,   // 눈 뜸 (가끔 깜빡임)
    Happy,    // 웃는 눈 ^ ^
    Closed,   // 감은 눈
}

/// <summary>
/// 애니메이션풍 2D 눈 그림을 바꿔 끼워 표정을 만든다. 눈 재질(FoxGame/ToonDecal)의 _BaseMap 을 바꾼다.
/// 보통 때는 3~5초마다 깜빡인다.
/// </summary>
public class FoxExpression : MonoBehaviour
{
    public Texture2D eyeOpen;
    public Texture2D eyeHappy;
    public Texture2D eyeBlink;
    [Tooltip("눈 재질 이름 (이 이름으로 시작하는 재질을 찾는다)")]
    public string eyeMaterialName = "Fox_Eye";

    public FoxMood Mood { get; private set; } = FoxMood.Normal;

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    Material eye;
    bool blinking;
    Coroutine temporary;

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)          // 이 여우만의 재질 복사본
                if (m.name.StartsWith(eyeMaterialName)) eye = m;
        Apply();
    }

    void OnEnable() => StartCoroutine(BlinkLoop());

    public void SetMood(FoxMood mood)
    {
        if (temporary != null) { StopCoroutine(temporary); temporary = null; }
        Mood = mood;
        Apply();
    }

    /// <summary>잠깐 다른 표정을 지었다가 원래대로 (점프할 때 웃는 눈 등).</summary>
    public void Flash(FoxMood mood, float seconds)
    {
        if (Mood != FoxMood.Normal) return;   // 이미 특별한 표정이면 그대로
        if (temporary != null) StopCoroutine(temporary);
        temporary = StartCoroutine(FlashRoutine(mood, seconds));
    }

    IEnumerator FlashRoutine(FoxMood mood, float seconds)
    {
        Mood = mood;
        Apply();
        yield return new WaitForSeconds(seconds);
        Mood = FoxMood.Normal;
        Apply();
        temporary = null;
    }

    IEnumerator BlinkLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(2.8f, 5f));
            if (Mood != FoxMood.Normal) continue;
            blinking = true;
            Apply();
            yield return new WaitForSeconds(0.12f);
            blinking = false;
            Apply();
        }
    }

    void Apply()
    {
        if (eye == null) return;
        var tex = Mood switch
        {
            FoxMood.Happy => eyeHappy,
            FoxMood.Closed => eyeBlink,
            _ => blinking ? eyeBlink : eyeOpen,
        };
        if (tex != null) eye.SetTexture(BaseMapId, tex);
    }
}
