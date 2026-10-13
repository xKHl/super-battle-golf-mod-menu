using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Weapon options and player speed.
    ///
    /// Item uses (from the decompiled game): there is ONE counter per inventory slot, InventorySlot.remainingUses.
    /// The game has no separate ammunition or reload state. Every use sends a per-use validation hash that the server
    /// issued when it granted the item (PlayerInventory.serverValidItemUseHashes); the server consumes the hash and
    /// treats an unknown one as cheating (OnInvalidItemUse). The slot list itself is a server-owned SyncList.
    /// => Use counts can only be kept from the server side, i.e. when this player is the host. As host the mod skips
    ///    the decrement and hands the consumed hash back with the game's own ServerReturnItemUse.
    ///
    /// Cooldown: the only weapon cooldown in the game is ItemSettings.ElephantGunShotCooldown, checked on the client
    /// in PlayerInventory.CanUseEquippedItem.
    ///
    /// Speed: the local player is simulated on this client. PlayerMovement.ApplyMovement scales the applied
    /// acceleration by the static consoleSpeedFactor (the game's own "moveSpeedFactor" console variable), which scales
    /// the resulting top speed linearly. Setting it (never multiplying it) cannot accumulate.
    /// </summary>
    public static class Tweaks
    {
        private static float baseElephantCooldown = -1f;
        private static bool cooldownPatched;

        public static bool IsAmmoWeapon(ItemType item)
        {
            switch (item)
            {
                case ItemType.DuelingPistol:
                case ItemType.ElephantGun:
                case ItemType.RocketLauncher:
                case ItemType.Railgun:
                case ItemType.FreezeBomb:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the use of this item should not be consumed right now.</summary>
        public static bool KeepsUses(ItemType item)
        {
            if (item == ItemType.None || !NetworkServer.active)
            {
                return false;
            }
            return IsAmmoWeapon(item) ? Plugin.Cfg.InfiniteAmmo.Value : Plugin.Cfg.InfiniteUses.Value;
        }

        public static string UsesStatus()
        {
            if (Game.Golfer == null)
            {
                return "not in a match";
            }
            return NetworkServer.active
                ? "active: you are the host, uses are kept on the server"
                : "inactive: the host's server counts and validates every use (see note)";
        }

        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.DecrementUseFromSlotAt))]
        [HarmonyPrefix]
        private static bool BeforeDecrement(PlayerInventory __instance, int index)
        {
            if (__instance == null || !__instance.isLocalPlayer || !NetworkServer.active)
            {
                return true;
            }
            if (!KeepsUses(__instance.GetEffectiveSlot(index).itemType))
            {
                return true;
            }
            UsesKept++;
            return false;
        }

        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.ServerValidateAndConsumeItemUse))]
        [HarmonyPostfix]
        private static void AfterConsume(PlayerInventory __instance, ulong itemUseHash, bool __result)
        {
            if (!__result || __instance == null || !__instance.isLocalPlayer || !NetworkServer.active)
            {
                return;
            }
            // The decrement was skipped, so the slot that produced this hash still reports the same hash.
            for (int i = 0; i < __instance.slots.Count; i++)
            {
                InventorySlot slot = __instance.GetEffectiveSlot(i);
                if (slot.itemType != ItemType.None && slot.remainingUses > 0 && slot.GetUsageValidationHash() == itemUseHash && KeepsUses(slot.itemType))
                {
                    __instance.ServerReturnItemUse(itemUseHash);
                    HashesReturned++;
                    return;
                }
            }
        }

        public static void Update()
        {
            ItemSettings items = Game.Try(() => GameManager.ItemSettings);
            if (items != null)
            {
                if (baseElephantCooldown < 0f)
                {
                    baseElephantCooldown = items.ElephantGunShotCooldown;
                }
                bool want = Plugin.Cfg.NoCooldown.Value;
                if (want != cooldownPatched)
                {
                    items.ElephantGunShotCooldown = want ? 0f : baseElephantCooldown;
                    cooldownPatched = want;
                }
            }
            float factor = Plugin.Cfg.SpeedEnabled.Value ? SpeedMultiplier : 1f;
            if (!Mathf.Approximately(factor, PeakFactor))
            {
                PeakFactor = factor;
                PeakSpeed = 0f;
            }
            PlayerMovement move = Game.Movement;
            if (move != null && Game.Try(() => move.IsGrounded))
            {
                PeakSpeed = Mathf.Max(PeakSpeed, CurrentSpeed());
            }
            BaseRunSpeed = Game.Try(() => GameManager.PlayerMovementSettings.DefaultMoveSpeed);
            if (!Mathf.Approximately(PlayerMovement.consoleSpeedFactor, factor))
            {
                PlayerMovement.consoleSpeedFactor = factor;
            }
        }

        public static float SpeedMultiplier => Mathf.Clamp(Mathf.Round(Plugin.Cfg.SpeedMultiplier.Value * 10f) / 10f, 0.5f, 5f);

        public static float BaseElephantCooldown => baseElephantCooldown;

        /// <summary>How many real uses were kept this session (decrements skipped / validation hashes handed back).</summary>
        public static int UsesKept { get; private set; }
        public static int HashesReturned { get; private set; }

        /// <summary>Highest grounded horizontal speed observed since the factor last changed (verification aid).</summary>
        public static float PeakSpeed { get; private set; }
        public static float PeakFactor { get; private set; } = 1f;
        public static float BaseRunSpeed { get; private set; }

        public static void Restore()
        {
            PlayerMovement.consoleSpeedFactor = 1f;
            ItemSettings items = Game.Try(() => GameManager.ItemSettings);
            if (items != null && cooldownPatched && baseElephantCooldown >= 0f)
            {
                items.ElephantGunShotCooldown = baseElephantCooldown;
                cooldownPatched = false;
            }
        }

        /// <summary>Horizontal speed of the local player right now, for the UI and for verification.</summary>
        public static float CurrentSpeed()
        {
            PlayerMovement m = Game.Movement;
            if (m == null)
            {
                return 0f;
            }
            Vector3 v = Game.Try(() => m.Velocity);
            v.y = 0f;
            return v.magnitude;
        }
    }
}
