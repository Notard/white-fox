using UnityEngine;

/// <summary>
/// 보드 전체가 화면에 들어오도록 카메라 거리를 맞춘다. 각도와 화면비는 그대로 두고 거리만 바꾼다.
/// 위쪽은 보석 배지, 아래쪽은 조작 안내 자리를 비워 둔다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFit : MonoBehaviour
{
    [Tooltip("내려다보는 방향 (보드 중심을 향함)")]
    public Vector3 viewDirection = new Vector3(0, -1, 1);
    [Tooltip("화면 가장자리 여백 (뷰포트 비율): 왼쪽, 오른쪽, 아래, 위")]
    public Vector4 margins = new Vector4(0.06f, 0.06f, 0.1f, 0.15f);
    public float targetHeight = -0.45f;

    public void Fit(Board board)
    {
        var cam = GetComponent<Camera>();
        var dir = viewDirection.normalized;
        Vector3 center = board.transform.position + Vector3.up * targetHeight;
        Vector2 half = board.Size * 0.5f;

        // 보드 아래쪽 모서리 + 가장 높은 칸 윗면 + 그 위 바위·보석·여우 높이까지 포함
        var corners = new Vector3[12];
        int i = 0;
        float top = board.TopY;
        center += Vector3.up * top * 0.5f;
        foreach (float y in new[] { board.BottomY, top, top + 0.9f })
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                corners[i++] = board.transform.position + new Vector3(sx * half.x, y, sz * half.y);

        float near = 1f, far = 200f;
        for (int step = 0; step < 30; step++)
        {
            float d = (near + far) / 2;
            Place(cam, center, dir, d);
            if (AllInside(cam, corners)) far = d; else near = d;
        }
        Place(cam, center, dir, far);
        FitCenter = center;
        FitDistance = far;
        Direction = dir;
    }

    /// <summary>마지막 Fit 결과: 바라보는 점, 거리, 방향 (CameraZoom 이 씀).</summary>
    public Vector3 FitCenter { get; private set; }
    public float FitDistance { get; private set; }
    public Vector3 Direction { get; private set; }

    static void Place(Camera cam, Vector3 center, Vector3 dir, float distance)
    {
        cam.transform.position = center - dir * distance;
        cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    bool AllInside(Camera cam, Vector3[] points)
    {
        foreach (var p in points)
        {
            var v = cam.WorldToViewportPoint(p);
            if (v.z <= 0 || v.x < margins.x || v.x > 1 - margins.y || v.y < margins.z || v.y > 1 - margins.w)
                return false;
        }
        return true;
    }
}
