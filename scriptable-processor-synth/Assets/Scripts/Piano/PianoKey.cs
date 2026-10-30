using UnityEngine;

// One piano key: a kinematic box hinged at its back edge. The marble presses the front more than the back; landing plays the note.
public class PianoKey : MonoBehaviour
{
    public int midi;
    public float width = 3f, maxAngle = 1.2f, maxDepth = 0.4f, length = 7f;
    public Material mat;
    public PianoKey source; // chord tones glow dimly while the key that triggered them is held
    public bool Touched => Time.time - touchTime < 0.12f;
    const float Intensity = 1.6f, Dim = 0.15f, HoldTime = 0.6f, RetriggerDelay = 1f, LightIntensity = 60f, PointIntensity = 30f;
    static readonly int Emission = Shader.PropertyToID("_EmissionColor");
    Rigidbody rb;
    float touchTime = -10f, press, pressTarget, level;
    bool wasTouched;
    Color color = Color.white;
    Rigidbody ballRb;
    float lastTrigger = -10f;
    MarbleBall ball;
    Light spot, point; // spot shines up through the marble, point lights the keys nearby
    Vector3 restPos;

    void Awake() { rb = GetComponent<Rigidbody>(); restPos = transform.localPosition; }

    public void Glow(PianoKey from, Color c) { source = from; color = c; }

    void OnCollisionEnter(Collision c) { OnCollisionStay(c); }

    void OnCollisionStay(Collision c)
    {
        if (c.collider.GetComponent<MarbleBall>() == null) return;
        // only the key under the marble's center counts, so rolling across a gap never triggers two keys
        var local = transform.InverseTransformPoint(c.transform.position);
        if (Mathf.Abs(local.x) > width * 0.5f * 0.85f || local.z < 0f || local.z > length) return;
        float f = 0; int n = 0;
        for (int i = 0; i < c.contactCount; i++)
        {
            var cp = c.GetContact(i);
            if (c.transform.position.y < cp.point.y + 0.2f) continue; // only contacts under the ball
            f += Mathf.Clamp01(transform.InverseTransformPoint(cp.point).z / length); n++;
        }
        if (n == 0) return;
        touchTime = Time.time; ballRb = c.rigidbody; if (ball == null) ball = c.collider.GetComponent<MarbleBall>();
        pressTarget = Mathf.Lerp(pressTarget, Mathf.Lerp(0.3f, 1f, f / n), 0.1f); // edge of the key presses further than the middle
    }

    void Update()
    {
        bool t = Touched;
        if (t && !wasTouched && Time.time - lastTrigger > RetriggerDelay)
        {
            lastTrigger = Time.time;
            color = Color.HSVToRGB(Random.value, 0.85f, 1f);
            foreach (var k in PianoRoll.Keys.Values) if (k.source == this) k.source = null; // drop stale chord links from an earlier key or scale
            var played = PillarMusic.Instance != null ? PillarMusic.Instance.PlayNote(midi, color) : null;
            if (played != null)
                foreach (int m in played)
                    if (m != midi && PianoRoll.Keys.TryGetValue(m, out var ck)) ck.Glow(this, color);
        }
        else if (!t && wasTouched && ballRb != null && ballRb.linearVelocity.y > 0.5f)
            ballRb.AddForce(Vector3.up * (0.4f + 1.2f * press / maxAngle), ForceMode.VelocityChange); // spring back kick
        wasTouched = t;
        bool held = Time.time - touchTime < HoldTime; // key stays down a while after the marble leaves
        float want = held ? pressTarget * maxAngle : 0f;
        press = Mathf.Lerp(press, want, 1f - Mathf.Exp(-(held ? 5f : 2.5f) * Time.deltaTime));
        float goal = t ? 1f : (source != null && source.Touched ? Dim : 0f);
        level = Mathf.MoveTowards(level, goal, (goal > level ? 6f : 1.2f) * Time.deltaTime);
        mat.SetColor(Emission, color * (level * Intensity));
        if (spot == null && level > 0f) MakeLight();
        if (spot != null)
        {
            spot.enabled = point.enabled = level > 0f;
            spot.color = point.color = color;
            spot.intensity = level * LightIntensity; point.intensity = level * PointIntensity;
        }
        if (t && ball != null) ball.SetGlow(color * (level * Intensity));
    }

    void MakeLight()
    {
        var go = new GameObject("KeyLight");
        go.transform.SetPositionAndRotation(transform.TransformPoint(0, 0.05f, length / 2), Quaternion.Euler(-90f, 0f, 0f));
        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot; spot.spotAngle = 80f; spot.range = 16f; spot.shadows = LightShadows.None;
        var pg = new GameObject("KeyPoint");
        pg.transform.SetParent(go.transform, false);
        pg.transform.position = go.transform.position + Vector3.up * 0.8f;
        point = pg.AddComponent<Light>();
        point.type = LightType.Point; point.range = 9f; point.shadows = LightShadows.None;
    }

    void OnDestroy() { if (spot != null) Destroy(spot.gameObject); }

    // pressed straight down with only a slight tilt, so the marble does not roll along the key
    void FixedUpdate()
    {
        var p = transform.parent;
        rb.MovePosition(p.TransformPoint(restPos + Vector3.down * (press / maxAngle * maxDepth)));
        rb.MoveRotation(p.rotation * Quaternion.Euler(press, 0, 0));
    } // relative to the rotated piano root
}
