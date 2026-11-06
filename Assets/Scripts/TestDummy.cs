using System.Collections;
using UnityEngine;

public class TestDummy : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float damagePerSpeed = 2f;
    [SerializeField] private float hitCooldown = 0.25f;
    [SerializeField] private float resetDelay = 4f;

    private int currentHealth;
    private float lastHitTime = float.NegativeInfinity;
    private float resetAt = float.PositiveInfinity;
    private TextMesh label;
    private Renderer bodyRenderer;
    private Color originalColor;

    private void Start()
    {
        if (GetComponent<Collider>() == null)
        {
            StartCoroutine(SpawnDummyWhenCameraIsReady());
            return;
        }

        currentHealth = maxHealth;
        bodyRenderer = GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            originalColor = new Color(0.2f, 0.65f, 0.95f);
            bodyRenderer.material.color = originalColor;
        }

        CreateLabel();
        RefreshLabel("Punch to test");
    }

    private IEnumerator SpawnDummyWhenCameraIsReady()
    {
        Camera cameraToFace = null;
        while (cameraToFace == null)
        {
            cameraToFace = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cameraToFace == null) yield return null;
        }

        yield return new WaitForSeconds(1f);
        Vector3 forward = Vector3.ProjectOnPlane(cameraToFace.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        Vector3 position = cameraToFace.transform.position + forward * 1.25f;
        position.y = cameraToFace.transform.position.y - 0.7f;

        GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = "Test Dummy";
        dummy.transform.position = position;
        dummy.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
        dummy.AddComponent<TestDummy>();
        Destroy(this);
    }

    private void Update()
    {
        if (Time.time >= resetAt)
        {
            currentHealth = maxHealth;
            resetAt = float.PositiveInfinity;
            if (bodyRenderer != null) bodyRenderer.material.color = originalColor;
            RefreshLabel("Ready to test");
        }

        if (label != null && Camera.main != null)
        {
            label.transform.rotation = Camera.main.transform.rotation;
        }
    }

    public void RegisterPunch(float punchSpeed)
    {
        if (punchSpeed < 1.2f || Time.time - lastHitTime < hitCooldown)
        {
            return;
        }

        lastHitTime = Time.time;
        int damage = Mathf.Max(1, Mathf.RoundToInt(punchSpeed * damagePerSpeed));
        currentHealth = Mathf.Max(0, currentHealth - damage);
        resetAt = Time.time + resetDelay;

        if (bodyRenderer != null) bodyRenderer.material.color = Color.Lerp(Color.red, originalColor, (float)currentHealth / maxHealth);
        RefreshLabel($"-{damage} damage");
    }

    private void CreateLabel()
    {
        GameObject labelObject = new GameObject("Dummy Health Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = Vector3.up * 1.2f;
        label = labelObject.AddComponent<TextMesh>();
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 48;
        label.characterSize = 0.08f;
        label.color = Color.white;
    }

    private void RefreshLabel(string message)
    {
        if (label != null)
        {
            label.text = $"TEST DUMMY\n{currentHealth} / {maxHealth} HP\n{message}";
        }
    }
}
