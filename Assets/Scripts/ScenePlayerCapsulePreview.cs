using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Shows the network player's capsule in the scene editor before a network player spawns.
/// This preview removes itself when the game enters Play mode; the live capsule comes from
/// the network player prefab.
/// </summary>
[ExecuteAlways]
public sealed class ScenePlayerCapsulePreview : MonoBehaviour
{
    private void Update()
    {
        if (Application.isPlaying)
        {
            Destroy(gameObject);
            return;
        }

        XROrigin origin = FindFirstObjectByType<XROrigin>();
        if (origin != null)
            transform.SetPositionAndRotation(origin.transform.position + Vector3.up * 0.9f, origin.transform.rotation);
    }
}
