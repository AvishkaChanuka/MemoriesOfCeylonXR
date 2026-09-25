using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;

/// <summary>
/// MR-adapted enemy base for room-scale gameplay.
/// 
/// KEY DIFFERENCES from PC version:
/// - No Idle / Patrol states — enemy spawns OUTSIDE the room and immediately chases
/// - "Player" position = XR headset (Camera.main.transform) — no rigidbody/CC movement
/// - States: Chase → Attack → Stunned → Dead
/// - DifficultyManager still scales HP / damage / speed / reaction
/// 
/// Subclassed by MeleeEnemy and RangedEnemy.
/// Attach to: Enemy prefab (NavMeshAgent + Animator + Collider)
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
//[RequireComponent(typeof(Animator))]
public abstract class EnemyBase : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  State Enum  (Patrol/Idle removed for MR)
    // ─────────────────────────────────────────────

    public enum EnemyState { Chase, Attack, Strafe, TakeCover, Stunned, Dead }

    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Base Stats (scaled by DifficultyManager)")]
    [SerializeField] protected float baseMaxHealth    = 80f;
    [SerializeField] protected float baseAttackDamage = 15f;
    [SerializeField] protected float baseMoveSpeed    = 3.0f;
    [SerializeField] protected float baseChaseSpeed   = 5.0f;

    [Header("Ranges")]
    [SerializeField] protected float attackRange     = 1.8f;
    [SerializeField] protected float attackCooldown  = 1.5f;
    [SerializeField] protected float stopDistance    = 1.5f;   // NavAgent stopping distance

    [Header("Hit Flash")]
    [SerializeField] private Renderer[] meshRenderers;
    [SerializeField] private Color      hitFlashColor    = Color.red;
    [SerializeField] private float      hitFlashDuration = 0.1f;

    [Header("Audio")]
    [SerializeField] protected AudioClip[] attackSounds;
    [SerializeField] protected AudioClip[] hurtSounds;
    [SerializeField] protected AudioClip   deathSound;
    protected AudioSource _audio;

    // ─────────────────────────────────────────────
    //  Animator Hashes
    // ─────────────────────────────────────────────

    protected static readonly int ANIM_SPEED    = Animator.StringToHash("Speed");
    protected static readonly int ANIM_ATTACK   = Animator.StringToHash("Attack");
    protected static readonly int ANIM_HIT      = Animator.StringToHash("Hit");
    protected static readonly int ANIM_STUNNED  = Animator.StringToHash("IsStunned");
    protected static readonly int ANIM_DEAD     = Animator.StringToHash("IsDead");
    protected static readonly int ANIM_BLOCKING = Animator.StringToHash("IsBlocking");

    // ─────────────────────────────────────────────
    //  Components
    // ─────────────────────────────────────────────

    protected NavMeshAgent _agent;
    [Header("Animator")]
    [SerializeField] protected Animator     _animator;
    protected EnemySensor  _sensor;

    // ─────────────────────────────────────────────
    //  Scaled Stats (written by DifficultyManager)
    // ─────────────────────────────────────────────

    public float MaxHealth    { get; protected set; }
    public float AttackDamage { get; protected set; }
    public float MoveSpeed    { get; protected set; }
    public float ChaseSpeed   { get; protected set; }

    // ─────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────

    public  EnemyState CurrentState  { get; protected set; }
    public  float      CurrentHealth { get; private set; }
    public  bool       IsAlive       { get; private set; } = true;

    /// <summary>
    /// In MR the "player" is the XR headset.
    /// We track Camera.main.transform which Unity XR sets to the head pose.
    /// </summary>
    protected Transform _playerHead;
    protected PlayerStats _playerStats;

    protected float _attackTimer;

    // Hit flash state
    private Color[] _originalColors;
    private bool    _isFlashing;

    // Events
    public event Action<EnemyBase> OnEnemyDied;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    protected virtual void Awake()
    {
        _agent    = GetComponent<NavMeshAgent>();
        //_animator = GetComponent<Animator>();
        _sensor   = GetComponent<EnemySensor>();
        _audio    = GetComponent<AudioSource>();

        // Cache renderer colours for hit flash
        if (meshRenderers != null && meshRenderers.Length > 0)
        {
            _originalColors = new Color[meshRenderers.Length];
            for (int i = 0; i < meshRenderers.Length; i++)
                _originalColors[i] = meshRenderers[i].material.color;
        }
    }

    protected virtual void Start()
    {
        // ── MR: player position = XR camera (headset) ──────────────────
        // Camera.main is automatically set to the XR head camera by Unity XR.
        // If you use XR Origin, this points to the XR Origin > Camera Offset > Main Camera.
        if (Camera.main != null)
            _playerHead = Camera.main.transform;

        // Optional: grab PlayerStats for kill tracking
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _playerStats = playerGO.GetComponent<PlayerStats>();

        // Apply base stats immediately (DifficultyManager will override if present)
        ApplyDifficultySettings(1f, 1f, 1f, 1f);

        _agent.stoppingDistance = stopDistance;

        // ── MR KEY POINT: spawn directly into Chase — no patrol/idle ───
        TransitionToState(EnemyState.Chase);
    }

    protected virtual void Update()
    {
        if (!IsAlive) return;

        _attackTimer -= Time.deltaTime;
        UpdateStateMachine();
        UpdateAnimator();
    }

    // ─────────────────────────────────────────────
    //  Difficulty Scaling API
    // ─────────────────────────────────────────────

    public void ApplyDifficultySettings(
        float healthMult, float damageMult, float speedMult, float reactionMult)
    {
        MaxHealth    = baseMaxHealth    * healthMult;
        AttackDamage = baseAttackDamage * damageMult;
        MoveSpeed    = baseMoveSpeed    * speedMult;
        ChaseSpeed   = baseChaseSpeed   * speedMult;

        if (CurrentHealth <= 0f) CurrentHealth = MaxHealth;
        else                     CurrentHealth  = Mathf.Min(CurrentHealth, MaxHealth);

        attackCooldown = Mathf.Lerp(2.0f, 0.7f, 1f - reactionMult * 0.5f);
    }

    // ─────────────────────────────────────────────
    //  Damage / Death
    // ─────────────────────────────────────────────

    public virtual void TakeDamage(float amount, Vector3 hitSource)
    {
        if (!IsAlive) return;

        CurrentHealth -= amount;
        StartCoroutine(FlashHit());

        if (CurrentHealth <= 0f)
        {
            Die();
        }
        else
        {
            _animator.SetTrigger(ANIM_HIT);
            PlayHurtSound();

            // Heavy hit → brief stun
            if (amount >= MaxHealth * 0.25f)
                StartCoroutine(StunRoutine(0.7f));
        }
    }

    protected virtual void Die()
    {
        if (!IsAlive) return;
        IsAlive = false;

        TransitionToState(EnemyState.Dead);
        _agent.enabled = false;
        _animator.SetBool(ANIM_DEAD, true);
        PlayDeathSound();

        if (_playerStats != null) _playerStats.RecordKill();
        OnEnemyDied?.Invoke(this);

        Destroy(gameObject, 3f);
    }

    // ─────────────────────────────────────────────
    //  State Machine
    // ─────────────────────────────────────────────

    protected void TransitionToState(EnemyState newState)
    {
        OnExitState(CurrentState);
        CurrentState = newState;
        OnEnterState(newState);
    }

    protected virtual void OnEnterState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.Chase:
                _agent.isStopped = false;
                _agent.speed     = ChaseSpeed;
                break;

            case EnemyState.Attack:
                _agent.isStopped = true;
                break;

            case EnemyState.Stunned:
                _agent.isStopped = true;
                _animator.SetBool(ANIM_STUNNED, true);
                break;

            case EnemyState.Dead:
                _agent.isStopped = true;
                break;
        }
    }

    protected virtual void OnExitState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.Stunned:
                _agent.isStopped = false;
                _animator.SetBool(ANIM_STUNNED, false);
                break;

            case EnemyState.Attack:
                _agent.isStopped = false;
                break;
        }
    }

    protected virtual void UpdateStateMachine()
    {
        switch (CurrentState)
        {
            case EnemyState.Chase:  UpdateChase();  break;
            case EnemyState.Attack: UpdateAttack(); break;
        }
    }

    // ─────────────────────────────────────────────
    //  Chase State
    //  → Navigate to the XR headset position
    // ─────────────────────────────────────────────

    protected virtual void UpdateChase()
    {
        if (_playerHead == null) return;

        float dist = DistanceToPlayer();

        // Close enough to attack
        if (dist <= attackRange && _attackTimer <= 0f)
        {
            TransitionToState(EnemyState.Attack);
            return;
        }

        // Navigate to player's feet position (flatten Y to NavMesh level)
        Vector3 target = new Vector3(
            _playerHead.position.x,
            transform.position.y,   // keep enemy on ground
            _playerHead.position.z);

        _agent.SetDestination(target);
        _agent.speed = ChaseSpeed;
    }

    // ─────────────────────────────────────────────
    //  Attack State
    // ─────────────────────────────────────────────

    protected virtual void UpdateAttack()
    {
        if (_playerHead == null) { TransitionToState(EnemyState.Chase); return; }

        FacePlayer();

        float dist = DistanceToPlayer();

        // Player stepped away (physically moved in room)
        if (dist > attackRange * 1.3f)
        {
            TransitionToState(EnemyState.Chase);
            return;
        }

        if (_attackTimer <= 0f)
        {
            PerformAttack();
            _attackTimer = attackCooldown;
        }
    }

    // ─────────────────────────────────────────────
    //  Abstract
    // ─────────────────────────────────────────────

    protected abstract void PerformAttack();

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    protected float DistanceToPlayer()
    {
        if (_playerHead == null) return Mathf.Infinity;
        // Use XZ distance only — player head height varies with crouching/standing
        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(_playerHead.position.x, 0f, _playerHead.position.z);
        return Vector3.Distance(a, b);
    }

    protected void FacePlayer()
    {
        if (_playerHead == null) return;
        Vector3 dir = (_playerHead.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(dir),
            Time.deltaTime * 8f);
    }

    private IEnumerator StunRoutine(float duration)
    {
        TransitionToState(EnemyState.Stunned);
        yield return new WaitForSeconds(duration);
        if (IsAlive) TransitionToState(EnemyState.Chase);
    }

    private IEnumerator FlashHit()
    {
        if (_isFlashing || meshRenderers == null) yield break;
        _isFlashing = true;
        for (int i = 0; i < meshRenderers.Length; i++)
            meshRenderers[i].material.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        for (int i = 0; i < meshRenderers.Length; i++)
            if (meshRenderers[i] != null)
                meshRenderers[i].material.color = _originalColors[i];
        _isFlashing = false;
    }

    // ─────────────────────────────────────────────
    //  Animator
    // ─────────────────────────────────────────────

    protected virtual void UpdateAnimator()
    {
        float speed = _agent.enabled ? _agent.velocity.magnitude / ChaseSpeed : 0f;
        _animator.SetFloat(ANIM_SPEED, speed, 0.1f, Time.deltaTime);
    }

    // ─────────────────────────────────────────────
    //  Audio
    // ─────────────────────────────────────────────

    protected void PlayAttackSound()
    {
        if (_audio == null || attackSounds == null || attackSounds.Length == 0) return;
        _audio.PlayOneShot(attackSounds[UnityEngine.Random.Range(0, attackSounds.Length)]);
    }

    protected void PlayHurtSound()
    {
        if (_audio == null || hurtSounds == null || hurtSounds.Length == 0) return;
        _audio.PlayOneShot(hurtSounds[UnityEngine.Random.Range(0, hurtSounds.Length)]);
    }

    protected void PlayDeathSound()
    {
        if (_audio == null || deathSound == null) return;
        _audio.PlayOneShot(deathSound);
    }

    // ─────────────────────────────────────────────
    //  Gizmos
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
    }
}
