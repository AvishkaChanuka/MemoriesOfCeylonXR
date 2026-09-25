using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Adaptive Difficulty Manager — monitors player performance in real-time
/// and smoothly scales enemy stats up or down.
/// 
/// Performance Score is computed from:
///   (+) Kills per minute (fast kills = good)
///   (–) Deaths (each death is a big negative)
///   (–) Damage taken ratio (normalized 0–1)
/// 
/// Score maps to a Difficulty Tier (1–5):
///   1 = Very Easy, 3 = Normal, 5 = Very Hard
/// 
/// Stats adjusted:
///   Enemy HP, Damage, Speed, Reaction time, Accuracy
/// 
/// Singleton — place ONE in the scene, assign Player reference.
/// </summary>
public class DifficultyManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────

    public static DifficultyManager Instance { get; private set; }

    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private PlayerStats playerStats;           // auto-found if null

    [Header("Evaluation")]
    [SerializeField] private float evaluationInterval  = 15f;  // seconds between difficulty recalcs
    [SerializeField] private float scoreSmoothing      = 0.3f; // lerp speed for difficulty changes
    [SerializeField] private bool  logDifficulty       = true;

    [Header("Difficulty Tiers (1=Easy → 5=Hard)")]
    [SerializeField] private DifficultyTier[] tiers = new DifficultyTier[]
    {
        new DifficultyTier { tier=1, healthMult=0.60f, damageMult=0.50f, speedMult=0.75f, reactionMult=0.50f, accuracyMult=0.40f },
        new DifficultyTier { tier=2, healthMult=0.80f, damageMult=0.75f, speedMult=0.90f, reactionMult=0.70f, accuracyMult=0.65f },
        new DifficultyTier { tier=3, healthMult=1.00f, damageMult=1.00f, speedMult=1.00f, reactionMult=1.00f, accuracyMult=1.00f },
        new DifficultyTier { tier=4, healthMult=1.30f, damageMult=1.30f, speedMult=1.15f, reactionMult=1.20f, accuracyMult=1.25f },
        new DifficultyTier { tier=5, healthMult=1.70f, damageMult=1.70f, speedMult=1.35f, reactionMult=1.50f, accuracyMult=1.50f },
    };

    [Header("Score Thresholds (maps score → tier)")]
    [SerializeField] private float tier1MaxScore = -5f;    // score < -5  → tier 1
    [SerializeField] private float tier2MaxScore =  0f;    // -5 to 0    → tier 2
    [SerializeField] private float tier3MaxScore =  5f;    // 0 to 5     → tier 3
    [SerializeField] private float tier4MaxScore = 10f;    // 5 to 10    → tier 4
                                                            // > 10       → tier 5

    // ─────────────────────────────────────────────
    //  Data Class
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class DifficultyTier
    {
        public int   tier;
        public float healthMult;
        public float damageMult;
        public float speedMult;
        public float reactionMult;
        public float accuracyMult;
    }

    // ─────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────

    public int   CurrentTier       { get; private set; } = 3;   // start at Normal
    public float CurrentScore      { get; private set; } = 0f;
    public float SmoothedHealthMult   { get; private set; } = 1f;
    public float SmoothedDamageMult   { get; private set; } = 1f;
    public float SmoothedSpeedMult    { get; private set; } = 1f;
    public float SmoothedReactionMult { get; private set; } = 1f;
    public float SmoothedAccuracyMult { get; private set; } = 1f;

    // Snapshot for evaluation window
    private int   _killsAtLastEval;
    private int   _deathsAtLastEval;
    private float _damageAtLastEval;
    private float _evalTimer;

    // All active enemies — register/unregister themselves
    private readonly List<EnemyBase> _activeEnemies = new List<EnemyBase>();

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (playerStats == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null)
                playerStats = playerGO.GetComponent<PlayerStats>();
        }

        if (playerStats == null)
            Debug.LogError("[DifficultyManager] PlayerStats not found! Difficulty will not adapt.");

        // Start at tier 3 (normal)
        ApplyTier(3, immediate: true);
    }

    private void Update()
    {
        _evalTimer -= Time.deltaTime;
        if (_evalTimer <= 0f)
        {
            _evalTimer = evaluationInterval;
            EvaluateDifficulty();
        }

        // Smooth the multipliers every frame
        SmoothMultipliers();
    }

    // ─────────────────────────────────────────────
    //  Enemy Registration
    // ─────────────────────────────────────────────

    /// <summary>Called by EnemySpawner (or EnemyBase.Start) when an enemy spawns.</summary>
    public void RegisterEnemy(EnemyBase enemy)
    {
        if (!_activeEnemies.Contains(enemy))
        {
            _activeEnemies.Add(enemy);
            enemy.OnEnemyDied += OnEnemyDied;
            ApplySettingsToEnemy(enemy);
        }
    }

    private void OnEnemyDied(EnemyBase enemy)
    {
        _activeEnemies.Remove(enemy);
        enemy.OnEnemyDied -= OnEnemyDied;
    }

    // ─────────────────────────────────────────────
    //  Difficulty Evaluation
    // ─────────────────────────────────────────────

    private void EvaluateDifficulty()
    {
        if (playerStats == null) return;

        // Delta since last evaluation
        int   killsDelta  = playerStats.KillCount  - _killsAtLastEval;
        int   deathsDelta = playerStats.DeathCount - _deathsAtLastEval;
        float damageDelta = playerStats.TotalDamageReceived - _damageAtLastEval;

        // Normalise damage by max health
        float damageRatio = playerStats.MaxHealth > 0f
            ? damageDelta / playerStats.MaxHealth
            : 0f;

        // Performance score formula:
        // More kills/min = higher score
        // Deaths and damage taken reduce score
        float killScore   =  killsDelta  * 3f;           // +3 per kill
        float deathScore  = -deathsDelta * 8f;           // -8 per death
        float damageScore = -damageRatio  * 5f;          // -5 at 1 full HP bar of damage

        float rawScore = killScore + deathScore + damageScore;
        CurrentScore   = Mathf.Lerp(CurrentScore, rawScore, 0.6f);  // smooth over time

        // Map score to tier
        int newTier = ScoreToTier(CurrentScore);

        if (logDifficulty)
        {
            Debug.Log($"[Difficulty] Kills:{killsDelta} Deaths:{deathsDelta} " +
                      $"DmgRatio:{damageRatio:F2} Score:{CurrentScore:F1} → Tier {newTier}");
        }

        ApplyTier(newTier);

        // Save snapshot
        _killsAtLastEval  = playerStats.KillCount;
        _deathsAtLastEval = playerStats.DeathCount;
        _damageAtLastEval = playerStats.TotalDamageReceived;
    }

    private int ScoreToTier(float score)
    {
        if (score < tier1MaxScore) return 1;
        if (score < tier2MaxScore) return 2;
        if (score < tier3MaxScore) return 3;
        if (score < tier4MaxScore) return 4;
        return 5;
    }

    private void ApplyTier(int tier, bool immediate = false)
    {
        CurrentTier = Mathf.Clamp(tier, 1, tiers.Length);
        DifficultyTier data = tiers[CurrentTier - 1];

        if (immediate)
        {
            SmoothedHealthMult   = data.healthMult;
            SmoothedDamageMult   = data.damageMult;
            SmoothedSpeedMult    = data.speedMult;
            SmoothedReactionMult = data.reactionMult;
            SmoothedAccuracyMult = data.accuracyMult;
        }
        // Otherwise the smoothing in Update() will lerp toward target values
        _targetHealthMult   = data.healthMult;
        _targetDamageMult   = data.damageMult;
        _targetSpeedMult    = data.speedMult;
        _targetReactionMult = data.reactionMult;
        _targetAccuracyMult = data.accuracyMult;

        // Apply to all currently active enemies
        foreach (EnemyBase enemy in _activeEnemies)
            if (enemy != null && enemy.IsAlive)
                ApplySettingsToEnemy(enemy);
    }

    private float _targetHealthMult   = 1f;
    private float _targetDamageMult   = 1f;
    private float _targetSpeedMult    = 1f;
    private float _targetReactionMult = 1f;
    private float _targetAccuracyMult = 1f;

    private void SmoothMultipliers()
    {
        float t = scoreSmoothing * Time.deltaTime;
        SmoothedHealthMult   = Mathf.Lerp(SmoothedHealthMult,   _targetHealthMult,   t);
        SmoothedDamageMult   = Mathf.Lerp(SmoothedDamageMult,   _targetDamageMult,   t);
        SmoothedSpeedMult    = Mathf.Lerp(SmoothedSpeedMult,    _targetSpeedMult,    t);
        SmoothedReactionMult = Mathf.Lerp(SmoothedReactionMult, _targetReactionMult, t);
        SmoothedAccuracyMult = Mathf.Lerp(SmoothedAccuracyMult, _targetAccuracyMult, t);
    }

    private void ApplySettingsToEnemy(EnemyBase enemy)
    {
        enemy.ApplyDifficultySettings(
            SmoothedHealthMult,
            SmoothedDamageMult,
            SmoothedSpeedMult,
            1f - SmoothedReactionMult * 0.3f  // reaction maps inversely (lower = faster)
        );
    }

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────

    /// <summary>Get the current spread multiplier for ranged enemies (1/accuracy).</summary>
    public float GetSpreadMultiplier() => 1f / Mathf.Max(0.1f, SmoothedAccuracyMult);

    /// <summary>Force an immediate tier jump (e.g., for boss encounters or cutscenes).</summary>
    public void ForceTier(int tier) => ApplyTier(tier, immediate: true);

    // ─────────────────────────────────────────────
    //  On-Screen Debug (optional — remove in prod)
    // ─────────────────────────────────────────────

    private void OnGUI()
    {
        if (!logDifficulty) return;

        GUI.Label(new Rect(10, 10, 300, 20),
            $"Difficulty Tier: {CurrentTier}/5  Score: {CurrentScore:F1}");
        GUI.Label(new Rect(10, 30, 300, 20),
            $"HP:{SmoothedHealthMult:F2}  DMG:{SmoothedDamageMult:F2}  " +
            $"SPD:{SmoothedSpeedMult:F2}  REACT:{SmoothedReactionMult:F2}");
    }
}
