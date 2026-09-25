using UnityEngine;

/// <summary>
/// Poolable projectile for ranged enemies (arrows, bullets, fireballs, etc.).
/// - Uses Rigidbody physics for arc if gravity enabled, straight otherwise
/// - Deals damage to player on hit via PlayerCombat.OnHit()
/// - Destroys self on impact or after lifetime expires
/// 
/// Attach to: Projectile prefab (with Rigidbody + Collider (isTrigger) + Renderer)
/// Tag the prefab: "EnemyProjectile"
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Projectile : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Behaviour")]
    [SerializeField] private float  lifetime        = 5f;      // auto-destroy after N seconds
    [SerializeField] private bool   useGravity      = false;   // arc shot (arrow) vs straight (bullet)
    [SerializeField] private bool   penetrates      = false;   // pass through multiple targets

    [Header("Visual Feedback")]
    [SerializeField] private GameObject hitParticlePrefab;     // optional hit VFX
    [SerializeField] private TrailRenderer trailRenderer;

    [Header("Audio")]
    [SerializeField] private AudioClip hitSound;

    // ─────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────

    private Rigidbody _rb;
    private float     _damage;
    private string    _ownerTag;     // who fired this (to avoid hitting allies)
    private bool      _initialized;
    private bool      _hasHit;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity  = useGravity;
        _rb.isKinematic = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void Start()
    {
        // Auto destroy if not initialized or after lifetime
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // Orient projectile along velocity direction (arrow tracking)
        if (_rb.linearVelocity.sqrMagnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
    }

    // ─────────────────────────────────────────────
    //  Public Init
    // ─────────────────────────────────────────────

    /// <summary>
    /// Must be called immediately after Instantiate.
    /// </summary>
    public void Initialize(Vector3 direction, float speed, float damage, string ownerTag)
    {
        _damage      = damage;
        _ownerTag    = ownerTag;
        _initialized = true;

        _rb.linearVelocity = direction.normalized * speed;
    }

    // ─────────────────────────────────────────────
    //  Collision
    // ─────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other.gameObject, other.transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.gameObject, collision.contacts[0].point);
    }

    private void HandleCollision(GameObject target, Vector3 point)
    {
        if (_hasHit && !penetrates) return;

        // Don't hit the owner or allies
        if (target.CompareTag(_ownerTag)) return;

        // Hit player
        PlayerCombat playerCombat = target.GetComponentInParent<PlayerCombat>();
        if (playerCombat != null)
        {
            playerCombat.OnHit(_damage, transform.position);
            SpawnHitFX(point);
            PlayHitSound(point);

            if (!penetrates)
            {
                _hasHit = true;
                Destroy(gameObject, 0.05f);
            }
            return;
        }

        // Hit environment / terrain — stop and despawn
        if (!target.CompareTag("Player") && !target.CompareTag("Enemy"))
        {
            SpawnHitFX(point);
            PlayHitSound(point);
            _hasHit = true;

            if (trailRenderer != null)
                trailRenderer.transform.parent = null; // detach trail so it fades naturally

            Destroy(gameObject, 0.05f);
        }
    }

    // ─────────────────────────────────────────────
    //  VFX / SFX
    // ─────────────────────────────────────────────

    private void SpawnHitFX(Vector3 position)
    {
        if (hitParticlePrefab != null)
            Destroy(
                Instantiate(hitParticlePrefab, position, Quaternion.identity),
                2f);
    }

    private void PlayHitSound(Vector3 position)
    {
        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, position);
    }
}
