using UnityEngine;

/// <summary>
/// VortexSystem — autonomous tornado simulation with EF-scale intensity,
/// pressure falloff, random roaming, optional target tracking,
/// and a spring-column funnel solver.
/// </summary>
/// 
/// Initial Implementation Informed by Sharp Coder Blog and Erik Nordeus
public class VortexSystem : MonoBehaviour
{
    [Header("Scene References")]
    public GameObject columnSegmentPrefab;
    public GameObject funnelParticleObj;
    public GameObject groundDustObj;
    public GameObject apexParticleObj;
    public GameObject debrisObj;

    [Header("Optional Tracking")]
    public Transform trackingTarget;
    [Range(0f, 1f)] public float trackingWeight = 0.45f;
    public float trackingRange = 120f;

    [Header("Debug")]
    public bool showDebugGizmos = false;
    public bool drawMovementTargets = true;

    [Header("EF Scale (0 = EF0 ... 5 = EF5)")]
    [Range(0, 5)]
    public int efRating = 2;

    private static readonly float[] efWindTable = { 30f, 45f, 60f, 75f, 90f, 110f };
    public float WindSpeedMS => efWindTable[Mathf.Clamp(efRating, 0, 5)];
    private float Intensity => efRating / 5f;

    [Header("Funnel Geometry")]
    public float funnelHeight = 55f;
    public float groundContactRadius = 4f;
    public float apexRadius = 22f;
    public AnimationCurve funnelProfile;

    [Header("Column Segments")]
    public int segmentCount = 12;

    [Header("Movement")]
    public float movementSpeed = 8f;
    public float turnRate = 2.2f;
    public float wanderRadius = 90f;
    public float wanderRetargetDistance = 10f;
    public float wanderRetargetInterval = 4f;
    public Vector2 worldBoundsX = new Vector2(-250f, 250f);
    public Vector2 worldBoundsZ = new Vector2(-250f, 250f);

    [Header("Upper Funnel Guide")]
    public float guideOffsetDistance = 30f;
    public float guideLateralNoise = 18f;
    public float guideLateralSpeed = 0.45f;
    public float guideLiftBias = 0.65f;

    [Header("Column Physics")]
    public float springStiffness = 2.2f;
    public float dampingCoeff = 0.25f;
    public float segmentMass = 1f;
    public float baseAnchorStrength = 1.8f;
    public float upperGuideStrength = 1.05f;
    public float neighbourInfluence = 1f;

    [Header("Atmospheric Turbulence")]
    public int noiseSeed = 7391;
    public float columnDriftScale = 0.14f;
    public float columnDriftStrength = 4f;
    public float apexLeanRadius = 5f;
    public float twistRate = 1.1f;
    public float pulseAmount = 0.14f;
    public float pulseSpeed = 0.7f;

    [Header("Pressure Model")]
    public float ambientPressure = 1013f;
    public float maxPressureDrop = 100f;

    [System.NonSerialized] public Transform[] segments;

    private Vector3[] _positionsCurrent;
    private Vector3[] _positionsPrevious;
    private Vector3[] _velocities;

    private float _noiseOffsetX;
    private float _noiseOffsetZ;
    private float _pulseOffset;
    private float[] _segmentPhase;
    private float[] _segmentRadiusMult;

    private Vector3 _baseAnchor;
    private Vector3 _wanderTarget;
    private Vector3 _currentMoveDirection = Vector3.forward;
    private Vector3 _upperGuidePoint;

    private float _nextWanderPickTime;

    public float CentralPressure => ambientPressure - maxPressureDrop * Intensity;

    public float GetFunnelRadius(float height01)
    {
        float baseRadius = Mathf.Lerp(
            groundContactRadius,
            apexRadius,
            funnelProfile.Evaluate(Mathf.Clamp01(height01))
        );

        float efScale = 0.6f + 0.4f * Intensity;
        return baseRadius * efScale;
    }

    public float GetPressureAtRadius(float distanceFromCentre)
    {
        float coreRadius = GetFunnelRadius(0f);
        float pressureDrop = maxPressureDrop * Intensity;

        if (distanceFromCentre <= coreRadius)
            return ambientPressure - pressureDrop;

        float falloff = Mathf.Clamp01(coreRadius / Mathf.Max(distanceFromCentre, 0.001f));
        return ambientPressure - pressureDrop * falloff * falloff;
    }

