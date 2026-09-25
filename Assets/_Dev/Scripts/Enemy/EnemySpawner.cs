using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;

/// <summary>
/// Pool-based enemy spawner. Pre-instantiates enemy pools to avoid runtime GC.
/// Features:
/// - Configurable wave system (wave count, enemy composition, delay between waves)
/// - MRUK vertical-surface spawning (enemies appear on room walls)
/// - Automatically registers spawned enemies with DifficultyManager
/// 
/// Place ONE in the scene. Requires MRUK to be initialised before the first wave.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Data Classes
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class EnemyPoolEntry
    {
        public string label;
        public GameObject prefab;
        public int poolSize = 10;
        [HideInInspector]
        public Queue<GameObject> pool;
    }

    [System.Serializable]
    public class WaveConfig
    {
        public string waveName = "Wave 1";
        public float spawnDelay = 0.5f;    // seconds between each enemy spawn in wave
        public float waveDelay = 5f;      // seconds before next wave starts
        public SpawnEntry[] enemies;
    }

    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("Must match the 'label' of an EnemyPoolEntry above")]
        public string enemyLabel;
        public int count = 3;
    }

    // ─────────────────────────────────────────────
    //  Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Enemy Pools")]
    [SerializeField] private EnemyPoolEntry[] enemyPools;

    [Header("MRUK Surface Spawning")]
    [Tooltip("Which surface labels enemies may spawn on (vertical surfaces only).")]
    public MRUKAnchor.SceneLabels spawnLabels;

    [Tooltip("Minimum distance from anchor edge before a spawn position is accepted.")]
    [SerializeField] private float minEdgeDistance = 0.3f;

    [Tooltip("How far in front of the wall surface the enemy is placed.")]
    [SerializeField] private float normalOffset = 0.05f;

    [Tooltip("How many random positions to try before giving up on a single enemy.")]
    [SerializeField] private int spawnTries = 100;

    [Header("Waves")]
    [SerializeField] private WaveConfig[] waves;
    [SerializeField] private bool loopWaves = true;
    [SerializeField] private float initialDelay = 2f;   // delay before first wave

    [Header("Limits")]
    [SerializeField] private int maxActiveEnemies = 20;

    // ─────────────────────────────────────────────
    //  Runtime State
    // ─────────────────────────────────────────────

    private int _currentWaveIndex = 0;
    private int _activeEnemyCount = 0;
    private bool _spawningActive = false;

    public int CurrentWave => _currentWaveIndex + 1;
    public int ActiveEnemyCount => _activeEnemyCount;
    public bool IsSpawning => _spawningActive;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Start()
    {
        InitializePools();
        StartCoroutine(SpawnWavesRoutine());
    }

    // ─────────────────────────────────────────────
    //  Pool Initialization
    // ─────────────────────────────────────────────

    private void InitializePools()
    {
        foreach (EnemyPoolEntry entry in enemyPools)
        {
            entry.pool = new Queue<GameObject>();

            for (int i = 0; i < entry.poolSize; i++)
            {
                GameObject go = Instantiate(entry.prefab, Vector3.zero, Quaternion.identity);
                go.SetActive(false);
                go.transform.SetParent(transform);  // keep hierarchy clean
                entry.pool.Enqueue(go);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Wave Spawning Coroutine
    // ─────────────────────────────────────────────

    private IEnumerator SpawnWavesRoutine()
    {
        yield return new WaitForSeconds(initialDelay);

        while (true)
        {
            if (waves == null || waves.Length == 0) yield break;

            WaveConfig wave = waves[_currentWaveIndex];
            Debug.Log($"[EnemySpawner] Starting {wave.waveName}");
            _spawningActive = true;

            // Spawn all enemies in this wave
            foreach (SpawnEntry entry in wave.enemies)
            {
                for (int i = 0; i < entry.count; i++)
                {
                    // Wait if at capacity
                    while (_activeEnemyCount >= maxActiveEnemies)
                        yield return new WaitForSeconds(1f);

                    SpawnEnemy(entry.enemyLabel);
                    yield return new WaitForSeconds(wave.spawnDelay);
                }
            }

            _spawningActive = false;

            // Advance wave
            _currentWaveIndex++;
            if (_currentWaveIndex >= waves.Length)
            {
                if (loopWaves)
                    _currentWaveIndex = 0;
                else
                {
                    Debug.Log("[EnemySpawner] All waves complete.");
                    yield break;
                }
            }

            Debug.Log($"[EnemySpawner] Next wave in {wave.waveDelay}s");
            yield return new WaitForSeconds(wave.waveDelay);
        }
    }

    // ─────────────────────────────────────────────
    //  Spawn Single Enemy
    // ─────────────────────────────────────────────

    private void SpawnEnemy(string label)
    {
        EnemyPoolEntry entry = FindPool(label);
        if (entry == null)
        {
            Debug.LogWarning($"[EnemySpawner] No pool found for label '{label}'");
            return;
        }

        GameObject go = GetFromPool(entry);
        if (go == null)
        {
            Debug.LogWarning($"[EnemySpawner] Pool '{label}' exhausted.");
            return;
        }

        // Find a valid spawn position on a wall surface via MRUK
        Vector3 spawnPos = Vector3.zero;
        bool placed = false;

        if (MRUK.Instance != null && MRUK.Instance.IsInitialized)
        {
            MRUKRoom room = MRUK.Instance.GetCurrentRoom();
            if (room != null)
            {
                for (int attempt = 0; attempt < spawnTries; attempt++)
                {
                    bool found = room.GenerateRandomPositionOnSurface(
                        MRUK.SurfaceType.VERTICAL,
                        minEdgeDistance,
                        LabelFilter.Included(spawnLabels),
                        out Vector3 pos,
                        out Vector3 norm);

                    if (found)
                    {
                        // Push slightly off the wall, then drop to floor level
                        spawnPos = pos + norm * normalOffset;
                        spawnPos.y = 0f;
                        placed = true;
                        break;
                    }
                }
            }
        }

        if (!placed)
        {
            Debug.LogWarning($"[EnemySpawner] Could not find MRUK surface position for '{label}' after {spawnTries} tries.");
            ReturnToPool(entry, go);
            return;
        }

        go.transform.position = spawnPos;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.SetActive(true);

        // Reset enemy health and state
        EnemyBase enemy = go.GetComponent<EnemyBase>();
        if (enemy != null)
        {
            // Register with difficulty manager
            if (DifficultyManager.Instance != null)
                DifficultyManager.Instance.RegisterEnemy(enemy);

            // Track active count
            enemy.OnEnemyDied += OnEnemyDeactivated;
            _activeEnemyCount++;
        }
    }

    // ─────────────────────────────────────────────
    //  Pool Helpers
    // ─────────────────────────────────────────────

    private GameObject GetFromPool(EnemyPoolEntry entry)
    {
        // Return inactive object from queue
        while (entry.pool.Count > 0)
        {
            GameObject go = entry.pool.Dequeue();
            if (go != null && !go.activeSelf)
                return go;
        }

        // Pool empty — expand (soft limit)
        if (entry.prefab != null)
        {
            GameObject go = Instantiate(entry.prefab, Vector3.zero, Quaternion.identity);
            go.transform.SetParent(transform);
            return go;
        }

        return null;
    }

    private void ReturnToPool(EnemyPoolEntry entry, GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(transform);
        entry.pool.Enqueue(go);
    }

    private EnemyPoolEntry FindPool(string label)
    {
        foreach (EnemyPoolEntry e in enemyPools)
            if (e.label == label) return e;
        return null;
    }



    // ─────────────────────────────────────────────
    //  Enemy Death Callback
    // ─────────────────────────────────────────────

    private void OnEnemyDeactivated(EnemyBase enemy)
    {
        enemy.OnEnemyDied -= OnEnemyDeactivated;
        _activeEnemyCount = Mathf.Max(0, _activeEnemyCount - 1);
    }

    // ─────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────

    /// <summary>Manually spawn a specific enemy type at a given position.</summary>
    public void SpawnAt(string label, Vector3 position)
    {
        EnemyPoolEntry entry = FindPool(label);
        if (entry == null) return;

        GameObject go = GetFromPool(entry);
        if (go == null) return;

        go.transform.position = position;
        go.SetActive(true);

        EnemyBase enemy = go.GetComponent<EnemyBase>();
        if (enemy != null)
        {
            if (DifficultyManager.Instance != null)
                DifficultyManager.Instance.RegisterEnemy(enemy);
            enemy.OnEnemyDied += OnEnemyDeactivated;
            _activeEnemyCount++;
        }
    }

    /// <summary>Stop all spawning (e.g., for cutscene or game over).</summary>
    public void StopSpawning() => StopAllCoroutines();


}
