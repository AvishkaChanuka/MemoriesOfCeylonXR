using UnityEngine;

/// <summary>
/// Handles player locomotion: walk, run, sprint, jump.
/// Drives the Animator with standard blend-tree parameters.
/// Requires: CharacterController, PlayerStats, Animator
/// Attach to: Player GameObject
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Movement")]
    [SerializeField] private float walkSpeed   = 3.5f;
    [SerializeField] private float runSpeed    = 6f;
    [SerializeField] private float sprintSpeed = 9f;
    [SerializeField] private float jumpHeight  = 1.5f;
    [SerializeField] private float gravity     = -19.62f;  // 2x earth gravity for snappy feel

    [Header("Rotation")]
    [SerializeField] private float turnSmoothTime = 0.1f;
    [SerializeField] private Transform cameraTransform;    // assign Main Camera transform

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;        // small empty child at feet
    [SerializeField] private float     groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Animator Hash (match your Animator params)")]
    // These must match the Parameter names in your Animator Controller exactly
    private static readonly int ANIM_SPEED      = Animator.StringToHash("Speed");
    private static readonly int ANIM_GROUNDED   = Animator.StringToHash("IsGrounded");
    private static readonly int ANIM_RUNNING    = Animator.StringToHash("IsRunning");
    private static readonly int ANIM_JUMPING    = Animator.StringToHash("IsJumping");

    // ─────────────────────────────────────────────
    //  Components
    // ─────────────────────────────────────────────

    private CharacterController _cc;
    private PlayerStats         _stats;
    private Animator            _animator;
    private PlayerCombat        _combat;

    // ─────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────

    private Vector3 _velocity;          // vertical velocity (gravity + jump)
    private float   _turnVelocity;      // SmoothDampAngle ref
    private bool    _isGrounded;
    private bool    _isSprinting;

    public bool  IsMoving    { get; private set; }
    public float MoveSpeed   { get; private set; }

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        _cc       = GetComponent<CharacterController>();
        _stats    = GetComponent<PlayerStats>();
        _animator = GetComponent<Animator>();
        _combat   = GetComponent<PlayerCombat>();

        // Auto-assign camera if not set
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        if (!_stats.IsAlive) return;

        CheckGround();
        HandleGravity();
        HandleMovement();
        HandleJump();
        UpdateAnimator();
    }

    // ─────────────────────────────────────────────
    //  Ground Check
    // ─────────────────────────────────────────────

    private void CheckGround()
    {
        Vector3 checkPos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;

        _isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundLayer);

        // Reset downward velocity on land
        if (_isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;
    }

    // ─────────────────────────────────────────────
    //  Gravity
    // ─────────────────────────────────────────────

    private void HandleGravity()
    {
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(new Vector3(0f, _velocity.y * Time.deltaTime, 0f));
    }

    // ─────────────────────────────────────────────
    //  Movement
    // ─────────────────────────────────────────────

    private void HandleMovement()
    {
        // Block movement during certain combat actions
        if (_combat != null && _combat.IsAttacking) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v).normalized;

        IsMoving = inputDir.magnitude >= 0.1f;

        // Sprinting: Left Shift + has stamina
        _isSprinting = Input.GetKey(KeyCode.LeftShift)
                       && IsMoving
                       && _stats.HasStamina(_stats.SprintStaminaCostPerSec * Time.deltaTime);

        if (_isSprinting)
            _stats.UseStamina(_stats.SprintStaminaCostPerSec * Time.deltaTime);

        MoveSpeed = _isSprinting ? sprintSpeed
                  : Input.GetKey(KeyCode.LeftControl) ? walkSpeed
                  : runSpeed;

        if (!IsMoving) return;

        // Camera-relative direction
        float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg
                            + (cameraTransform != null ? cameraTransform.eulerAngles.y : 0f);

        float smoothAngle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle, ref _turnVelocity, turnSmoothTime);

        transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);

        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        _cc.Move(moveDir * MoveSpeed * Time.deltaTime);
    }

    // ─────────────────────────────────────────────
    //  Jump
    // ─────────────────────────────────────────────

    private void HandleJump()
    {
        if (Input.GetButtonDown("Jump") && _isGrounded)
        {
            // v = sqrt(2 * |gravity| * jumpHeight)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    // ─────────────────────────────────────────────
    //  Animator
    // ─────────────────────────────────────────────

    private void UpdateAnimator()
    {
        // Speed: 0=idle, 0.5=walk, 1=run, 1.5=sprint
        float speedParam = 0f;
        if (IsMoving)
        {
            speedParam = _isSprinting ? 1.5f
                       : (MoveSpeed >= runSpeed ? 1f : 0.5f);
        }

        _animator.SetFloat(ANIM_SPEED,    speedParam, 0.1f, Time.deltaTime);
        _animator.SetBool(ANIM_GROUNDED,  _isGrounded);
        _animator.SetBool(ANIM_RUNNING,   _isSprinting);
        _animator.SetBool(ANIM_JUMPING,   !_isGrounded);
    }

    // ─────────────────────────────────────────────
    //  Gizmos — visualise ground check
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 pos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pos, groundCheckRadius);
    }
}
