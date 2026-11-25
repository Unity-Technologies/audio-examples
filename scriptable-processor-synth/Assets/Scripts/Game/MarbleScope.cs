using UnityEngine;

// Waveform drawn inside the marble: a camera-facing line just in front of the glow core.
public class MarbleScope : MonoBehaviour
{
    const int N = 96;
    LineRenderer lr;
    Transform rig;
    readonly Vector3[] pts = new Vector3[N];

    void Start()
    {
        rig = new GameObject("MarbleScope").transform;
        lr = rig.gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.positionCount = N; lr.alignment = LineAlignment.View;
        lr.widthMultiplier = 0.03f; lr.numCapVertices = 4; lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", new Color(0.8f, 1f, 1f));
        lr.material = m;
    }

    static float Sample(int i) => SynthScope.buf[i & (SynthScope.Size - 1)];

    void LateUpdate()
    {
        var cam = Camera.main.transform;
        rig.SetPositionAndRotation(transform.position - cam.forward * 0.3f, cam.rotation);

        int n = 1024, end = SynthScope.pos - 1, start = end - 2048;
        for (int i = start; i < end - n; i++) if (Sample(i) <= 0 && Sample(i + 1) > 0) { start = i; break; } // hold the trace still
        for (int i = 0; i < N; i++)
            pts[i] = new Vector3((i / (N - 1f) - 0.5f) * 0.5f, Mathf.Clamp(Sample(start + i * n / N) * 1.2f, -0.12f, 0.12f), 0);
        lr.SetPositions(pts);
    }

    void OnDestroy() { if (rig != null) Destroy(rig.gameObject); }
}
