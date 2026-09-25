using UnityEngine;
using System.Collections;

/// <summary>
/// MR Ranged Enemy — enters the room from outside and attacks the player (XR headset).
/// 
/// Behaviour flow:
///   Spawn outside room → Chase (close in to optimal range) →
///   Strafe (circle the play space while firing) →
///   Cover (retreat if low HP) → Dead
/// 
/// MR Notes:
/// - Optimal range keeps enemy within the room / arms reach radius
/// - Strafe respects play space boundary — use coverLayer for room walls
/// - Lead target predicts headset movement for harder difficulties
/// </summary>
public class RangedEnemy : EnemyBase
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Ranged Settings")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform  firePoint;              // muzzle position
    [SerializeField] private float      projectileSpeed   = 16f;
    [SerializeField] private float      optimalRange      = 2.5f;  // comfortable MR room distance
    [SerializeField] private float      minRange          = 1.0f;  // too close — back off
    [SerializeField] private float      spread            = 1.5f;  // accuracy (lower = more accurate)

    [Header("Burst Fire")]
    [SerializeField] private bool  useBurstFire  = false;
    [SerializeField] private int   burstCount    = 3;
    [SerializeField] private float burstFireRate = 0.15f;

    [Header("Strafe (circling the play space)")]
    [SerializeField] private float strafeSpeed       = 1.8f;
    [SerializeField] private float strafeChangeTime  = 2.0f;

    [Header("Cover")]
    [SerializeField] private float lowHealthThreshold = 0.35f;
    [SerializeField] private float coverSearchRadius  = 5f;
    [SerializeField] private LayerMask coverLayer;

    [Header("Lead Target")]
    [SerializeField] private bool useLeadTarget = true;

    // ─────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────

    private bool  _isStrafeRight = true;
    private float _strafeChangeTimer;
    private bool  _isReloading;
    private bool  _hasCoverPos;
    private Vector3 _coverPos;

    // ─────────────────────────────────────────────
    //  Init
    // ─────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _strafeChangeTimer = strafeChangeTime;
    }

    // ─────────────────────────────────────────────
    //  State Machine
    // ─────────────────────────────────────────────

    protected override void UpdateStateMachine()
    {
        switch (CurrentState)
        {
            case EnemyState.Chase:     UpdateChase();     break;
            case EnemyState.Strafe:    UpdateStrafe();    break;
            case EnemyState.Attack:    UpdateRangedShot();break;
            case EnemyState.TakeCover: UpdateCover();     break;
            case EnemyState.Stunned:   break;
            case EnemyState.Dead:      break;
        }
    }

    // ─────────────────────────────────────────────
    //  Chase → approach to optimal range
    // ─────────────────────────────────────────────

    protected override void UpdateChase()
    {
        if (_playerHead == null) return;

        float dist = DistanceToPlayer();

        // Too close → back away
        if (dist < minRange)
        {
            Vector3 back = (transform.position - _playerHead.position).normalized;
            back.y = 0f;
            _agent.SetDestination(transform.position + back * 2f);
            _agent.speed = ChaseSpeed;
            return;
        }

        // Within good range → start strafing
        if (dist <= optimalRange * 1.2f)
        {
            TransitionToState(EnemyState.Strafe);
            return;
        }

        // Move toward player's XZ position
        Vector3 target = new Vector3(_playerHead.position.x, transform.position.y, _playerHead.position.z);
        _agent.SetDestination(target);
        _agent.speed = ChaseSpeed;
    }

    // ─────────────────────────────────────────────
    //  Strafe — orbit around player while shooting
    // ─────────────────────────────────────────────

    private void UpdateStrafe()
    {
        if (_playerHead == null) { TransitionToState(EnemyState.Chase); return; }

        float dist = DistanceToPlayer();

        FacePlayer();

        // Low health → take cover
        if (CurrentHealth / MaxHealth <= lowHealthThreshold)
        {
            TransitionToState(EnemyState.TakeCover);
            return;
        }

        // Too far → chase
        if (dist > optimalRange * 2f)
        {
            TransitionToState(EnemyState.Chase);
            return;
        }

        // Too close → back off
        if (dist < minRange)
        {
            Vector3 back = (transform.position - _playerHead.position).normalized;
            back.y = 0f;
            _agent.SetDestination(transform.position + back * 2f);
        }
        else
        {
            // Strafe perpendicular
            Vector3 toPlayer  = (_playerHead.position - transform.position);
            toPlayer.y = 0f;
            toPlayer   = toPlayer.normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, toPlayer) * (_isStrafeRight ? 1f : -1f);

            // Blend: maintain range + lateral movement
            float   drift    = dist - optimalRange;
            Vector3 navTarget = transform.position
                                + perp * strafeSpeed
                                + toPlayer * Mathf.Clamp(drift * 0.5f, -1.5f, 1.5f);

            _agent.SetDestination(navTarget);
            _agent.speed = strafeSpeed;
        }

        // Flip strafe direction periodically
        _strafeChangeTimer -= Time.deltaTime;
        if (_strafeChangeTimer <= 0f)
        {
            _isStrafeRight     = !_isStrafeRight;
            _strafeChangeTimer  = strafeChangeTime + Random.Range(-0.4f, 0.4f);
        }

        // Shoot while strafing
        if (_attackTimer <= 0f && !_isReloading && HasLineOfSight())
        {
            if (useBurstFire) StartCoroutine(BurstFire());
            else              FireProjectile();
            _attackTimer = attackCooldown;
        }
    }

    // ─────────────────────────────────────────────
    //  Aimed shot (still state)
    // ─────────────────────────────────────────────

    private void UpdateRangedShot()
    {
        if (_playerHead == null) { TransitionToState(EnemyState.Chase); return; }

        _agent.isStopped = true;
        FacePlayer();

        if (DistanceToPlayer() > optimalRange * 2f)
        {
            _agent.isStopped = false;
            TransitionToState(EnemyState.Chase);
            return;
        }

        if (_attackTimer <= 0f && !_isReloading && HasLineOfSight())
        {
            if (useBurstFire) StartCoroutine(BurstFire());
            else              FireProjectile();
            _attackTimer = attackCooldown;
        }

        // Return to strafe after cooldown
        if (_attackTimer < attackCooldown * 0.4f)
        {
            _agent.isStopped = false;
            TransitionToState(EnemyState.Strafe);
        }
    }

    // ─────────────────────────────────────────────
    //  Cover
    // ─────────────────────────────────────────────

    private void UpdateCover()
    {
        if (CurrentHealth / MaxHealth > lowHealthThreshold + 0.15f)
        {
            _hasCoverPos = false;
            TransitionToState(EnemyState.Strafe);
            return;
        }

        if (!_hasCoverPos) FindCover();

        if (_hasCoverPos)
        {
            _agent.isStopped = false;
            _agent.SetDestination(_coverPos);
            _agent.speed = ChaseSpeed * 1.2f;

            if (Vector3.Distance(transform.position, _coverPos) < 1.2f)
            {
                if (_attackTimer <= 0f && HasLineOfSight())
                {
                    FireProjectile();
                    _attackTimer = attackCooldown * 1.5f;
                }
            }
        }
        else
        {
            // No cover found — flee from player
            if (_playerHead != null)
            {
                Vector3 flee = (transform.position - _playerHead.position).normalized * 4f;
                flee.y = 0f;
                _agent.SetDestination(transform.position + flee);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Projectile
    // ─────────────────────────────────────────────

    protected override void PerformAttack() => FireProjectile();

    private void FireProjectile()
    {
        if (projectilePrefab == null || _playerHead == null) return;

        _animator.SetTrigger(ANIM_ATTACK);
        PlayAttackSound();

        Vector3 spawnPos = firePoint != null
            ? firePoint.position
            : transform.position + Vector3.up * 1.4f;

        // Aim at headset (the player's actual head in MR)
        Vector3 aimPos = useLeadTarget
            ? PredictHeadPosition(spawnPos)
            : _playerHead.position;

        // Accuracy spread — higher tier = less spread
        aimPos += new Vector3(
            Random.Range(-spread, spread) * 0.04f,
            Random.Range(-spread * 0.5f, spread * 0.5f) * 0.04f,
            Random.Range(-spread, spread) * 0.04f);

        Vector3 dir = (aimPos - spawnPos).normalized;

        GameObject go  = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(dir));
        Projectile proj = go.GetComponent<Projectile>();
        proj?.Initialize(dir, projectileSpeed, AttackDamage, gameObject.tag);
    }

    private IEnumerator BurstFire()
    {
        _isReloading = true;
        for (int i = 0; i < burstCount; i++)
        {
            if (!IsAlive) break;
            FireProjectile();
            yield return new WaitForSeconds(burstFireRate);
        }
        _isReloading = false;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Predict where the headset will be by the time the projectile arrives.
    /// In MR the head velocity can be grabbed from XRNode — here we approximate
    /// using frame-to-frame delta (works without XR SDK dependency).
    /// </summary>
    private Vector3 _prevHeadPos;
    private Vector3 _headVelocity;

    protected override void Update()
    {
        base.Update();
        if (_playerHead != null)
        {
            _headVelocity = (_playerHead.position - _prevHeadPos) / Time.deltaTime;
            _prevHeadPos  = _playerHead.position;
        }
    }

    private Vector3 PredictHeadPosition(Vector3 from)
    {
        float travelTime = Vector3.Distance(from, _playerHead.position) / projectileSpeed;
        return _playerHead.position + _headVelocity * travelTime;
    }

    private bool HasLineOfSight()
    {
        if (_playerHead == null) return false;
        Vector3 eye    = transform.position + Vector3.up * 1.4f;
        Vector3 target = _playerHead.position;
        return !Physics.Linecast(eye, target, coverLayer);
    }

    private void FindCover()
    {
        Collider[] cols = Physics.OverlapSphere(transform.position, coverSearchRadius, coverLayer);
        float best = float.MaxValue;
        bool  found = false;

        foreach (Collider col in cols)
        {
            Vector3 pt      = col.ClosestPoint(transform.position);
            float   distP   = Vector3.Distance(pt, _playerHead != null ? _playerHead.position : Vector3.zero);
            float   distMe  = Vector3.Distance(pt, transform.position);
            float   score   = distMe - distP * 0.5f;
            if (score < best)
            {
                best     = score;
                _coverPos = pt;
                found    = true;
            }
        }
        _hasCoverPos = found;
    }

    // ─────────────────────────────────────────────
    //  Gizmos
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, optimalRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minRange);
        if (firePoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(firePoint.position, 0.08f);
        }
    }
}
