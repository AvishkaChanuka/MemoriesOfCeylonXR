using UnityEngine;

/// <summary>
/// MR-adapted Enemy Sensor.
/// 
/// In room-scale MR the enemy always "knows" where the player is (they're
/// right in front of them in the physical room). This sensor is simplified:
/// 
/// - No FOV cone needed — enemy is already in the room and player is stationary
/// - Hearing range: always detects within radius (player makes no "noise" in MR)
/// - Line-of-sight check: used by RangedEnemy to decide if it can shoot
/// - All detection is based on Camera.main (XR headset) position
/// 
/// Attach to: same GameObject as EnemyBase
/// </summary>
public class EnemySensor : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Enemy always detects player within this radius (XZ distance).")]
    [SerializeField] private float detectionRadius = 20f;

    [Header("Line of Sight (used by RangedEnemy)")]
    [SerializeField] private LayerMask obstacleLayers;
    [SerializeField] private float     eyeHeight = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;

    // ─────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────

    public bool  PlayerDetected    { get; private set; }
    public float DistanceToPlayer  { get; private set; }

    private Transform _headCamera;   // XR headset = Camera.main

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Start()
    {
        // In Unity XR, Camera.main is automatically the headset camera
        if (Camera.main != null)
            _headCamera = Camera.main.transform;
        else
            Debug.LogWarning("[EnemySensor] Camera.main not found. " +
                             "Ensure your XR Origin has a Camera tagged MainCamera.");
    }

    private void Update()
    {
        if (_headCamera == null) return;

        // XZ-only distance (ignore crouching height)
        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(_headCamera.position.x, 0f, _headCamera.position.z);
        DistanceToPlayer = Vector3.Distance(a, b);

        PlayerDetected = DistanceToPlayer <= detectionRadius;
    }

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────

    /// <summary>True if the player (headset) is within detection radius.</summary>
    public bool CanSeePlayer() => PlayerDetected;

    /// <summary>
    /// Raycast from eye to headset — used by RangedEnemy before firing.
    /// Returns true if nothing blocks the line of sight.
    /// </summary>
    public bool HasClearShot()
    {
        if (_headCamera == null) return false;
        Vector3 eye    = transform.position + Vector3.up * eyeHeight;
        Vector3 target = _headCamera.position;
        return !Physics.Linecast(eye, target, obstacleLayers);
    }

    // ─────────────────────────────────────────────
    //  Gizmos
    // ─────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;
        Gizmos.color = PlayerDetected
            ? new Color(0f, 1f, 0f, 0.2f)
            : new Color(1f, 1f, 0f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
