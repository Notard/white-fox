using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스 휠 확대·축소. Zoom 0 = 보드 전체(CameraFit 이 맞춘 화면), 1 = 여우 가까이.
/// 확대할수록 카메라가 보드 가운데에서 여우 쪽으로 옮겨 가고, 확대된 동안에는 여우를 따라간다.
/// 일시정지 중이나 결과 창이 떠 있을 때는 휠을 받지 않는다.
/// </summary>
[RequireComponent(typeof(CameraFit))]
public class CameraZoom : MonoBehaviour
{
    [Tooltip("가장 확대했을 때 여우까지 거리")]
    public float closeDistance = 6.5f;
    [Tooltip("휠 한 칸에 바뀌는 양 (0~1)")]
    public float step = 0.12f;
    [Tooltip("따라가는 부드러움 (클수록 빠름)")]
    public float smoothing = 10f;

    /// <summary>목표 확대 정도 (0 = 보드 전체, 1 = 여우 가까이).</summary>
    public float Target { get; private set; }
    /// <summary>지금 화면의 확대 정도 (Target 을 부드럽게 따라감).</summary>
    public float Current { get; private set; }

    CameraFit fit;
    FoxController fox;
    GameManager game;

    void Awake()
    {
        fit = GetComponent<CameraFit>();
        fox = FindAnyObjectByType<FoxController>();
        game = FindAnyObjectByType<GameManager>();
    }

    /// <summary>확대 정도를 정한다 (0~1).</summary>
    public void SetZoom(float t) => Target = Mathf.Clamp01(t);

    bool CanZoom => game == null || (!game.IsPaused && (game.State == GameState.Playing || game.State == GameState.Ready));

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && CanZoom)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                SetZoom(Target + Mathf.Sign(wheel) * step * Mathf.Max(1f, Mathf.Abs(wheel) / 120f));
        }
    }

    void LateUpdate()
    {
        if (fit.FitDistance <= 0) return;
        Current = Mathf.Lerp(Current, Target, 1 - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
        if (Mathf.Abs(Current - Target) < 0.0005f) Current = Target;

        // 바라보는 점: 보드 가운데 → 여우 (조금 위)
        Vector3 focus = fit.FitCenter;
        if (fox != null)
            focus = Vector3.Lerp(fit.FitCenter, fox.transform.position + Vector3.up * 0.5f, Mathf.SmoothStep(0, 1, Current));
        float distance = Mathf.Lerp(fit.FitDistance, Mathf.Min(closeDistance, fit.FitDistance), Current);
        transform.position = focus - fit.Direction * distance;
        transform.rotation = Quaternion.LookRotation(fit.Direction, Vector3.up);
    }
}
