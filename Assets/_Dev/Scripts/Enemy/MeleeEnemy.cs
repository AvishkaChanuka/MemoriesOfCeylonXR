using UnityEngine;
using System.Collections;

/// <summary>
/// MR Melee Enemy — enters the room and closes in on the player (XR headset).
/// 
/// Behaviour flow:
///   Spawn outside room → Chase (NavMesh pathfind to headset XZ) →
///   Attack (combo hits via overlap sphere) → Stunned (on heavy hit) → Dead
/// 
/// MR Notes:
/// - Player moves physically; distance check uses XZ plane only
/// - Attack hitbox should overlap the player's physical space (room bounds)
/// - Can block incoming hits (XR controller strikes)
/// </summary>
public class MeleeEnemy : EnemyBase
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Melee Attack")]
    [SerializeField] private float  meleeHitboxRadius  = 1.0f;
    [SerializeField] private float  meleeHitboxOffset  = 0.9f;   // forward from enemy
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private int    comboMaxHits       = 2;
    [SerializeField] private float  comboPauseDuration = 0.4f;

    [Header("Block")]
    [SerializeField] private bool   canBlock        = true;
    [SerializeField] private float  blockChance     = 0.30f;
    [SerializeField] private float  blockDuration   = 1.0f;
    [SerializeField] private float  blockCooldown   = 4.0f;

    [Header("Charge Attack (optional)")]
    [SerializeField] private bool   canCharge       = false;
    [SerializeField] private float  chargeDistance  = 6f;
    [SerializeField] private float  chargeSpeed     = 10f;
    [SerializeField] private float  chargeDuration  = 0.45f;

    // ─────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────

    private bool  _isBlocking;
    private float _blockTimer;
    private float _blockCooldownTimer;
    private bool  _isCharging;

    // ─────────────────────────────────────────────
    //  Overrides
    // ─────────────────────────────────────────────

    protected override void Update()
    {
        base.Update();

        if (_isBlocking)
        {
            _blockTimer -= Time.deltaTime;
            if (_blockTimer <= 0f)
            {
                _isBlocking = false;
                _animator.SetBool(ANIM_BLOCKING, false);
                _blockCooldownTimer = blockCooldown;
            }
        }

        if (_blockCooldownTimer > 0f)
            _blockCooldownTimer -= Time.deltaTime;
    }

    protected override void UpdateChase()
    {
        if (_playerHead == null) return;

        float dist = DistanceToPlayer();

        // Charge if far enough and off cooldown
        if (canCharge && !_isCharging && dist >= chargeDistance && _attackTimer <= 0f)
        {
            StartCoroutine(ChargeAttack());
            return;
        }

        if (dist <= attackRange && _attackTimer <= 0f)
        {
            TransitionToState(EnemyState.Attack);
            return;
        }

        // Head to XZ position of the headset (stays on ground)
        Vector3 target = new Vector3(_playerHead.position.x, transform.position.y, _playerHead.position.z);
        _agent.SetDestination(target);
        _agent.speed = ChaseSpeed;
    }

    protected override void UpdateAttack()
    {
        if (_playerHead == null) { TransitionToState(EnemyState.Chase); return; }

        FacePlayer();

        float dist = DistanceToPlayer();
        if (dist > attackRange * 1.3f)
        {
            TransitionToState(EnemyState.Chase);
            return;
        }

        if (_attackTimer <= 0f && !_isBlocking)
        {
            StartCoroutine(ComboAttack());
            _attackTimer = attackCooldown;
        }
    }

    // ─────────────────────────────────────────────
    //  Melee Combo
    // ─────────────────────────────────────────────

    protected override void PerformAttack() { /* handled by UpdateAttack coroutine */ }

    private IEnumerator ComboAttack()
    {
        PlayAttackSound();
        for (int hit = 0; hit < comboMaxHits; hit++)
        {
            if (!IsAlive) yield break;

            _animator.SetTrigger(ANIM_ATTACK);
            yield return new WaitForSeconds(0.18f);   // sync to animation swing peak

            // Overlap sphere in front of enemy
            Vector3 origin = transform.position
                             + transform.forward * meleeHitboxOffset
                             + Vector3.up * 0.9f;

            Collider[] hits = Physics.OverlapSphere(origin, meleeHitboxRadius, playerLayer);
            foreach (Collider col in hits)
            {
                PlayerCombat pc = col.GetComponentInParent<PlayerCombat>();
                if (pc != null)
                    pc.OnHit(AttackDamage, transform.position);
            }

            if (hit < comboMaxHits - 1)
                yield return new WaitForSeconds(comboPauseDuration);
        }
    }

    // ─────────────────────────────────────────────
    //  Charge
    // ─────────────────────────────────────────────

    private IEnumerator ChargeAttack()
    {
        _isCharging     = true;
        float orig      = _agent.speed;
        _agent.speed    = chargeSpeed;
        _attackTimer    = attackCooldown * 1.5f;

        float elapsed = 0f;
        while (elapsed < chargeDuration && IsAlive)
        {
            if (_playerHead != null)
            {
                Vector3 t = new Vector3(_playerHead.position.x, transform.position.y, _playerHead.position.z);
                _agent.SetDestination(t);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (DistanceToPlayer() <= attackRange * 1.5f)
        {
            PlayerCombat pc = _playerHead?.GetComponentInParent<PlayerCombat>();
            if (pc == null && _playerHead != null)
                pc = _playerHead.GetComponent<PlayerCombat>();
            pc?.OnHit(AttackDamage * 1.5f, transform.position);
        }

        _agent.speed = orig;
        _isCharging  = false;
    }

    // ─────────────────────────────────────────────
    //  Block (react to XR controller hits)
    // ─────────────────────────────────────────────

    public override void TakeDamage(float amount, Vector3 hitSource)
    {
        if (canBlock && !_isBlocking && _blockCooldownTimer <= 0f
            && Random.value < blockChance)
        {
            StartCoroutine(BlockReaction());
            amount *= 0.2f;
        }
        base.TakeDamage(amount, hitSource);
    }

    private IEnumerator BlockReaction()
    {
        _isBlocking  = true;
        _blockTimer  = blockDuration;
        _animator.SetBool(ANIM_BLOCKING, true);
        _agent.isStopped = true;
        yield return new WaitForSeconds(0.25f);
        if (IsAlive) _agent.isStopped = false;
    }

    // ─────────────────────────────────────────────
    //  Gizmos
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Vector3 o = transform.position + transform.forward * meleeHitboxOffset + Vector3.up * 0.9f;
        Gizmos.DrawWireSphere(o, meleeHitboxRadius);
        if (canCharge)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, chargeDistance);
        }
    }
}
