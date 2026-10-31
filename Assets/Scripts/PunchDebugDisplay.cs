using UnityEngine;
using UnityEngine.UI;

public class PunchDebugDisplay : MonoBehaviour
{
    private static PunchDebugDisplay instance;

    private Transform displayCamera;
    private RectTransform canvasRoot;
    private Text readout;
    private float punchSpeed;
    private string punchResult = "Contact: waiting... | Damage: --";

    public static void Show(string message)
    {
        EnsureInstance();
        if (instance != null)
        {
            instance.punchResult = message;
            instance.Refresh();
        }
    }

    public static void UpdateSpeed(float speed)
    {
        EnsureInstance();
        if (instance != null)
        {
            instance.punchSpeed = speed;
            instance.Refresh();
        }
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        GameObject displayObject = new GameObject("Punch Debug Display", typeof(RectTransform));
        instance = displayObject.AddComponent<PunchDebugDisplay>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        CreateReadout();
    }

    private void LateUpdate()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }

        if (mainCamera == null || displayCamera == mainCamera.transform)
            return;

        displayCamera = mainCamera.transform;
        canvasRoot.SetParent(displayCamera, false);
        canvasRoot.localPosition = new Vector3(-0.42f, 0.30f, 1.2f);
        canvasRoot.localRotation = Quaternion.identity;
        canvasRoot.localScale = Vector3.one * 0.001f;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void CreateReadout()
    {
        GameObject canvasObject = new GameObject("Punch Debug Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasRoot = canvasObject.GetComponent<RectTransform>();

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        canvasRoot.sizeDelta = new Vector2(720f, 190f);

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasRoot, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.82f);

        GameObject textObject = new GameObject("Readout", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(panelObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18f, 12f);
        textRect.offsetMax = new Vector2(-18f, -12f);

        readout = textObject.GetComponent<Text>();
        readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        readout.fontSize = 38;
        readout.alignment = TextAnchor.MiddleLeft;
        readout.horizontalOverflow = HorizontalWrapMode.Wrap;
        readout.verticalOverflow = VerticalWrapMode.Overflow;
        readout.color = Color.white;
        Refresh();
    }

    private void Refresh()
    {
        if (readout != null)
        {
            readout.text = $"PUNCH DEBUG\nSpeed: {punchSpeed:F2} m/s\n{punchResult}";
        }
    }
}
