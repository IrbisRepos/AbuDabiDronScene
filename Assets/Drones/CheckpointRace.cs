using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class CheckpointRace : MonoBehaviour
{
    [Header("Target (who passes checkpoints)")]
    [Tooltip("Rigidbody объекта, который проходит маршрут (квадрокоптер/игрок). Если пусто — попытается найти сам.")]
    public Rigidbody targetRigidbody;

    [Tooltip("Опционально: если targetRigidbody пуст, можно искать по тегу.")]
    public string targetTag = "Player";

    [Header("Checkpoints")]
    [Tooltip("Автоматически найти все RaceCheckpoint в сцене и отсортировать по Order.")]
    public bool autoFindCheckpoints = true;

    [Tooltip("Если autoFindCheckpoints=false, можно задать список вручную (в порядке Order).")]
    public List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();

    [Header("UI (Legacy uGUI Text)")]
    public bool autoCreateUI = true;
    public Text uiText;
    public int fontSize = 18;
    public Color textColor = new Color(0.9f, 0.95f, 1f, 1f);

    [Header("Telemetry - Performance")]
    public bool showFps = true;

    [Tooltip("Скорость сглаживания. Больше = быстрее реагирует, но сильнее прыгает.")]
    [Range(0.5f, 30f)]
    public float perfSmoothing = 10f;

    public bool showFixedFps = true;
    public bool showPhysicsLoad = true;

    [Header("Behaviour")]
    [Tooltip("Начинать таймер при прохождении первого чекпоинта (Order=0).")]
    public bool startTimerOnFirstCheckpoint = true;

    [Tooltip("Если true — при входе не в тот чекпоинт будет показываться подсказка.")]
    public bool showWrongCheckpointMessage = true;

    [Tooltip("Клавиша сброса прогресса/таймера.")]
    public KeyCode resetKey = KeyCode.F5;

    // runtime
    private int currentIndex = 0;
    private bool running = false;
    private bool finished = false;

    private float startTime;
    private float finishTime;

    private float maxSpeedMS = 0f;
    private float avgSpeedMS = 0f;

    private float distanceTraveled = 0f;
    private Vector3 lastPos;
    private bool hasLastPos = false;

    private float wrongMsgUntil = 0f;
    private string wrongMsg = "";

    // Perf telemetry runtime (smoothed)
    private float fpsSmoothed = 0f;
    private float frameMsSmoothed = 0f;

    private float fixedFpsSmoothed = 0f;
    private float fixedStepsPerFrameSmoothed = 0f;
    private float physicsLoadSmoothed = 0f; // "fixed time / frame time" ratio (1.0 == 100%)

    // Counters for FixedUpdate per rendered frame
    private int fixedStepsSinceLastUpdate = 0;

    private readonly StringBuilder sb = new StringBuilder(1200);

    private void Awake()
    {
        ResolveTarget();
        BuildCheckpointList();
        EnsureUI();

        foreach (var cp in checkpoints)
            if (cp != null) cp.manager = this;

        ResetRace();
    }

    private void Update()
    {
        UpdatePerformanceTelemetry();

        if (Input.GetKeyDown(resetKey))
            ResetRace();

        if (uiText != null)
            uiText.text = BuildUI();
    }

    private void FixedUpdate()
    {
        fixedStepsSinceLastUpdate++;

        if (!running || finished || targetRigidbody == null) return;

        Vector3 pos = targetRigidbody.position;

        if (!hasLastPos)
        {
            lastPos = pos;
            hasLastPos = true;
        }

        distanceTraveled += Vector3.Distance(lastPos, pos);
        lastPos = pos;

        float speed = targetRigidbody.velocity.magnitude;
        if (speed > maxSpeedMS) maxSpeedMS = speed;
    }

    private void UpdatePerformanceTelemetry()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0.000001f) return;

        float k = Mathf.Max(0.01f, perfSmoothing);
        float a = 1f - Mathf.Exp(-k * dt);

        // FPS
        float fpsInstant = 1f / dt;
        fpsSmoothed = Mathf.Lerp(fpsSmoothed, fpsInstant, a);
        frameMsSmoothed = Mathf.Lerp(frameMsSmoothed, dt * 1000f, a);

        // Fixed FPS (сколько FixedUpdate реально было за этот кадр)
        int steps = fixedStepsSinceLastUpdate;
        fixedStepsSinceLastUpdate = 0;

        float fixedFpsInstant = steps / dt;                  // steps per second
        float stepsPerFrameInstant = steps;                  // steps per rendered frame
        float physicsLoadInstant = 0f;

        // Оценка "нагрузки" физики: сколько времени физика "должна" была просимулировать за этот кадр
        // по сравнению с длительностью кадра. >1 значит догоняет (несколько fixed шагов за кадр).
        float fixedDt = Time.fixedDeltaTime;
        if (fixedDt > 0.000001f)
            physicsLoadInstant = (steps * fixedDt) / dt;

        fixedFpsSmoothed = Mathf.Lerp(fixedFpsSmoothed, fixedFpsInstant, a);
        fixedStepsPerFrameSmoothed = Mathf.Lerp(fixedStepsPerFrameSmoothed, stepsPerFrameInstant, a);
        physicsLoadSmoothed = Mathf.Lerp(physicsLoadSmoothed, physicsLoadInstant, a);
    }

    // Called by checkpoint
    public void NotifyCheckpointTriggered(RaceCheckpoint checkpoint, Collider other)
    {
        if (finished) return;
        if (checkpoint == null) return;
        if (!IsTargetCollider(other)) return;

        if (autoFindCheckpoints && (checkpoints == null || checkpoints.Count == 0))
            BuildCheckpointList();

        if (checkpoint.Order != currentIndex)
        {
            if (showWrongCheckpointMessage)
            {
                wrongMsg = $"Wrong checkpoint. Expected: {currentIndex}, got: {checkpoint.Order}";
                wrongMsgUntil = Time.time + 1.2f;
            }
            return;
        }

        if (startTimerOnFirstCheckpoint && currentIndex == 0 && !running)
            StartTimer();

        currentIndex++;

        if (checkpoint.isFinish)
        {
            FinishRace();
            return;
        }

        if (currentIndex >= checkpoints.Count)
            FinishRace();
    }

    private void StartTimer()
    {
        running = true;
        finished = false;
        startTime = Time.time;

        distanceTraveled = 0f;
        maxSpeedMS = 0f;
        hasLastPos = false;
    }

    private void FinishRace()
    {
        finished = true;
        running = false;

        finishTime = Time.time;
        float elapsed = Mathf.Max(0.0001f, finishTime - startTime);
        avgSpeedMS = distanceTraveled / elapsed;
    }

    public void ResetRace()
    {
        running = !startTimerOnFirstCheckpoint;
        finished = false;

        currentIndex = 0;

        startTime = Time.time;
        finishTime = 0f;

        distanceTraveled = 0f;
        maxSpeedMS = 0f;
        avgSpeedMS = 0f;

        hasLastPos = false;

        wrongMsg = "";
        wrongMsgUntil = 0f;

        if (!startTimerOnFirstCheckpoint)
            StartTimer();
    }

    // ---------------- UI ----------------

    private string BuildUI()
    {
        int total = checkpoints?.Count ?? 0;
        int passed = Mathf.Clamp(currentIndex, 0, total);

        float elapsed = running ? (Time.time - startTime) : (finished ? (finishTime - startTime) : 0f);
        if (!running && !finished && !startTimerOnFirstCheckpoint)
            elapsed = Time.time - startTime;

        float curSpeedMS = (targetRigidbody != null) ? targetRigidbody.velocity.magnitude : 0f;

        sb.Clear();

        if (showFps || showFixedFps || showPhysicsLoad)
        {
            sb.AppendLine("PERFORMANCE");
            sb.AppendLine("-----------");

            if (showFps)
                sb.AppendLine($"FPS: {fpsSmoothed:0.0}   Frame: {frameMsSmoothed:0.0} ms");

            if (showFixedFps)
                sb.AppendLine($"Fixed FPS: {fixedFpsSmoothed:0.0}   Fixed steps/frame: {fixedStepsPerFrameSmoothed:0.00}");

            if (showPhysicsLoad)
            {
                // показываем как % и как ratio
                float pct = physicsLoadSmoothed * 100f;
                sb.AppendLine($"Physics load: {pct:0}% (ratio {physicsLoadSmoothed:0.00})   fixedDT: {Time.fixedDeltaTime * 1000f:0.0} ms");
            }

            sb.AppendLine();
        }

        sb.AppendLine("RACE / CHECKPOINTS");
        sb.AppendLine("------------------");
        sb.AppendLine($"Checkpoint: {passed}/{total}");
        sb.AppendLine($"Status: {(finished ? "FINISHED" : (running ? "RUNNING" : "WAITING"))}");
        sb.AppendLine($"Time: {elapsed:0.00} s");
        sb.AppendLine($"Speed: {curSpeedMS:0.00} m/s ({curSpeedMS * 3.6f:0.0} km/h)");
        sb.AppendLine($"Max speed: {maxSpeedMS:0.00} m/s ({maxSpeedMS * 3.6f:0.0} km/h)");

        if (finished)
        {
            sb.AppendLine();
            sb.AppendLine("RESULTS");
            sb.AppendLine("-------");
            sb.AppendLine($"Finish time: {elapsed:0.00} s");
            sb.AppendLine($"Distance: {distanceTraveled:0.0} m");
            sb.AppendLine($"Average speed: {avgSpeedMS:0.00} m/s ({avgSpeedMS * 3.6f:0.0} km/h)");
        }

        if (showWrongCheckpointMessage && Time.time < wrongMsgUntil && !string.IsNullOrEmpty(wrongMsg))
        {
            sb.AppendLine();
            sb.AppendLine(wrongMsg);
        }

        sb.AppendLine();
        sb.AppendLine($"Reset: {resetKey}");

        return sb.ToString();
    }

    private void EnsureUI()
    {
        if (uiText != null) return;
        if (!autoCreateUI) return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject c = new GameObject("RaceCanvas");
            canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            c.AddComponent<CanvasScaler>();
            c.AddComponent<GraphicRaycaster>();
        }

        GameObject tgo = new GameObject("RaceText");
        tgo.transform.SetParent(canvas.transform, false);

        uiText = tgo.AddComponent<Text>();
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize = fontSize;
        uiText.color = textColor;
        uiText.alignment = TextAnchor.UpperLeft;
        uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
        uiText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rt = uiText.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(640f, 520f);
    }

    // ---------------- Setup helpers ----------------

    private void ResolveTarget()
    {
        if (targetRigidbody != null) return;

        if (!string.IsNullOrEmpty(targetTag))
        {
            GameObject go = GameObject.FindGameObjectWithTag(targetTag);
            if (go != null) targetRigidbody = go.GetComponent<Rigidbody>();
        }

        if (targetRigidbody == null)
        {
            var quad = FindObjectOfType<QuadcopterController>();
            if (quad != null) targetRigidbody = quad.GetComponent<Rigidbody>();
        }
    }

    private void BuildCheckpointList()
    {
        if (!autoFindCheckpoints) return;

        checkpoints = FindObjectsOfType<RaceCheckpoint>(true)
            .OrderBy(c => c.Order)
            .ToList();

        foreach (var cp in checkpoints)
            cp.manager = this;
    }

    private bool IsTargetCollider(Collider other)
    {
        if (targetRigidbody == null || other == null) return false;

        Rigidbody hitRb = other.attachedRigidbody;
        if (hitRb == null) return false;

        return hitRb == targetRigidbody;
    }
}
