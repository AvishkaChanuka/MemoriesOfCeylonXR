using UnityEngine;
using System;

/// <summary>
/// Manages player health, stamina, and performance metrics.
/// Performance data is consumed by DifficultyManager to scale enemy difficulty.
/// Attach to: Player GameObject
/// </summary>
public class PlayerStats : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float healthRegenRate = 0f;          // HP/sec (0 = disabled)
    [SerializeField] private float healthRegenDelay = 5f;         // seconds after last hit

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenRate = 15f;        // stamina/sec
    [SerializeField] private float staminaRegenDelay = 1.5f;      // seconds after use
    [SerializeField] private float sprintStaminaCost = 20f;       // per second
    [SerializeField] private float attackStaminaCost = 15f;
    [SerializeField] private float blockStaminaCost = 10f;        // per second while blocking

    [Header("UI (optional)")]
    [SerializeField] private UnityEngine.UI.Slider healthBar;
    [SerializeField] private UnityEngine.UI.Slider staminaBar;

    // ─────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────

    public event Action<float, float> OnHealthChanged;   // (current, max)
    public event Action<float, float> OnStaminaChanged;  // (current, max)
    public event Action<float>        OnDamageTaken;     // (amount)
    public event Action               OnDeath;
    public event Action               OnKilledEnemy;

    // ─────────────────────────────────────────────
    //  Properties
    // ─────────────────────────────────────────────

    public float CurrentHealth  { get; private set; }
    public float MaxHealth      => maxHealth;
    public float CurrentStamina { get; private set; }
    public float MaxStamina     => maxStamina;
    public bool  IsAlive        { get; private set; } = true;
    public bool  IsBlocking     { get; set; }

    // ─────────────────────────────────────────────
    //  Performance Tracking (read by DifficultyManager)
    // ─────────────────────────────────────────────

    public int   KillCount           { get; private set; }
    public int   DeathCount          { get; private set; }
    public float TotalDamageReceived { get; private set; }
    public float TotalDamageDealt    { get; private set; }
    public float SessionTime         { get; private set; }

    /// <summary>Kills per minute — high value = player is doing well.</summary>
    public float KillsPerMinute => SessionTime > 0f
        ? (KillCount / SessionTime) * 60f
        : 0f;

    /// <summary>0–1 ratio; higher = taking more damage relative to max.</summary>
    public float DamageRatio => maxHealth > 0f
        ? TotalDamageReceived / Mathf.Max(1f, TotalDamageReceived + (maxHealth * (DeathCount + 1)))
        : 0f;

    // ─────────────────────────────────────────────
    //  Private State
    // ─────────────────────────────────────────────

    private float _healthRegenTimer;
    private float _staminaRegenTimer;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        CurrentHealth  = maxHealth;
        CurrentStamina = maxStamina;
    }

    private void Update()
    {
        if (!IsAlive) return;

        SessionTime += Time.deltaTime;

        HandleHealthRegen();
        HandleStaminaRegen();

        // Drain stamina while blocking
        if (IsBlocking)
            UseStamina(blockStaminaCost * Time.deltaTime);
    }

    // ─────────────────────────────────────────────
    //  Public API — Health
    // ─────────────────────────────────────────────

    /// <summary>Apply damage to the player. Blocking reduces damage by 60%.</summary>
    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;

        // Blocking absorbs 60% damage (only if stamina allows)
        if (IsBlocking && CurrentStamina > 0f)
            amount *= 0.4f;

        amount = Mathf.Max(0f, amount);
        CurrentHealth = Mathf.Clamp(CurrentHealth - amount, 0f, maxHealth);
        TotalDamageReceived += amount;

        _healthRegenTimer = 0f;  // reset regen delay

        OnDamageTaken?.Invoke(amount);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        UpdateHealthUI();

        if (CurrentHealth <= 0f)
            Die();
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0f, maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        UpdateHealthUI();
    }

    // ─────────────────────────────────────────────
    //  Public API — Stamina
    // ─────────────────────────────────────────────

    /// <summary>Returns false if not enough stamina.</summary>
    public bool UseStamina(float amount)
    {
        if (CurrentStamina < amount) return false;
        CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
        _staminaRegenTimer = 0f;
        OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
        UpdateStaminaUI();
        return true;
    }

    public bool HasStamina(float amount) => CurrentStamina >= amount;
    public float SprintStaminaCostPerSec => sprintStaminaCost;
    public float AttackStaminaCost       => attackStaminaCost;

    // ─────────────────────────────────────────────
    //  Public API — Performance Recording
    // ─────────────────────────────────────────────

    public void RecordKill()
    {
        KillCount++;
        OnKilledEnemy?.Invoke();
    }

    public void RecordDamageDealt(float amount)
    {
        TotalDamageDealt += amount;
    }

    // ─────────────────────────────────────────────
    //  Private Helpers
    // ─────────────────────────────────────────────

    private void HandleHealthRegen()
    {
        if (healthRegenRate <= 0f || CurrentHealth >= maxHealth) return;

        _healthRegenTimer += Time.deltaTime;
        if (_healthRegenTimer >= healthRegenDelay)
            Heal(healthRegenRate * Time.deltaTime);
    }

    private void HandleStaminaRegen()
    {
        if (CurrentStamina >= maxStamina) return;

        _staminaRegenTimer += Time.deltaTime;
        if (_staminaRegenTimer >= staminaRegenDelay)
        {
            CurrentStamina = Mathf.Clamp(CurrentStamina + staminaRegenRate * Time.deltaTime, 0f, maxStamina);
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            UpdateStaminaUI();
        }
    }

    private void Die()
    {
        if (!IsAlive) return;
        IsAlive = false;
        DeathCount++;
        OnDeath?.Invoke();
        Debug.Log("[PlayerStats] Player died.");
    }

    private void UpdateHealthUI()
    {
        if (healthBar != null)
            healthBar.value = CurrentHealth / maxHealth;
    }

    private void UpdateStaminaUI()
    {
        if (staminaBar != null)
            staminaBar.value = CurrentStamina / maxStamina;
    }
}
