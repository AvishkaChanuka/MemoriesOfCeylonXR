using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Listens to WeaponManager and updates the HUD weapon slot.
/// Attach to the Canvas GameObject that contains your weapon UI panel.
///
/// Extend this later with:
///   • Durability bar (fill a Slider/Image fillAmount)
///   • Ammo counter (show/hide ammo TextMeshPro)
///   • Rarity border colour change
/// </summary>
public class WeaponHUDPanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The Image component that displays the weapon icon.")]
    [SerializeField] private Image weaponIconImage;

    [Tooltip("Optional label showing the weapon name.")]
    [SerializeField] private TextMeshProUGUI weaponNameText;

    [Tooltip("Sprite to show when the player is unarmed / no weapon equipped.")]
    [SerializeField] private Sprite unarmedSprite;

    // ── Future: durability bar ───────────────────────────────────────────
    // [Header("Durability (enable when ready)")]
    // [SerializeField] private Slider durabilityBar;
    // [SerializeField] private Image  durabilityFill;
    // [SerializeField] private Color  fullDurabilityColor  = Color.green;
    // [SerializeField] private Color  lowDurabilityColor   = Color.red;
    // ─────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (WeaponManager.Instance != null)
            WeaponManager.Instance.OnActiveWeaponChanged += HandleWeaponChanged;
    }

    private void OnDisable()
    {
        if (WeaponManager.Instance != null)
            WeaponManager.Instance.OnActiveWeaponChanged -= HandleWeaponChanged;
    }

    private void Start()
    {
        // Sync to whatever weapon is already active when HUD is first shown
        if (WeaponManager.Instance != null)
            HandleWeaponChanged(WeaponManager.Instance.ActiveWeapon);
    }

    private void HandleWeaponChanged(WeaponBase weapon)
    {
        if (weapon != null && weapon.data != null)
        {
            // Icon
            weaponIconImage.sprite = weapon.data.hudIcon;
            weaponIconImage.enabled = true;

            // Name label (optional)
            if (weaponNameText != null)
                weaponNameText.text = weapon.data.weaponName;

            // ── Future durability update ──────────────────────────────
            // UpdateDurabilityBar(weapon.DurabilityPercent);
            // ─────────────────────────────────────────────────────────
        }
        else
        {
            // Unarmed state
            weaponIconImage.sprite = unarmedSprite;
            weaponIconImage.enabled = unarmedSprite != null;

            if (weaponNameText != null)
                weaponNameText.text = "Unarmed";
        }
    }

    // ── Future: durability bar method ────────────────────────────────────
    // private void UpdateDurabilityBar(float percent)
    // {
    //     if (durabilityBar == null) return;
    //     durabilityBar.value = percent;
    //     durabilityFill.color = Color.Lerp(lowDurabilityColor, fullDurabilityColor, percent);
    // }
    // ─────────────────────────────────────────────────────────────────────
}