    void Start()
    {
        Random.InitState(noiseSeed);

        _noiseOffsetX = Random.Range(0f, 1000f);
        _noiseOffsetZ = Random.Range(1000f, 2000f);
        _pulseOffset = Random.Range(0f, 10f);

        _baseAnchor = transform.position;
        _baseAnchor.y = 0f;

        BuildColumn();

        int n = segmentCount;
        _positionsCurrent = new Vector3[n];
        _positionsPrevious = new Vector3[n];
        _velocities = new Vector3[n];
        _segmentPhase = new float[n];
        _segmentRadiusMult = new float[n];

        for (int i = 0; i < n; i++)
        {
            _positionsCurrent[i] = segments[i].position;
            _positionsPrevious[i] = segments[i].position;
            _velocities[i] = Vector3.zero;
            _segmentPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            _segmentRadiusMult[i] = Random.Range(0.8f, 1.2f);
        }

        PickNewWanderTarget(true);
        _upperGuidePoint = _baseAnchor + transform.forward * guideOffsetDistance;

        BindParticleSystem(funnelParticleObj);
        BindParticleSystem(debrisObj);
        BindApexSystem();
    }

    void Update()
    {
        UpdateAutonomousMovement();
        UpdateUpperGuide();
        SyncParticleSpinSpeeds();
    }

    void FixedUpdate()
    {
        SolveColumnDynamics();
    }

