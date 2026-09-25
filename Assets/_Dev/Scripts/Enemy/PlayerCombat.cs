using UnityEngine;
using System.Collections;

/// <summary>
/// Player combat for Meta All-in-One SDK (OVRInput).
/// 
/// Controls:
///   Right Index Trigger (press) → Melee Attack
///   Left Grip (hold)            → Block
///   Heavy physical swing        → handled by EnemyBase.TakeDamage() from your weapon collider
/// 
/// Requires: PlayerStats on the same GameObject (OVRCameraRig root)
/// Attach to: OVRCameraRig root GameObject
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerCombat : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Attack Settings")]
    [SerializeField] private float attackDamage     = 25f;
    [SerializeField] private float attackRange      = 1.2f;       // melee reach in metres
    [SerializeField] private float attackRate       = 1.2f;       // attacks per second
    [SerializeField] private LayerMask enemyLayer;

    [Header("Attack Origin")]
    [SerializeField] private Transform rightHandAnchor;            // assign OVRCameraRig > RightHandAnchor
    [SerializeField] private Transform leftHandAnchor;             // assign OVRCameraRig > LeftHandAnchor

    [Header("OVR Input Mapping")]
    [Tooltip("Button that triggers an attack on the right controller")]
    [SerializeField] private OVRInput.RawButton attackButton  = OVRInput.RawButton.RIndexTrigger;
    [Tooltip("Button held on left controller to block")]
    [SerializeField] private OVRInput.RawButton blockButton   = OVRInput.RawButton.LHandTrigger;

    [Header("Hit Reaction")]
    [SerializeField] private float hitStunDuration = 0.3f;

    [Header("Haptics")]
    [SerializeField] private float hitHapticFreq      = 0.5f;
    [SerializeField] private float hitHapticAmplitude = 0.8f;
    [SerializeField] private float hitHapticDuration  = 0.15f;

    [Header("Audio")]
    [SerializeField] private AudioClip[] swingSounds;
    [SerializeField] private AudioClip   hitSound;
    [SerializeField] private AudioClip   blockSound;
    [SerializeField] private AudioClip   deathSound;
    private AudioSource _audio;

    // ─────────────────────────────────────────────
    //  Animator Hashes
    // ─────────────────────────────────────────────

    private static readonly int ANIM_ATTACK  = Animator.StringToHash("Attack");
    private static readonly int ANIM_HIT     = Animator.StringToHash("Hit");
    private static readonly int ANIM_BLOCK   = Animator.StringToHash("IsBlocking");
    private static readonly int ANIM_DIE     = Animator.StringToHash("Die");

    // ─────────────────────────────────────────────
    //  Components
    // ─────────────────────────────────────────────

    private PlayerStats _stats;
    private Animator    _animator;   // optional — on a body/avatar mesh

    // ─────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────

    public bool IsAttacking  { get; private set; }
    public bool IsBlocking   { get; private set; }
    public bool IsDead       { get; private set; }

    private float _nextAttackTime;
    private bool  _isInHitStun;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        _stats    = GetComponent<PlayerStats>();
        _animator = GetComponentInChildren<Animator>();   // body avatar, if present
        _audio    = GetComponent<AudioSource>();

        _stats.OnDeath += HandleDeath;

        // Auto-find Meta hand anchors if not assigned
        if (rightHandAnchor == null || leftHandAnchor == null)
            AutoFindHandAnchors();
    }

    private void Update()
    {
        if (!_stats.IsAlive || _isInHitStun || IsDead) return;

        HandleAttackInput();
        HandleBlockInput();
    }

    private void OnDestroy()
    {
        if (_stats != null) _stats.OnDeath -= HandleDeath;
    }

    // ─────────────────────────────────────────────
    //  Attack  — Right Index Trigger
    // ─────────────────────────────────────────────

    private void HandleAttackInput()
    {
        if (!OVRInput.GetDown(attackButton)) return;
        if (Time.time < _nextAttackTime)     return;
        if (!_stats.UseStamina(_stats.AttackStaminaCost)) return;

        IsAttacking     = true;
        _nextAttackTime = Time.time + (1f / attackRate);

        _animator?.SetTrigger(ANIM_ATTACK);
        PlaySwingSound();
        TriggerHaptic(OVRInput.Controller.RTouch, 0.3f, 0.5f, 0.08f);

        StartCoroutine(PerformAttack());
    }

    private IEnumerator PerformAttack()
    {
        yield return new WaitForSeconds(0.12f);   // brief delay to sync with hand motion

        // Use right hand position as attack origin
        Vector3 origin = rightHandAnchor != null
            ? rightHandAnchor.position
            : Camera.main.transform.position + Camera.main.transform.forward * 0.5f;

        Collider[] hits = Physics.OverlapSphere(origin, attackRange, enemyLayer);
        bool didHit = false;

        foreach (Collider col in hits)
        {
            EnemyBase enemy = col.GetComponentInParent<EnemyBase>();
            if (enemy != null && enemy.IsAlive)
            {
                enemy.TakeDamage(attackDamage, origin);
                _stats.RecordDamageDealt(attackDamage);
                didHit = true;
            }
        }

        if (didHit)
        {
            PlayHitSound();
            // Strong haptic feedback when landing a hit
            TriggerHaptic(OVRInput.Controller.RTouch,
                hitHapticFreq, hitHapticAmplitude, hitHapticDuration);
        }

        yield return new WaitForSeconds(0.3f);
        IsAttacking = false;
    }

    // ─────────────────────────────────────────────
    //  Block  — Left Grip (hold)
    // ─────────────────────────────────────────────

    private void HandleBlockInput()
    {
        bool wasBlocking = IsBlocking;
        IsBlocking = OVRInput.Get(blockButton) && _stats.HasStamina(1f);
        _stats.IsBlocking = IsBlocking;

        _animator?.SetBool(ANIM_BLOCK, IsBlocking);

        // Drain stamina while holding block
        if (IsBlocking)
            _stats.UseStamina(10f * Time.deltaTime);

        // One-shot haptic + sound on block start
        if (IsBlocking && !wasBlocking)
        {
            PlayBlockSound();
            TriggerHaptic(OVRInput.Controller.LTouch, 0.3f, 0.6f, 0.1f);
        }
    }

    // ─────────────────────────────────────────────
    //  Receive Hit (called by enemy scripts)
    // ─────────────────────────────────────────────

    public void OnHit(float damage, Vector3 hitSource)
    {
        if (IsDead || _isInHitStun) return;

        _stats.TakeDamage(damage);

        if (_stats.IsAlive)
        {
            _animator?.SetTrigger(ANIM_HIT);
            // Haptic on both controllers to signal getting hit
            TriggerHaptic(OVRInput.Controller.All, 0.5f, 1.0f, 0.2f);
            StartCoroutine(HitStunRoutine());
        }
    }

    private IEnumerator HitStunRoutine()
    {
        _isInHitStun = true;
        yield return new WaitForSeconds(hitStunDuration);
        _isInHitStun = false;
    }

    // ─────────────────────────────────────────────
    //  Death
    // ─────────────────────────────────────────────

    private void HandleDeath()
    {
        if (IsDead) return;
        IsDead      = true;
        IsAttacking = false;
        IsBlocking  = false;

        _animator?.SetTrigger(ANIM_DIE);
        PlayDeathSound();
        // Trigger sustained rumble on death
        TriggerHaptic(OVRInput.Controller.All, 0.5f, 1.0f, 0.5f);

        Debug.Log("[PlayerCombat] Player died.");
        // Invoke your GameManager / game-over logic here
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Auto-find Meta hand anchors from OVRCameraRig hierarchy.
    /// Hierarchy: OVRCameraRig > TrackingSpace > RightHandAnchor / LeftHandAnchor
    /// </summary>
    private void AutoFindHandAnchors()
    {
        OVRCameraRig rig = GetComponent<OVRCameraRig>();
        if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
        if (rig == null) return;

        if (rightHandAnchor == null) rightHandAnchor = rig.rightHandAnchor;
        if (leftHandAnchor  == null) leftHandAnchor  = rig.leftHandAnchor;

        if (rightHandAnchor != null)
            Debug.Log("[PlayerCombat] Auto-found right hand anchor: " + rightHandAnchor.name);
    }

    /// <summary>Send haptic impulse to a Meta Touch controller.</summary>
    private void TriggerHaptic(OVRInput.Controller controller,
        float frequency, float amplitude, float duration)
    {
        OVRInput.SetControllerVibration(frequency, amplitude, controller);
        StartCoroutine(StopHapticAfter(controller, duration));
    }

    private IEnumerator StopHapticAfter(OVRInput.Controller controller, float duration)
    {
        yield return new WaitForSeconds(duration);
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    // ─────────────────────────────────────────────
    //  Audio
    // ─────────────────────────────────────────────

    private void PlaySwingSound()
    {
        if (_audio == null || swingSounds == null || swingSounds.Length == 0) return;
        _audio.PlayOneShot(swingSounds[Random.Range(0, swingSounds.Length)]);
    }

    private void PlayHitSound()
    {
        if (_audio == null || hitSound == null) return;
        _audio.PlayOneShot(hitSound);
    }

    private void PlayBlockSound()
    {
        if (_audio == null || blockSound == null) return;
        _audio.PlayOneShot(blockSound);
    }

    private void PlayDeathSound()
    {
        if (_audio == null || deathSound == null) return;
        _audio.PlayOneShot(deathSound);
    }

    // ─────────────────────────────────────────────
    //  Gizmos
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (rightHandAnchor == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(rightHandAnchor.position, attackRange);
    }
}
