using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ArtificialHorizonUI : MonoBehaviour
{
    [Header("Target")]
    public Transform target; // квадрокоптер

    [Header("Placement")]
    public bool autoCreateUI = true;
    public Vector2 size = new Vector2(240f, 240f);

    [Tooltip("Позиция от выбранного угла (см. topRight). Для topRight обычно отрицательные X/Y.")]
    public Vector2 anchoredPosition = new Vector2(-20f, -20f);

    public bool topRight = true;

    [Header("Look")]
    public Color skyColor = new Color(0.25f, 0.55f, 0.95f, 1f);
    public Color groundColor = new Color(0.55f, 0.35f, 0.18f, 1f);
    public Color lineColor = new Color(1f, 1f, 1f, 0.95f);
    public Color borderColor = new Color(1f, 1f, 1f, 0.9f);
    public Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Response")]
    [Tooltip("Сколько градусов тангажа соответствует maxPitchOffsetPx пикселям (по модулю).")]
    public float pitchRangeDeg = 45f;

    [Tooltip("Максимальный сдвиг горизонта по Y в пикселях при pitchRangeDeg.")]
    public float maxPitchOffsetPx = 80f;

    public bool clampPitch = true;

    [Header("Pitch ladder")]
    public bool enablePitchLadder = true;

    [Tooltip("Шаг делений в градусах (по запросу: 5).")]
    public int ladderStepDeg = 5;

    [Tooltip("Максимальное отображаемое деление (обычно равно pitchRangeDeg или меньше).")]
    public int ladderMaxDeg = 45;

    [Tooltip("Показывать подписи на 10/20/... градусов.")]
    public bool ladderLabels = true;

    [Tooltip("Размер шрифта подписи делений.")]
    public int ladderFontSize = 14;

    [Header("Roll zero marker")]
    public bool enableRollZeroMarker = true;

    [Header("Telemetry (optional)")]
    public bool showNumbers = true;
    public int fontSize = 14;

    // UI refs
    private RectTransform root;
    private RectTransform maskRect;
    private RectTransform horizonLayer;     // вращаем (roll) и двигаем (pitch)
    private Text numbersText;

    // cached runtime values (for debug)
    private float lastPitchDeg;
    private float lastRollDeg;

    // built-in sprite for Image
    public Sprite uiSprite;

    private void Awake()
    {
        if (target == null)
        {
            var ctrl = FindObjectOfType<QuadcopterController>();
            if (ctrl != null) target = ctrl.transform;
        }

        if (autoCreateUI)
            EnsureUI();
    }

    private void LateUpdate()
    {
        if (target == null || root == null) return;

        float pitchDeg = NormalizeAngle180(target.eulerAngles.x);
        float rollDeg = NormalizeAngle180(target.eulerAngles.z);

        lastPitchDeg = pitchDeg;
        lastRollDeg = rollDeg;

        if (clampPitch)
        {
            float pr = Mathf.Max(0.001f, pitchRangeDeg);
            pitchDeg = Mathf.Clamp(pitchDeg, -pr, pr);
        }

        // Горизонт/лестница вращаются ПРОТИВОПОЛОЖНО крену аппарата
        horizonLayer.localRotation = Quaternion.Euler(0f, 0f, -rollDeg);

        // Нос вверх (pitch +) => линия горизонта уходит ВНИЗ на приборе
        float pr2 = Mathf.Max(0.001f, pitchRangeDeg);
        float pitch01 = pitchDeg / pr2;
        float y = -pitch01 * maxPitchOffsetPx;

        var p = horizonLayer.anchoredPosition;
        p.y = y;
        horizonLayer.anchoredPosition = p;

        if (showNumbers && numbersText != null)
            numbersText.text = $"PITCH: {pitchDeg:0}°\nROLL : {rollDeg:0}°";
    }

    // ---------------- UI building ----------------

    private void EnsureUI()
    {
        if (uiSprite == null)
            uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var c = new GameObject("HorizonCanvas");
            canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            c.AddComponent<CanvasScaler>();
            c.AddComponent<GraphicRaycaster>();
        }

        // Root
        GameObject rootGO = new GameObject("ArtificialHorizon");
        rootGO.transform.SetParent(canvas.transform, false);
        root = rootGO.AddComponent<RectTransform>();
        root.sizeDelta = size;

        if (topRight)
        {
            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
        }
        else
        {
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
        }

        root.anchoredPosition = anchoredPosition;

        // Background slight tint
        var bg = rootGO.AddComponent<Image>();
        bg.sprite = uiSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0.10f);

        // Mask area
        GameObject maskGO = new GameObject("Mask");
        maskGO.transform.SetParent(root, false);
        maskRect = maskGO.AddComponent<RectTransform>();
        maskRect.anchorMin = new Vector2(0f, 0f);
        maskRect.anchorMax = new Vector2(1f, 1f);
        maskRect.offsetMin = new Vector2(10f, 10f);
        maskRect.offsetMax = new Vector2(-10f, -10f);
        maskGO.AddComponent<RectMask2D>();

        // Horizon layer (big enough to move/rotate without edges)
        GameObject layerGO = new GameObject("HorizonLayer");
        layerGO.transform.SetParent(maskRect, false);
        horizonLayer = layerGO.AddComponent<RectTransform>();
        horizonLayer.anchorMin = new Vector2(0.5f, 0.5f);
        horizonLayer.anchorMax = new Vector2(0.5f, 0.5f);
        horizonLayer.pivot = new Vector2(0.5f, 0.5f);
        horizonLayer.sizeDelta = size * 2.4f;
        horizonLayer.anchoredPosition = Vector2.zero;

        CreateSkyAndGround(horizonLayer);

        // Pitch ladder (деления)
        if (enablePitchLadder)
            CreatePitchLadder(horizonLayer);

        // Static overlays
        CreateCrosshair(root);
        CreateOutline(root);

        // Roll zero marker (V сверху)
        if (enableRollZeroMarker)
            CreateRollZeroMarker(root);

        if (showNumbers)
            CreateNumbers(root);
    }

    private void CreateSkyAndGround(RectTransform parent)
    {
        // Sky
        GameObject skyGO = new GameObject("Sky");
        skyGO.transform.SetParent(parent, false);
        var skyRect = skyGO.AddComponent<RectTransform>();
        skyRect.anchorMin = new Vector2(0f, 0.5f);
        skyRect.anchorMax = new Vector2(1f, 1f);
        skyRect.offsetMin = Vector2.zero;
        skyRect.offsetMax = Vector2.zero;

        var skyImg = skyGO.AddComponent<Image>();
        skyImg.sprite = uiSprite;
        skyImg.color = skyColor;

        // Ground
        GameObject groundGO = new GameObject("Ground");
        groundGO.transform.SetParent(parent, false);
        var groundRect = groundGO.AddComponent<RectTransform>();
        groundRect.anchorMin = new Vector2(0f, 0f);
        groundRect.anchorMax = new Vector2(1f, 0.5f);
        groundRect.offsetMin = Vector2.zero;
        groundRect.offsetMax = Vector2.zero;

        var groundImg = groundGO.AddComponent<Image>();
        groundImg.sprite = uiSprite;
        groundImg.color = groundColor;

        // Horizon line at center
        CreateLine(parent, "HorizonLine", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(parent.sizeDelta.x * 0.95f, 3f), 0f, lineColor);
    }

    private void CreatePitchLadder(RectTransform parent)
    {
        int step = Mathf.Max(1, ladderStepDeg);
        int maxDeg = Mathf.Max(step, ladderMaxDeg);

        // Подстроим maxDeg, чтобы не превышал pitchRangeDeg, если clampPitch включён.
        if (clampPitch)
            maxDeg = Mathf.Min(maxDeg, Mathf.FloorToInt(Mathf.Max(0f, pitchRangeDeg)));

        GameObject ladderGO = new GameObject("PitchLadder");
        ladderGO.transform.SetParent(parent, false);
        var ladderRect = ladderGO.AddComponent<RectTransform>();
        ladderRect.anchorMin = new Vector2(0.5f, 0.5f);
        ladderRect.anchorMax = new Vector2(0.5f, 0.5f);
        ladderRect.pivot = new Vector2(0.5f, 0.5f);
        ladderRect.sizeDelta = parent.sizeDelta;
        ladderRect.anchoredPosition = Vector2.zero;

        float w = size.x; // используем размер прибора, а не parent.sizeDelta (он больше)
        float shortLen = w * 0.28f;
        float midLen = w * 0.42f;
        float longLen = w * 0.58f;

        // Рисуем деления сверху и снизу (кроме 0)
        for (int deg = -maxDeg; deg <= maxDeg; deg += step)
        {
            if (deg == 0) continue;

            float abs = Mathf.Abs(deg);
            bool major20 = (abs % 20f) < 0.01f;
            bool major10 = (abs % 10f) < 0.01f;

            float len = major20 ? longLen : (major10 ? midLen : shortLen);
            float thickness = major20 ? 3f : (major10 ? 2.5f : 2f);

            float y = PitchDegToYOffset(deg);

            // Левая и правая риски (как у авиагоризонта)
            float gap = w * 0.10f; // зазор вокруг центра (прицельной марки)
            float halfLen = len * 0.5f;

            // left tick
            CreateLine(ladderRect, $"TickL_{deg}",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-(gap * 0.5f + halfLen * 0.5f), y),
                new Vector2(halfLen, thickness),
                0f, lineColor);

            // right tick
            CreateLine(ladderRect, $"TickR_{deg}",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(+(gap * 0.5f + halfLen * 0.5f), y),
                new Vector2(halfLen, thickness),
                0f, lineColor);

            // подписи (на 10/20/30/40 и т.д.)
            if (ladderLabels && major10)
            {
                CreateLadderLabel(ladderRect, $"LblL_{deg}", new Vector2(0f, 0.5f),
                    new Vector2(-(gap * 0.5f + halfLen + w * 0.06f), y - 8f),
                    Mathf.RoundToInt(abs).ToString());

                CreateLadderLabel(ladderRect, $"LblR_{deg}", new Vector2(1f, 0.5f),
                    new Vector2(+(gap * 0.5f + halfLen + w * 0.06f), y - 8f),
                    Mathf.RoundToInt(abs).ToString());
            }
        }
    }

    private float PitchDegToYOffset(float deg)
    {
        float pr = Mathf.Max(0.001f, pitchRangeDeg);
        float pitch01 = deg / pr;
        // deg > 0 (нос вверх) => линия уходит вниз => y отрицательное (как и в LateUpdate)
        return -pitch01 * maxPitchOffsetPx;
    }

    private void CreateLadderLabel(RectTransform parent, string name, Vector2 pivot, Vector2 anchoredPos, string text)
    {
        GameObject tgo = new GameObject(name);
        tgo.transform.SetParent(parent, false);

        Text t = tgo.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = ladderFontSize;
        t.color = new Color(lineColor.r, lineColor.g, lineColor.b, 0.95f);
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;

        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(40f, 18f);
    }

    private void CreateRollZeroMarker(RectTransform parent)
    {
        // V-образный маркер сверху по центру
        GameObject go = new GameObject("RollZeroMarker");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -6f);
        rt.sizeDelta = new Vector2(60f, 30f);

        // две диагональные линии, образующие "V"
        CreateLine(rt, "V_Left",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-10f, -10f), new Vector2(22f, 3f), +35f, borderColor);

        CreateLine(rt, "V_Right",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(+10f, -10f), new Vector2(22f, 3f), -35f, borderColor);

        // маленькая риска по центру (чтобы “0” читался лучше)
        CreateLine(rt, "V_Center",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -6f), new Vector2(10f, 3f), 0f, borderColor);
    }

    private void CreateCrosshair(RectTransform parent)
    {
        // horizontal
        CreateLine(parent, "Crosshair_H",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(parent.sizeDelta.x * 0.45f, 2f), 0f, crosshairColor);

        // vertical
        CreateLine(parent, "Crosshair_V",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(2f, parent.sizeDelta.y * 0.25f), 0f, crosshairColor);
    }

    private void CreateOutline(RectTransform parent)
    {
        float w = 2f;

        // top
        CreateLine(parent, "EdgeTop",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -w * 0.5f), new Vector2(parent.sizeDelta.x, w), 0f, borderColor);

        // bottom
        CreateLine(parent, "EdgeBottom",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, +w * 0.5f), new Vector2(parent.sizeDelta.x, w), 0f, borderColor);

        // left
        CreateLine(parent, "EdgeLeft",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(+w * 0.5f, 0f), new Vector2(w, parent.sizeDelta.y), 0f, borderColor);

        // right
        CreateLine(parent, "EdgeRight",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-w * 0.5f, 0f), new Vector2(w, parent.sizeDelta.y), 0f, borderColor);
    }

    private void CreateNumbers(RectTransform parent)
    {
        GameObject tgo = new GameObject("HorizonNumbers");
        tgo.transform.SetParent(parent, false);

        numbersText = tgo.AddComponent<Text>();
        numbersText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        numbersText.fontSize = fontSize;
        numbersText.color = new Color(1f, 1f, 1f, 0.9f);
        numbersText.alignment = TextAnchor.LowerLeft;
        numbersText.horizontalOverflow = HorizontalWrapMode.Overflow;
        numbersText.verticalOverflow = VerticalWrapMode.Overflow;

        var rt = numbersText.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(10f, 8f);
        rt.sizeDelta = new Vector2(220f, 60f);
    }

    // универсальная “линия” (прямоугольник Image)
    private void CreateLine(RectTransform parent, string name,
        Vector2 anchorMinMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta,
        float zRotDeg, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMinMax;
        rt.anchorMax = anchorMinMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        rt.localRotation = Quaternion.Euler(0f, 0f, zRotDeg);

        Image img = go.AddComponent<Image>();
        img.sprite = uiSprite;
        img.color = color;
        img.raycastTarget = false;
    }

    private static float NormalizeAngle180(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }
}