    void BuildColumn()
    {
        segments = new Transform[segmentCount];
        float segH = funnelHeight / Mathf.Max(segmentCount, 1);

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = Instantiate(columnSegmentPrefab, transform);

            Vector3 pos = transform.position;
            pos.y += i * segH;
            seg.transform.position = pos;

            float h01 = (segmentCount > 1) ? (float)i / (segmentCount - 1) : 0f;
            float r = GetFunnelRadius(h01);

            Vector3 s = seg.transform.localScale;
            s.x = r * 2f;
            s.z = segH / 2f;
            seg.transform.localScale = s;

            segments[i] = seg.transform;
        }
    }

    void UpdateAutonomousMovement()
    {
        Vector3 flatPos = _baseAnchor;
        flatPos.y = 0f;

        bool needsRetarget =
            Time.time >= _nextWanderPickTime ||
            Vector3.Distance(flatPos, _wanderTarget) <= wanderRetargetDistance ||
            IsNearBounds(flatPos, 8f);

        if (needsRetarget)
        {
            PickNewWanderTarget(false);
        }

        Vector3 desiredDirection = (_wanderTarget - flatPos);
        desiredDirection.y = 0f;

        if (trackingTarget != null)
        {
            Vector3 toTrack = trackingTarget.position - flatPos;
            toTrack.y = 0f;

            float dist = toTrack.magnitude;
            if (dist <= trackingRange && dist > 0.01f)
            {
                float rangeFactor = 1f - Mathf.Clamp01(dist / trackingRange);
                float blend = trackingWeight * rangeFactor;
                desiredDirection = Vector3.Slerp(
                    desiredDirection.normalized,
                    toTrack.normalized,
                    blend
                ) * desiredDirection.magnitude;
            }
        }

        if (desiredDirection.sqrMagnitude > 0.001f)
        {
            Vector3 desiredForward = desiredDirection.normalized;
            _currentMoveDirection = Vector3.Slerp(
                _currentMoveDirection,
                desiredForward,
                turnRate * Time.deltaTime
            ).normalized;
        }

        float speedMod = Mathf.Lerp(0.75f, 1.25f, Intensity);
        _baseAnchor += _currentMoveDirection * movementSpeed * speedMod * Time.deltaTime;
        _baseAnchor = ClampToBounds(_baseAnchor);

        if (_currentMoveDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(_currentMoveDirection, Vector3.up);
        }

        transform.position = new Vector3(_baseAnchor.x, transform.position.y, _baseAnchor.z);
    }

    void UpdateUpperGuide()
    {
        float t = Time.time * guideLateralSpeed;

        Vector3 rightNoise = transform.right *
            ((Mathf.PerlinNoise(_noiseOffsetX, t) - 0.5f) * 2f * guideLateralNoise);

        Vector3 forwardNoise = transform.forward *
            ((Mathf.PerlinNoise(_noiseOffsetZ, t) - 0.5f) * 2f * guideLateralNoise * 0.5f);

        Vector3 forwardBias = _currentMoveDirection.normalized * guideOffsetDistance * guideLiftBias;

        _upperGuidePoint = _baseAnchor + forwardBias + rightNoise + forwardNoise;
        _upperGuidePoint.y = 0f;
    }

    void PickNewWanderTarget(bool forceFar)
    {
        Vector2 centre = new Vector2(_baseAnchor.x, _baseAnchor.z);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector2 candidate = centre + offset;

            candidate.x = Mathf.Clamp(candidate.x, worldBoundsX.x, worldBoundsX.y);
            candidate.y = Mathf.Clamp(candidate.y, worldBoundsZ.x, worldBoundsZ.y);

            Vector3 candidate3 = new Vector3(candidate.x, 0f, candidate.y);

            if (!forceFar || Vector3.Distance(candidate3, _baseAnchor) > wanderRetargetDistance * 1.5f)
            {
                _wanderTarget = candidate3;
                _nextWanderPickTime = Time.time + wanderRetargetInterval + Random.Range(0.5f, 2f);
                return;
            }
        }

        _wanderTarget = _baseAnchor + transform.forward * wanderRadius * 0.5f;
        _wanderTarget = ClampToBounds(_wanderTarget);
        _nextWanderPickTime = Time.time + wanderRetargetInterval;
    }

    bool IsNearBounds(Vector3 pos, float margin)
    {
        return pos.x <= worldBoundsX.x + margin ||
               pos.x >= worldBoundsX.y - margin ||
               pos.z <= worldBoundsZ.x + margin ||
               pos.z >= worldBoundsZ.y - margin;
    }

    Vector3 ClampToBounds(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, worldBoundsX.x, worldBoundsX.y);
        pos.z = Mathf.Clamp(pos.z, worldBoundsZ.x, worldBoundsZ.y);
        pos.y = 0f;
        return pos;
    }

    void SolveColumnDynamics()
    {
        float dt = 0.02f;
        float time = Time.time;
        float pulse = 1f + Mathf.Sin(time * pulseSpeed + _pulseOffset) * pulseAmount;

        Vector3 groundPos = _baseAnchor;
        groundPos.y = 0f;

        Vector3 guidePos = _upperGuidePoint;
        guidePos.y = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            float h01 = (segmentCount > 1) ? (float)i / (segmentCount - 1) : 0f;

            float lowerAnchor = Mathf.Lerp(baseAnchorStrength, 0.2f, h01);
            float upperInfluence = Mathf.Lerp(0.15f, upperGuideStrength, h01);

            Vector3 prevPos = _positionsPrevious[i];
            Vector3 accel = Vector3.zero;

            float nx = Mathf.PerlinNoise(_noiseOffsetX + i * columnDriftScale, time * 0.32f + _segmentPhase[i]) - 0.5f;
            float nz = Mathf.PerlinNoise(_noiseOffsetZ + i * columnDriftScale, time * 0.32f + _segmentPhase[i]) - 0.5f;
            Vector3 driftOffset = new Vector3(nx, 0f, nz) * columnDriftStrength * pulse * Mathf.Lerp(0.35f, 1.2f, h01);

            float angle = time * (0.5f + h01 * twistRate) + _segmentPhase[i];
            float leanRadius = h01 * apexLeanRadius * _segmentRadiusMult[i] * (0.5f + Intensity * 0.5f);
            Vector3 leanOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * leanRadius;

            Vector3 blendedTarget = Vector3.Lerp(groundPos, guidePos, h01) + driftOffset + leanOffset;

            if (i == 0)
            {
                accel += (-springStiffness * (prevPos - _positionsPrevious[i + 1]) * neighbourInfluence) / segmentMass;
                accel += (-springStiffness * (prevPos - groundPos) * lowerAnchor) / segmentMass;
            }
            else if (i == segmentCount - 1)
            {
                accel += (springStiffness * (_positionsPrevious[i - 1] - prevPos) * neighbourInfluence) / segmentMass;
                accel += (-springStiffness * (prevPos - blendedTarget) * upperInfluence) / segmentMass;
            }
            else
            {
                accel += (-springStiffness * (prevPos - _positionsPrevious[i - 1]) * neighbourInfluence) / segmentMass;
                accel += (springStiffness * (_positionsPrevious[i + 1] - prevPos) * neighbourInfluence) / segmentMass;
                accel += (blendedTarget - prevPos) * (0.28f + 0.18f * h01);
            }

            accel -= (dampingCoeff * _velocities[i]) / segmentMass;

            _positionsCurrent[i] = prevPos + dt * _velocities[i];
            _positionsCurrent[i].y = _positionsPrevious[i].y;

            _velocities[i] += dt * accel;
        }

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p = _positionsCurrent[i];
            p.y = segments[i].position.y;
            segments[i].position = p;
            _positionsPrevious[i] = _positionsCurrent[i];

            float h01 = (segmentCount > 1) ? (float)i / (segmentCount - 1) : 0f;
            float r = GetFunnelRadius(h01);
            Vector3 s = segments[i].localScale;
            s.x = r * 2f;
            segments[i].localScale = s;
        }

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 lookTarget = (i > 0)
                ? segments[i - 1].position
                : segments[i].position + Vector3.down;
            segments[i].LookAt(lookTarget);
        }

        if (groundDustObj != null)
        {
            Vector3 baseEmitPos = _positionsCurrent[0];
            baseEmitPos.y = 0f;
            groundDustObj.transform.position = baseEmitPos;
        }

        if (apexParticleObj != null)
        {
            Vector3 apexEmitPos = _positionsCurrent[segmentCount - 1];
            apexEmitPos.y = funnelHeight;
            apexParticleObj.transform.position = apexEmitPos;
        }
    }

    void BindParticleSystem(GameObject psObj)
    {
        if (psObj == null) return;

        VortexDust script = psObj.GetComponent<VortexDust>();
        if (script == null) return;

        script.columnSegments = segments;
        script.funnelProfile = funnelProfile;
        script.funnelHeight = funnelHeight;
        script.apexRadius = apexRadius;
        script.vortexTransform = transform;
        script.spinSpeed = WindSpeedMS * 0.04f;
        script.efRating = efRating;
    }

    void BindApexSystem()
    {
        if (apexParticleObj == null) return;

        VortexApex apex = apexParticleObj.GetComponent<VortexApex>();
        if (apex == null) return;

        apex.baseRadius = apexRadius;
        apex.spinSpeed = WindSpeedMS * 0.04f;
        apex.funnelHeight = funnelHeight;
    }

    void SyncParticleSpinSpeeds()
    {
        float spin = WindSpeedMS * 0.04f;

        if (groundDustObj != null)
        {
            GroundVortex gv = groundDustObj.GetComponent<GroundVortex>();
            if (gv != null) gv.spinSpeed = spin;
        }

        if (funnelParticleObj != null)
        {
            VortexDust vd = funnelParticleObj.GetComponent<VortexDust>();
            if (vd != null)
            {
                vd.spinSpeed = spin;
                vd.efRating = efRating;
            }
        }

        if (debrisObj != null)
        {
            VortexDust dd = debrisObj.GetComponent<VortexDust>();
            if (dd != null)
            {
                dd.spinSpeed = spin;
                dd.efRating = efRating;
            }
        }

        if (apexParticleObj != null)
        {
            VortexApex apex = apexParticleObj.GetComponent<VortexApex>();
            if (apex != null) apex.spinSpeed = spin;
        }
    }

    public void SetEFRating(int rating)
    {
        efRating = Mathf.Clamp(rating, 0, 5);
    }

    public bool IsInsideDamageRadius(Vector3 worldPos)
    {
        Vector3 base2D = new Vector3(_baseAnchor.x, 0f, _baseAnchor.z);
        Vector3 pos2D = new Vector3(worldPos.x, 0f, worldPos.z);
        return Vector3.Distance(base2D, pos2D) <= GetFunnelRadius(0f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        Vector3 basePos = Application.isPlaying ? _baseAnchor : transform.position;
        basePos.y = 0f;

        Gizmos.color = Color.red;
        DrawCircleGizmo(basePos, GetFunnelRadius(0f));

        Gizmos.color = Color.cyan;
        Vector3 apexPos = basePos + Vector3.up * funnelHeight;
        DrawCircleGizmo(apexPos, GetFunnelRadius(1f));

        Gizmos.color = Color.Lerp(Color.green, Color.red, Intensity);
        Gizmos.DrawSphere(basePos + Vector3.up * (funnelHeight * 0.5f), 1.5f);

        if (drawMovementTargets && Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_wanderTarget + Vector3.up * 2f, 1.2f);
            Gizmos.DrawLine(basePos + Vector3.up, _wanderTarget + Vector3.up);

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_upperGuidePoint + Vector3.up * 2f, 1.0f);
            Gizmos.DrawLine(basePos + Vector3.up * 2f, _upperGuidePoint + Vector3.up * 2f);

            if (trackingTarget != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(basePos + Vector3.up * 3f, trackingTarget.position);
            }
        }
    }

    void DrawCircleGizmo(Vector3 centre, float radius)
    {
        int steps = 32;
        Vector3 prev = centre + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}