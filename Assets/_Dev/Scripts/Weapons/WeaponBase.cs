using UnityEngine;

/// <summary>
/// Attach this to every weapon prefab.
/// Holds a reference to the weapon's ScriptableObject data.
/// Extend with durability tracking here later.
/// </summary>
public class WeaponBase : MonoBehaviour
{
    [Header("Weapon Definition")]
    [Tooltip("Drag the WeaponData ScriptableObject for this weapon here.")]
    public WeaponData data;

    // ── Future: runtime durability state ──────────────────────
    // private int _currentDurability;
    // public int CurrentDurability => _currentDurability;
    // public float DurabilityPercent => (float)_currentDurability / data.maxDurability;
    //
    // private void Awake() => _currentDurability = data.maxDurability;
    // public void UseDurability(int amount) { ... notify WeaponManager ... }
    // ─────────────────────────────────────────────────────────
}
