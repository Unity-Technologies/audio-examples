using UnityEngine;

// One basalt pillar. Landing on it plays a note; its top glows a random color (and lights upward) while the marble rests on it.
public class HexPillar : MonoBehaviour
{
    public Material topMat;
    public float topY, t;
    public int jitter;
    const float Intensity = 3f, RampUp = 6f, RampDown = 1.2f, RetriggerDelay = 1f, LightIntensity = 60f, PointIntensity = 30f;
    static readonly int Emission = Shader.PropertyToID("_EmissionColor");
    float level, touchTime = -10f, lastTrigger = -10f;
    bool wasTouched;
    Color color = Color.white;
    Light spot, point; // spot shines up through the marble, point lights nearby (lower) pillars
    MarbleBall ball;

    void OnCollisionEnter(Collision c) { OnCollisionStay(c); }

    void OnCollisionStay(Collision c)
    {
        var b = c.collider.GetComponent<MarbleBall>();
        if (b == null) return;
        Vector3 p = c.transform.position, d = p - transform.position;
        if (p.y > topY + 0.3f && new Vector2(d.x, d.z).magnitude < 0.85f) { touchTime = Time.time; ball = b; }
    }

    void Update()
    {
        bool touched = Time.time - touchTime < 0.15f;
        if (touched && !wasTouched && Time.time - lastTrigger > RetriggerDelay)
        {
            lastTrigger = Time.time;
            color = Color.HSVToRGB(Random.value, 0.85f, 1f);
            if (PillarMusic.Instance != null) PillarMusic.Instance.Play(t, jitter, color);
        }
        wasTouched = touched;
        float prev = level;
        level = Mathf.MoveTowards(level, touched ? 1f : 0f, (touched ? RampUp : RampDown) * Time.deltaTime);
        if (level != prev)
        {
            topMat.SetColor(Emission, color * (level * Intensity));
            if (spot == null && level > 0f) MakeLight();
            if (spot != null)
            {
                spot.enabled = point.enabled = level > 0f;
                spot.color = point.color = color;
                spot.intensity = level * LightIntensity; point.intensity = level * PointIntensity;
            }
        }
        if (touched && ball != null) ball.SetGlow(color * (level * Intensity));
    }

    void MakeLight()
    {
        var go = new GameObject("PillarLight");
        go.transform.SetPositionAndRotation(new Vector3(transform.position.x, topY + 0.05f, transform.position.z), Quaternion.Euler(-90f, 0f, 0f));
        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot; spot.spotAngle = 80f; spot.range = 16f; spot.shadows = LightShadows.None;
        var pg = new GameObject("PillarPoint");
        pg.transform.SetParent(go.transform, false);
        pg.transform.position = new Vector3(transform.position.x, topY + 0.8f, transform.position.z);
        point = pg.AddComponent<Light>();
        point.type = LightType.Point; point.range = 9f; point.shadows = LightShadows.None;
    }

    void OnDestroy() { if (spot != null) Destroy(spot.gameObject); }
}
