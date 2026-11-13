using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Provides VR controller haptic feedback for punches, impacts, and knockouts.
/// Works across OpenXR, Meta Quest, and PC VR platforms.
/// </summary>
public static class VRHaptics
{
    public static void TriggerPunchImpulse(bool isLeft, float punchSpeed)
    {
        float normalizedSpeed = Mathf.InverseLerp(1.2f, 5.0f, punchSpeed);
        float amplitude = Mathf.Lerp(0.55f, 1.0f, normalizedSpeed);
        float duration = Mathf.Lerp(0.08f, 0.18f, normalizedSpeed);
        SendImpulse(isLeft ? XRNode.LeftHand : XRNode.RightHand, amplitude, duration);
    }

    public static void TriggerDamageImpulse(float damage)
    {
        float amplitude = Mathf.Clamp(damage / 30f, 0.45f, 1.0f);
        float duration = Mathf.Clamp(damage / 100f, 0.12f, 0.28f);
        SendImpulse(XRNode.LeftHand, amplitude, duration);
        SendImpulse(XRNode.RightHand, amplitude, duration);
    }

    public static void TriggerKnockoutImpulse()
    {
        SendImpulse(XRNode.LeftHand, 1.0f, 0.5f);
        SendImpulse(XRNode.RightHand, 1.0f, 0.5f);
    }

    private static void SendImpulse(XRNode node, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid)
        {
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.01f, duration));
        }
    }
}
