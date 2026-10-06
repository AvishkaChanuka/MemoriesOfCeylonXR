using System;
using UnityEngine;

/// <summary>
/// Tracks which weapon is currently active and broadcasts events
/// so any HUD panel (or future system) can react without tight coupling.
///
/// Attach to: Player root / OVRCameraRig
/// </summary>
public class WeaponManager : MonoBehaviour
{
    // ── Singleton (optional — remove if you prefer direct references) ──
    public static WeaponManager Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────────
    /// <summary>Fires whenever the active weapon changes. Passes new WeaponBase (null = unarmed).</summary>
    public event Action<WeaponBase> OnActiveWeaponChanged;

    // ── State ────────────────────────────────────────────────────────────
    public WeaponBase ActiveWeapon { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Call this whenever the player equips or switches to a new weapon.
    /// Pass null to represent the unarmed/fist state.
    /// </summary>
    public void SetActiveWeapon(WeaponBase newWeapon)
    {
        if (ActiveWeapon == newWeapon) return;

        ActiveWeapon = newWeapon;
        OnActiveWeaponChanged?.Invoke(ActiveWeapon);

        Debug.Log($"[WeaponManager] Active weapon → {(newWeapon != null ? newWeapon.data.weaponName : "Unarmed")}");
    }
}
