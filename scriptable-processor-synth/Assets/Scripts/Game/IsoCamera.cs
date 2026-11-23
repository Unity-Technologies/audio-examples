using UnityEngine;

// Orthographic isometric camera that follows a target.
[RequireComponent(typeof(Camera))]
public class IsoCamera : MonoBehaviour
{
    public Transform target;
    public float size = 7f, smooth = 6f;
    bool snapped;

    void Start()
    {
        var cam = GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = size;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.06f, 0.1f);
        transform.rotation = Quaternion.Euler(35.264f, -45f, 0f);
    }

    void LateUpdate()
    {
        if (!target) return;
        Vector3 want = target.position - transform.forward * 40f;
        // snap on the first frame: gliding in from the scene camera's start position puts the piano behind the near plane
        if (!snapped) { transform.position = want; snapped = true; return; }
        transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-smooth * Time.deltaTime));
    }
}
