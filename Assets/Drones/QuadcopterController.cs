using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class QuadcopterController : MonoBehaviour
{
    // ----------------------------
    // KEYS (hardcoded)
    // ----------------------------
    private const KeyCode KeyPitchForward = KeyCode.W;
    private const KeyCode KeyPitchBack = KeyCode.S;
    private const KeyCode KeyRollLeft = KeyCode.A;
    private const KeyCode KeyRollRight = KeyCode.D;
    private const KeyCode KeyYawLeft = KeyCode.Q;
    private const KeyCode KeyYawRight = KeyCode.E;

    private const KeyCode KeyThrottleUp = KeyCode.R;
    private const KeyCode KeyThrottleDown = KeyCode.F;
    private const KeyCode KeyKillMotors = KeyCode.X;

    private const KeyCode KeyToggleTelemetry = KeyCode.F1;

    // ----------------------------
    // FLIGHT
    // ----------------------------
    [Header("Flight - Thrust")]
    [Range(1.2f, 6f)] public float thrustToWeight = 2.8f;
    [Range(0.1f, 8f)] public float throttleChangeSpeed = 1.2f;
    [Range(0f, 25f)] public float throttleSmoothing = 7f;

    [Header("Flight - Stabilization (Angle mode)")]
    [Range(5f, 85f)] public float maxTiltAngleDeg = 55f;
    [Range(10f, 540f)] public float yawRateDegPerSec = 140f;

    [Range(0.5f, 40f)] public float attitudeKp = 14f;
    [Range(0.0f, 15f)] public float attitudeKd = 3.0f;
    [Range(0f, 35f)] public float uprightRescueStrength = 10f;
    [Range(1f, 60f)] public float maxAngularSpeedRad = 14f;

    // ----------------------------
    // SPEED ASSIST (tilt compensation)
    // ----------------------------
    [Header("Speed Assist")]
    public bool tiltCompensation = true;
    [Range(0f, 1f)] public float tiltCompStrength = 1f;
    [Range(1f, 3f)] public float tiltCompMax = 2.4f;
    [Range(0.05f, 0.8f)] public float tiltCompMinCos = 0.18f;

    // ----------------------------
    // MOTORS: RPM + VISUAL SPIN
    // ----------------------------
    [Header("Motors - RPM & Visual")]
    [Tooltip("������������ ������� ������ (��� �������/����������).")]
    [Range(1000f, 40000f)] public float maxMotorRPM = 14000f;

    [Tooltip("������� �� '�������� ����' ��� ����� ���� (��� �������). 0 = ����� �� ����.")]
    [Range(0f, 8000f)] public float idleMotorRPM = 1200f;

    [Tooltip("�������� ���������/������ ������� (��������������� ����� � �������).")]
    [Range(0.5f, 20f)] public float motorSpoolRate = 7f;

    [Tooltip("��������� ������ ������� Pitch/Roll ������ �� ������� RPM ����� �������� (0..0.5 ������).")]
    [Range(0f, 0.5f)] public float motorMixPR = 0.18f;

    [Tooltip("��������� ������ ������� Yaw ������ �� ������� RPM ����� �������� (0..0.5 ������).")]
    [Range(0f, 0.5f)] public float motorMixYaw = 0.12f;

    [Tooltip("������� ��������-������ � ������������ � RPM.")]
    public bool animateMotorCylinders = true;

    [Tooltip("��������� �������� �������� ��������� (���� ������� ������� ��������/������).")]
    [Range(0.1f, 10f)] public float motorVisualSpinMultiplier = 1.0f;

    // ----------------------------
    // PHYSICS
    // ----------------------------
    [Header("Physics")]
    [Range(0f, 0.5f)] public float centerOfMassDown = 0.06f;

    [Tooltip("���� ��������, ���������� ����������� ������������ (������������� ��� �����). Rigidbody.drag ������ � 0.")]
    public bool useCustomAerodynamics = true;

    [Tooltip("�������� ������������� ������������ ������� (������, ��� ������ ���������).")]
    [Range(0f, 2f)] public float airDragLinear = 0.02f;

    [Tooltip("������������ ������������� ������������ ������� (�������, ��� ������������ ������������ ��������).")]
    [Range(0f, 1.5f)] public float airDragQuadratic = 0.05f;

    [Tooltip("Angular drag Rigidbody (����� ��������).")]
    [Range(0f, 5f)] public float angularDrag = 0.6f;

    // ----------------------------
    // WIND & TURBULENCE
    // ----------------------------
    [Header("Wind & Turbulence")]
    public Vector3 steadyWindWorld = new Vector3(3f, 0f, 1f);

    [Range(0f, 20f)] public float gustAmplitude = 5f;
    [Range(0.01f, 5f)] public float gustFrequency = 0.35f;

    [Range(0f, 10f)] public float turbulenceTorque = 1.2f;
    [Range(0.01f, 10f)] public float turbulenceFrequency = 1.1f;

    // ----------------------------
    // BATTERY
    // ----------------------------
    [Header("Battery")]
    [Range(5f, 500f)] public float batteryCapacityWh = 60f;
    [Range(0f, 500f)] public float basePowerW = 20f;
    [Range(0f, 5000f)] public float maxExtraPowerW = 650f;
    [Range(0f, 2f)] public float batteryThrustLimit = 1.0f;

    // ----------------------------
    // VISUAL MOTORS
    // ----------------------------
    [Header("Visual Motors (auto-created)")]
    [Range(0.05f, 2f)] public float armLength = 0.35f;
    [Range(0.01f, 0.5f)] public float motorRadius = 0.06f;
    [Range(0.01f, 0.5f)] public float motorHeight = 0.02f;
    public Color motorColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    // ----------------------------
    // TELEMETRY (Legacy uGUI Text)
    // ----------------------------
    [Header("Telemetry (Legacy uGUI Text)")]
    public bool autoCreateTelemetryUI = true;
    public int fontSize = 16;
    public Color telemetryColor = new Color(0.85f, 0.95f, 1f, 1f);

    // ----------------------------
    // STATE
    // ----------------------------
    private Rigidbody rb;

    private float throttleCmd;
    private float throttle;
    private bool killMotors;

    private float pitchCmd;
    private float rollCmd;
    private float yawCmd;

    private float yawTargetDeg;

    private float batteryEnergyJ;
    private float batteryEnergyJ0;

    // motors visuals + positions
    // 0: front-right, 1: front-left, 2: back-left, 3: back-right (X configuration)
    private readonly Transform[] motorTf = new Transform[4];
    private readonly Vector3[] motorLocalPos = new Vector3[4];

    // motor spin directions for visuals (pair CW/CCW)
    // +1 / -1 just for visual rotation sign
    private readonly int[] motorSpinDir = { +1, -1, +1, -1 };

    // motor outputs / rpm for telemetry & visuals
    private readonly float[] motorOut = new float[4];       // current 0..1 (smoothed)
    private readonly float[] motorOutTarget = new float[4]; // target 0..1
    private readonly float[] motorRPM = new float[4];       // computed rpm

    // UI
    private Text telemetryText;
    private bool telemetryVisible = true;
    private readonly StringBuilder sb = new StringBuilder(1700);

    // tilt compensation telemetry
    private float effectiveThrottle;   // 0..1 after tilt compensation
    private float tiltCompFactor;      // multiplier

    // cached wind for telemetry (computed in FixedUpdate)
    private Vector3 lastWindVel;
    private Vector3 lastRelAirVel;
    private float lastTotalThrustN;
    private float lastPowerW;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (useCustomAerodynamics)
            rb.drag = 0f;

        rb.angularDrag = angularDrag;
        rb.centerOfMass = new Vector3(0f, -Mathf.Abs(centerOfMassDown), 0f);
        rb.maxAngularVelocity = Mathf.Max(1f, maxAngularSpeedRad);

        batteryEnergyJ0 = Mathf.Max(1f, batteryCapacityWh) * 3600f;
        batteryEnergyJ = batteryEnergyJ0;

        yawTargetDeg = transform.eulerAngles.y;

        BuildMotorLayoutX();
        CreateMotorVisuals();

        if (autoCreateTelemetryUI)
            EnsureTelemetryUI();
    }

    private void OnValidate()
    {
        if (armLength < 0.05f) armLength = 0.05f;
        if (motorRadius < 0.01f) motorRadius = 0.01f;
        if (motorHeight < 0.01f) motorHeight = 0.01f;
    }

    private void Update()
    {
        ReadKeyboardInput();

        if (Input.GetKeyDown(KeyToggleTelemetry))
            telemetryVisible = !telemetryVisible;

        if (telemetryText != null)
            telemetryText.enabled = telemetryVisible;

        if (telemetryVisible && telemetryText != null)
            telemetryText.text = BuildTelemetryString();

        if (animateMotorCylinders)
            AnimateMotorVisuals();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (killMotors)
            throttleCmd = Mathf.MoveTowards(throttleCmd, 0f, throttleChangeSpeed * dt);

        float smoothing = Mathf.Max(0.01f, throttleSmoothing);
        throttle = Mathf.Lerp(throttle, throttleCmd, 1f - Mathf.Exp(-smoothing * dt));
        throttle = Mathf.Clamp01(throttle);

        yawTargetDeg += yawCmd * yawRateDegPerSec * dt;
        yawTargetDeg = WrapAngleDeg(yawTargetDeg);

        float targetPitchDeg = -pitchCmd * maxTiltAngleDeg;
        float targetRollDeg = -rollCmd * maxTiltAngleDeg;

        Quaternion desiredRot = Quaternion.Euler(targetPitchDeg, yawTargetDeg, targetRollDeg);
        ApplyAttitudeStabilization(desiredRot);

        // ---- Tilt compensation ----
        float upDot = Mathf.Clamp(Vector3.Dot(transform.up, Vector3.up), -1f, 1f);
        float cosTilt = Mathf.Max(tiltCompMinCos, upDot);

        float idealComp = Mathf.Min(1f / cosTilt, tiltCompMax);
        tiltCompFactor = tiltCompensation ? Mathf.Lerp(1f, idealComp, tiltCompStrength) : 1f;

        effectiveThrottle = Mathf.Clamp01(throttle * tiltCompFactor);

        // Battery drain based on effectiveThrottle
        lastPowerW = basePowerW + maxExtraPowerW * Mathf.Pow(effectiveThrottle, 2.2f);
        batteryEnergyJ = Mathf.Max(0f, batteryEnergyJ - lastPowerW * dt);

        float battery01 = GetBattery01();
        float batteryThrustFactor = Mathf.Clamp01(Mathf.Lerp(1f, battery01, Mathf.Clamp01(batteryThrustLimit)));

        float g = Mathf.Abs(Physics.gravity.y);
        float maxTotalThrustN = thrustToWeight * rb.mass * g;

        lastTotalThrustN = (batteryEnergyJ <= 0f) ? 0f : effectiveThrottle * maxTotalThrustN * batteryThrustFactor;

        // RPM model (visual/telemetry)
        UpdateMotorRPMModel(dt, battery01);

        // thrust (simplified: equal per motor for stable easy control)
        ApplyMotorThrust(lastTotalThrustN);

        // Wind + aerodynamics + turbulence
        ApplyWindAerodynamicsAndTurbulence();
    }

    // ----------------------------
    // INPUT
    // ----------------------------
    private void ReadKeyboardInput()
    {
        float pitch =
            (Input.GetKey(KeyPitchForward) ? 1f : 0f) +
            (Input.GetKey(KeyPitchBack) ? -1f : 0f);

        float roll =
            (Input.GetKey(KeyRollRight) ? 1f : 0f) +
            (Input.GetKey(KeyRollLeft) ? -1f : 0f);

        float yaw =
            (Input.GetKey(KeyYawRight) ? 1f : 0f) +
            (Input.GetKey(KeyYawLeft) ? -1f : 0f);

        pitchCmd = Mathf.Clamp(pitch, -1f, 1f);
        rollCmd = Mathf.Clamp(roll, -1f, 1f);
        yawCmd = Mathf.Clamp(yaw, -1f, 1f);

        float thrDelta =
            (Input.GetKey(KeyThrottleUp) ? 1f : 0f) +
            (Input.GetKey(KeyThrottleDown) ? -1f : 0f);

        throttleCmd = Mathf.Clamp01(throttleCmd + thrDelta * throttleChangeSpeed * Time.deltaTime);

        if (Input.GetKeyDown(KeyKillMotors))
            killMotors = !killMotors;
    }

    // ----------------------------
    // STABILIZATION (PD)
    // ----------------------------
    private void ApplyAttitudeStabilization(Quaternion desiredRotation)
    {
        Quaternion qErr = desiredRotation * Quaternion.Inverse(rb.rotation);

        if (qErr.w < 0f)
        {
            qErr.x = -qErr.x;
            qErr.y = -qErr.y;
            qErr.z = -qErr.z;
            qErr.w = -qErr.w;
        }

        qErr.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (float.IsNaN(axis.x) || axis == Vector3.zero) return;

        if (angleDeg > 180f) angleDeg -= 360f;
        float angleRad = angleDeg * Mathf.Deg2Rad;

        Vector3 torqueAccel = axis.normalized * (attitudeKp * angleRad) - (attitudeKd * rb.angularVelocity);

        float tiltFromUpDeg = Vector3.Angle(transform.up, Vector3.up);
        if (tiltFromUpDeg > 65f && uprightRescueStrength > 0f)
        {
            Vector3 rescueAxis = Vector3.Cross(transform.up, Vector3.up);
            float rescueFactor = Mathf.InverseLerp(65f, 120f, tiltFromUpDeg);
            torqueAccel += rescueAxis * (uprightRescueStrength * rescueFactor);
        }

        rb.AddTorque(torqueAccel, ForceMode.Acceleration);
    }

    // ----------------------------
    // MOTORS RPM MODEL (visual/telemetry)
    // ----------------------------
    private void UpdateMotorRPMModel(float dt, float battery01)
    {
        // Base output around effectiveThrottle
        float baseOut = (batteryEnergyJ <= 0f) ? 0f : effectiveThrottle;

        // Simple mixer from sticks:
        // pitchCmd: W (+1) => rear faster, front slower
        // rollCmd : D (+1) => left faster, right slower
        // yawCmd  : E (+1) => alternate by spin direction
        float p = pitchCmd * motorMixPR;
        float r = rollCmd * motorMixPR;
        float y = yawCmd * motorMixYaw;

        // start with base
        for (int i = 0; i < 4; i++)
            motorOutTarget[i] = baseOut;

        // Pitch: front (0,1) -, back (2,3) +
        motorOutTarget[0] -= p;
        motorOutTarget[1] -= p;
        motorOutTarget[2] += p;
        motorOutTarget[3] += p;

        // Roll: right side (0,3) -, left side (1,2) +
        motorOutTarget[0] -= r;
        motorOutTarget[3] -= r;
        motorOutTarget[1] += r;
        motorOutTarget[2] += r;

        // Yaw: alternate by spinDir (purely for visuals/telemetry)
        motorOutTarget[0] += y * motorSpinDir[0];
        motorOutTarget[1] += y * motorSpinDir[1];
        motorOutTarget[2] += y * motorSpinDir[2];
        motorOutTarget[3] += y * motorSpinDir[3];

        // kill motors forces targets to 0
        if (killMotors || batteryEnergyJ <= 0f)
        {
            for (int i = 0; i < 4; i++)
                motorOutTarget[i] = 0f;
        }

        // clamp + spool smoothing
        float rate = Mathf.Max(0.01f, motorSpoolRate);
        for (int i = 0; i < 4; i++)
        {
            motorOutTarget[i] = Mathf.Clamp01(motorOutTarget[i]);
            motorOut[i] = Mathf.MoveTowards(motorOut[i], motorOutTarget[i], rate * dt);

            // RPM scaling with battery (������� �������� ���������)
            float batteryRPMFactor = Mathf.Lerp(0.65f, 1.0f, Mathf.Sqrt(Mathf.Clamp01(battery01)));

            float maxRpm = maxMotorRPM * batteryRPMFactor;
            float idle = idleMotorRPM;

            // ���� ������ "� ����" � ������� 0, ����� ������� idle
            if (motorOut[i] <= 0.0001f)
                motorRPM[i] = 0f;
            else
                motorRPM[i] = Mathf.Lerp(idle, maxRpm, motorOut[i]);
        }
    }

    private void AnimateMotorVisuals()
    {
        // ������� �������� ������ ��������� ��� Y
        // deg/sec = rpm * 360/60 = rpm * 6
        if (motorTf == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < 4; i++)
        {
            Transform m = motorTf[i];
            if (m == null) continue;

            float degPerSec = motorRPM[i] * 6f * motorVisualSpinMultiplier;
            float deltaDeg = degPerSec * dt * motorSpinDir[i];

            m.Rotate(Vector3.up, deltaDeg, Space.Self);
        }
    }

    // ----------------------------
    // THRUST (simplified)
    // ----------------------------
    private void ApplyMotorThrust(float totalThrustN)
    {
        float perMotor = totalThrustN * 0.25f;
        Vector3 thrustDir = transform.up;

        for (int i = 0; i < 4; i++)
        {
            Vector3 worldPos = transform.TransformPoint(motorLocalPos[i]);
            rb.AddForceAtPosition(thrustDir * perMotor, worldPos, ForceMode.Force);
        }
    }

    // ----------------------------
    // WIND + AERODYNAMICS + TURBULENCE
    // ----------------------------
    private void ApplyWindAerodynamicsAndTurbulence()
    {
        float t = Time.time;
        Vector3 pos = rb.worldCenterOfMass;

        float n1 = Mathf.PerlinNoise(pos.x * 0.07f + t * gustFrequency, pos.z * 0.07f);
        float n2 = Mathf.PerlinNoise(pos.z * 0.07f - t * gustFrequency * 0.9f, pos.x * 0.07f);
        float n3 = Mathf.PerlinNoise(pos.x * 0.05f + 17.3f, t * gustFrequency * 1.1f);

        Vector3 gust = new Vector3((n1 - 0.5f) * 2f, (n3 - 0.5f) * 1.2f, (n2 - 0.5f) * 2f) * gustAmplitude;
        Vector3 windVel = steadyWindWorld + gust;

        Vector3 v = rb.velocity;
        Vector3 relAir = v - windVel;

        lastWindVel = windVel;
        lastRelAirVel = relAir;

        if (useCustomAerodynamics)
        {
            float sp = relAir.magnitude;
            Vector3 dragF = (sp > 0.0001f)
                ? (-relAir * (airDragLinear + airDragQuadratic * sp))
                : Vector3.zero;

            rb.AddForce(dragF, ForceMode.Force);
        }

        float tx = (Mathf.PerlinNoise(10.1f, t * turbulenceFrequency) - 0.5f) * 2f;
        float ty = (Mathf.PerlinNoise(20.2f, t * turbulenceFrequency * 0.9f) - 0.5f) * 2f;
        float tz = (Mathf.PerlinNoise(30.3f, t * turbulenceFrequency * 1.1f) - 0.5f) * 2f;

        Vector3 turbTorque = new Vector3(tx, ty, tz) * turbulenceTorque;
        rb.AddTorque(turbTorque, ForceMode.Acceleration);
    }

    // ----------------------------
    // MOTORS VISUALS (auto)
    // ----------------------------
    private void BuildMotorLayoutX()
    {
        float a = armLength;
        motorLocalPos[0] = new Vector3(+a, 0f, +a); // front-right
        motorLocalPos[1] = new Vector3(-a, 0f, +a); // front-left
        motorLocalPos[2] = new Vector3(-a, 0f, -a); // back-left
        motorLocalPos[3] = new Vector3(+a, 0f, -a); // back-right
    }
    public GameObject vint;
    private void CreateMotorVisuals()
    {
        // cleanup old motors
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform ch = transform.GetChild(i);
            if (!ch.name.StartsWith("Motor_")) continue;

            if (Application.isPlaying) Destroy(ch.gameObject);
            else DestroyImmediate(ch.gameObject);
        }

        for (int i = 0; i < 4; i++)
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m.name = $"Motor_{i}";
            m.transform.SetParent(transform, false);
            m.transform.localPosition = motorLocalPos[i] + new Vector3(0f, 0.02f, 0f);
            m.transform.localRotation = Quaternion.identity;
            m.transform.localScale = new Vector3(motorRadius * 2f, motorHeight * 0.5f, motorRadius * 2f);
            GameObject vi = Instantiate(vint, Vector3.zero, Quaternion.identity);//
            vi.transform.SetParent(m.transform, false); vi.SetActive(true); //
            vi.transform.localPosition = new Vector3(0, 1.65f, 0);   vi.transform.localEulerAngles = new Vector3(-90,0,0);  vi.transform.localScale = Vector3.one*10; //
            Collider col = m.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            Renderer r = m.GetComponent<Renderer>();
            if (r != null)
            {
                Material mat = new Material(Shader.Find("HDRP/Lit"));
                mat.color = motorColor;
                r.sharedMaterial = mat;
            }

            motorTf[i] = m.transform;
        }
    }

    // ----------------------------
    // TELEMETRY UI (Legacy Text)
    // ----------------------------
    private void EnsureTelemetryUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject c = new GameObject("TelemetryCanvas");
            canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            c.AddComponent<CanvasScaler>();
            c.AddComponent<GraphicRaycaster>();
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        GameObject tgo = new GameObject("QuadTelemetryText");
        tgo.transform.SetParent(canvas.transform, false);

        telemetryText = tgo.AddComponent<Text>();
        telemetryText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        telemetryText.fontSize = fontSize;
        telemetryText.color = telemetryColor;
        telemetryText.alignment = TextAnchor.UpperLeft;
        telemetryText.horizontalOverflow = HorizontalWrapMode.Overflow;
        telemetryText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rt = telemetryText.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(920f, 620f);
    }

    private string BuildTelemetryString()
    {
        float battery01 = GetBattery01();

        Vector3 vel = rb.velocity;
        Vector3 av = rb.angularVelocity;
        Vector3 eul = transform.eulerAngles;

        float tiltDeg = Vector3.Angle(transform.up, Vector3.up);
        float groundSpeed = vel.magnitude;
        float airSpeed = lastRelAirVel.magnitude;

        // Motor labels for readability
        // 0 FR, 1 FL, 2 BL, 3 BR
        sb.Clear();
        sb.AppendLine("QUADCOPTER TELEMETRY (Legacy uGUI Text)");
        sb.AppendLine("--------------------------------------");
        sb.AppendLine($"Kill motors: {killMotors}");
        sb.AppendLine($"Input  Pitch:{pitchCmd,6:0.00}  Roll:{rollCmd,6:0.00}  Yaw:{yawCmd,6:0.00}");
        sb.AppendLine($"Throttle cmd:{throttleCmd,6:0.00}  Throttle:{throttle,6:0.00}");
        sb.AppendLine($"Tilt: {tiltDeg,6:0.0} deg   TiltComp: x{tiltCompFactor,5:0.00}   EffectiveThrottle:{effectiveThrottle,6:0.00}");
        sb.AppendLine($"Thrust total: {lastTotalThrustN,8:0.0} N   (T/W={thrustToWeight:0.00})");
        sb.AppendLine($"Power: {lastPowerW,8:0} W");
        sb.AppendLine();

        sb.AppendLine($"Battery: {battery01 * 100f,6:0.0}%   Energy: {batteryEnergyJ / 3600f,8:0.00} Wh / {batteryCapacityWh:0.00} Wh");
        sb.AppendLine();

        sb.AppendLine($"Alt Y: {transform.position.y,8:0.00} m");
        sb.AppendLine($"Ground speed |V|: {groundSpeed,7:0.00} m/s   Air speed |Vair|: {airSpeed,7:0.00} m/s");
        sb.AppendLine($"Vel:  x:{vel.x,7:0.00} y:{vel.y,7:0.00} z:{vel.z,7:0.00}");
        sb.AppendLine($"AngVel(rad/s): x:{av.x,7:0.00} y:{av.y,7:0.00} z:{av.z,7:0.00}");
        sb.AppendLine($"Euler(deg): Pitch(X):{NormalizeAngle180(eul.x),7:0.0}  Yaw(Y):{NormalizeAngle180(eul.y),7:0.0}  Roll(Z):{NormalizeAngle180(eul.z),7:0.0}");
        sb.AppendLine();

        sb.AppendLine($"Wind vel: x:{lastWindVel.x,7:0.00} y:{lastWindVel.y,7:0.00} z:{lastWindVel.z,7:0.00}");
        sb.AppendLine($"RelAir  : x:{lastRelAirVel.x,7:0.00} y:{lastRelAirVel.y,7:0.00} z:{lastRelAirVel.z,7:0.00}");
        sb.AppendLine();

        sb.AppendLine("Motors (X layout):");
        sb.AppendLine($"  M0 Front-Right: out {motorOut[0],5:0.00}  rpm {motorRPM[0],7:0}");
        sb.AppendLine($"  M1 Front-Left : out {motorOut[1],5:0.00}  rpm {motorRPM[1],7:0}");
        sb.AppendLine($"  M2 Back-Left  : out {motorOut[2],5:0.00}  rpm {motorRPM[2],7:0}");
        sb.AppendLine($"  M3 Back-Right : out {motorOut[3],5:0.00}  rpm {motorRPM[3],7:0}");
        sb.AppendLine();

        sb.AppendLine("Keys:");
        sb.AppendLine("  W/S pitch  A/D roll  Q/E yaw");
        sb.AppendLine("  R/F throttle  X kill motors toggle  F1 telemetry");

        return sb.ToString();
    }

    private float GetBattery01() => Mathf.Clamp01(batteryEnergyJ / batteryEnergyJ0);

    // ----------------------------
    // UTILS
    // ----------------------------
    private static float WrapAngleDeg(float a)
    {
        a %= 360f;
        if (a < 0f) a += 360f;
        return a;
    }

    private static float NormalizeAngle180(float a)
    {
        a = WrapAngleDeg(a);
        if (a > 180f) a -= 360f;
        return a;
    }
}