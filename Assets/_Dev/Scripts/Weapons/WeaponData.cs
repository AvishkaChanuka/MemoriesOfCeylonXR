using UnityEngine;

/// <summary>
/// ScriptableObject that holds all static data for a weapon.
/// Create via: Right-click > Create > Combat/Weapon Data
/// Extend this with durability, ammo, rarity, etc. later.
/// </summary>
/// 

[CreateAssetMenu(menuName = "Combat/Weapon Data", fileName = "NewWeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Unnamed Weapon";

    [Header("HUD")]
    [Tooltip("The icon shown in the active weapon slot on the HUD.")]
    public Sprite hudIcon;

    [Header("Stats (extend here for durability, ammo, etc.)")]
    public int maxDurability = 100;
    // public int maxAmmo = 30;  // uncomment when you add ranged weapons
}
