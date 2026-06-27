using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Barotrauma;
using Barotrauma.LuaCs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.ElysianRealm
{
    public sealed class ElysianGameplayPlugin : IAssemblyPlugin
    {
        private const string CharacterControlHook = "elysianrealm.gameplay.character.control";
        private const string BowDrawHudHook = "elysianrealm.gameplay.rangedweapon.drawhud.after";
        private const string ProjectileImpactHook = "elysianrealm.gameplay.projectile.impact.after";
        private const string ProjectileShootHook = "elysianrealm.gameplay.projectile.shoot.after";
        private const string RangedWeaponBeforeHook = "elysianrealm.gameplay.rangedweapon.use.before";
        private const string RangedWeaponAfterHook = "elysianrealm.gameplay.rangedweapon.use.after";
        private const string NightVisionLightMapBeforeHook = "elysianrealm.gameplay.lightmap.nightvision.before";
        private const string NightVisionLightMapAfterHook = "elysianrealm.gameplay.lightmap.nightvision.after";

        private const string BowIdentifier = "pastflower";
        private const string ArrowIdentifier = "lovespears";
        private const string SuperArrowIdentifier = "lovespears_super";
        private const string HornBuffIdentifier = "elysiaencouragement";
        private const string ChargeSoundAfflictionIdentifier = "pastflower_charge_sound";
        private const string BowNoAmmoHintText = "\u7231\u8389\u5e0c\u96c5\u7684\u6e29\u99a8\u63d0\u793a\uff1a\u9700\u8981\u7231\u77db";
        private const string ChargeSpriteRelativePath = "Assets/UI/Pillarofflame.png";
        private const string ExplosionSpriteRelativePath = "Assets/UI/\u771f\u62111.png";

        private const float BowSuperChargeSeconds = 15.0f;
        private const float BowMinChargeSeconds = 0.5f;
        private const float BowChargeVisualStartSeconds = 0.5f;
        private const float BowSuperImpulseMultiplier = 10.0f;
        private const float PastflowerBeamVisualSeconds = 0.34f;
        private const float PastflowerExplosionVisualSeconds = 0.85f;
        private const float PastflowerExplosionVisualScale = 2.0f;
        private const float PastflowerExplosionRange = 500.0f;
        private const float PastflowerExplosionInternalDamage = 500.0f;
        private const float PastflowerExplosionStructureDamage = 300.0f;
        private const float LoveSpearInternalDamage = 75.0f;
        private const float LoveSpearLacerationDamage = 200.0f;
        private const float LoveSpearBleedingDamage = 90.0f;
        private const float LoveSpearStunDamage = 0.5f;
        private const float PastflowerDirectHitRadius = 220.0f;
        private const float BowNoAmmoHintSeconds = 2.0f;
        private const float BowNoAmmoHintCooldownSeconds = 0.75f;
        private const float PastflowerSuperVoiceRange = 8000.0f;
        private const float PastflowerSuperVoiceVolume = 2.2f;
        private const int PendingSuperShotMilliseconds = 8000;
        private const float NightVisionAfflictionThreshold = 0.1f;
        private const float NightVisionAmbientBoost = 0.62f;
        private const int NightVisionAmbientTargetR = 46;
        private const int NightVisionAmbientTargetG = 52;
        private const int NightVisionAmbientTargetB = 50;

        private static readonly Dictionary<Character, ChargeState> ChargeStates = new Dictionary<Character, ChargeState>();
        private static readonly Dictionary<Character, int> BowNoAmmoHintTicks = new Dictionary<Character, int>();
        private static readonly Dictionary<object, WeaponOverride> WeaponOverrides = new Dictionary<object, WeaponOverride>();
        private static readonly Dictionary<Item, SuperShotData> SuperProjectiles = new Dictionary<Item, SuperShotData>();
        private static readonly HashSet<Item> RemovedVolleyAmmo = new HashSet<Item>();
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();
        private static readonly List<PendingSuperShot> PendingSuperShots = new List<PendingSuperShot>();
        private static readonly List<BeamVisual> BeamVisuals = new List<BeamVisual>();
        private static readonly List<ExplosionVisual> ExplosionVisuals = new List<ExplosionVisual>();
        private static readonly List<DelayedItemRemoval> DelayedItemRemovals = new List<DelayedItemRemoval>();
        private static readonly List<RegisteredHook> RegisteredHooks = new List<RegisteredHook>();
        private const string NightVisionAfflictionIdentifier = "elysian_slot_stigmata_mid_human_effect";
        private static readonly string[] PastflowerSuperVoiceAfflictions = new[]
        {
            "pastflower_super_voice_escape",
            "pastflower_super_voice_flower",
            "pastflower_super_voice_surprise"
        };
        private static readonly string[] PastflowerSuperVoiceFiles = new[]
        {
            "Assets/Audio/\u7231\u8389\u5e0c\u96c5-\u9003\u4e0d\u6389\u54e6~.ogg",
            "Assets/Audio/\u7231\u8389\u5e0c\u96c5-\u9001\u4f60\u4e00\u6735\u82b1~.ogg",
            "Assets/Audio/\u4eba\u4e4b\u5f8b\u8005-\u9001\u4f60\u4e00\u70b9\u60ca\u559c\u3002.ogg"
        };
        private static ContentPackage ownerPackage;
        private static MethodInfo characterIsKeyDownMethod;
        private static MethodInfo characterIsKeyHitMethod;
        private static MethodInfo guiDrawStringMethod;
        private static MethodInfo toDisplayUnitsMethod;
        private static List<MethodInfo> soundPlayMethods;
        private static Type rangedWeaponType;
        private static Sprite chargeSprite;
        private static Sprite explosionSprite;
        private static bool chargeSpriteLoadFailed;
        private static bool explosionSpriteLoadFailed;
        private static bool guiDrawStringLookupFailed;
        private static bool guiDrawStringInvokeFailed;
        private static bool toDisplayUnitsLookupFailed;
        private static bool soundPlayLookupFailed;
        private static bool directVoicePlayFailed;
        private static bool nightVisionLightHookFailed;
        private static bool nightVisionLightHookRegistered;
        private static bool nightVisionAmbientRestorePending;
        private static Color nightVisionOriginalAmbient;
        private static bool registered;

        private sealed class RegisteredHook
        {
            public RegisteredHook(string identifier, MethodBase method, ILuaCsHook.HookMethodType hookType)
            {
                Identifier = identifier;
                Method = method;
                HookType = hookType;
            }

            public readonly string Identifier;
            public readonly MethodBase Method;
            public readonly ILuaCsHook.HookMethodType HookType;
        }

        public void PreInitPatching()
        {
            EnsureInitialized(this, null);
        }

        internal static void EnsureInitialized(IAssemblyPlugin hookOwner, ContentPackage packageOverride)
        {
            if (registered)
            {
                return;
            }

            registered = true;
            ownerPackage = packageOverride;
            if (ownerPackage == null)
            {
                LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianGameplayPlugin>(out ownerPackage);
            }

            ElysianBuffPlugin.EnsureInitialized(hookOwner, ownerPackage);
            CacheInputMethods();
            HookCharacterControl(hookOwner);
            HookBowHud(hookOwner);
            HookProjectileImpact(hookOwner);
            HookRangedWeaponUse(hookOwner);
            HookNightVisionLightMap(hookOwner);

            string packageDir = ownerPackage == null ? "<unresolved>" : ownerPackage.Dir;
            LuaCsLogger.LogMessage("[ElysianRealm] Gameplay plugin registered. Package=" + packageDir);
        }

        public void Initialize()
        {
        }

        public void OnLoadCompleted()
        {
            if (!nightVisionLightHookRegistered)
            {
                HookNightVisionLightMap(this);
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        internal static void Shutdown()
        {
            UnhookRegisteredMethods();
            ChargeStates.Clear();
            BowNoAmmoHintTicks.Clear();
            WeaponOverrides.Clear();
            SuperProjectiles.Clear();
            RemovedVolleyAmmo.Clear();
            PendingSuperShots.Clear();
            BeamVisuals.Clear();
            ExplosionVisuals.Clear();
            DelayedItemRemovals.Clear();
            RestoreNightVisionAmbient(FindGameMainLightManager());
            LoggedOnce.Clear();
            RemoveSprite(ref chargeSprite);
            RemoveSprite(ref explosionSprite);
            guiDrawStringMethod = null;
            toDisplayUnitsMethod = null;
            soundPlayMethods = null;
            chargeSpriteLoadFailed = false;
            explosionSpriteLoadFailed = false;
            guiDrawStringLookupFailed = false;
            guiDrawStringInvokeFailed = false;
            toDisplayUnitsLookupFailed = false;
            soundPlayLookupFailed = false;
            directVoicePlayFailed = false;
            nightVisionLightHookFailed = false;
            nightVisionLightHookRegistered = false;
            nightVisionAmbientRestorePending = false;
            ownerPackage = null;
            registered = false;
        }

        private static void RememberHook(string identifier, MethodBase method, ILuaCsHook.HookMethodType hookType)
        {
            RegisteredHooks.Add(new RegisteredHook(identifier, method, hookType));
        }

        private static void UnhookRegisteredMethods()
        {
            bool loggedFailure = false;

            for (int i = RegisteredHooks.Count - 1; i >= 0; i--)
            {
                RegisteredHook hook = RegisteredHooks[i];
                try
                {
                    if (!TryUnhookLuaCsMethod(hook.Identifier, hook.Method, hook.HookType))
                    {
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    if (!loggedFailure)
                    {
                        loggedFailure = true;
                        LuaCsLogger.LogError("[ElysianRealm] Failed to unhook one or more gameplay methods: " + ex.GetType().Name);
                    }
                }
            }

            RegisteredHooks.Clear();
        }

        private static bool TryUnhookLuaCsMethod(string identifier, MethodBase method, ILuaCsHook.HookMethodType hookType)
        {
            object eventService = LuaCsSetup.Instance.EventService;
            if (eventService == null || method == null)
            {
                return false;
            }

            object patcher = GetMemberValue(eventService, "_luaPatcher") ?? eventService;
            MethodInfo unhookMethod = patcher.GetType().GetMethod(
                "UnhookMethod",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(MethodBase), typeof(ILuaCsHook.HookMethodType) },
                null);

            if (unhookMethod == null)
            {
                return false;
            }

            unhookMethod.Invoke(patcher, new object[] { identifier, method, hookType });
            return true;
        }

        private static void HookCharacterControl(IAssemblyPlugin hookOwner)
        {
            MethodInfo method = typeof(Character).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "Control", StringComparison.Ordinal) &&
                                     m.GetParameters().Any(p => string.Equals(p.Name, "deltaTime", StringComparison.OrdinalIgnoreCase)));

            if (method == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] Character.Control was not found; gameplay input features disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                CharacterControlHook,
                method,
                CharacterControlAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
            RememberHook(CharacterControlHook, method, ILuaCsHook.HookMethodType.After);
        }

        private static void HookRangedWeaponUse(IAssemblyPlugin hookOwner)
        {
            Type type = FindRangedWeaponType();
            if (type == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] RangedWeapon type was not found; bow super shot disabled.");
                return;
            }

            MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "Use", StringComparison.Ordinal) &&
                                     m.GetParameters().Any(p => p.ParameterType == typeof(Character)));

            if (method == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] RangedWeapon.Use was not found; bow super shot disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                RangedWeaponBeforeHook,
                method,
                RangedWeaponUseBefore,
                ILuaCsHook.HookMethodType.Before,
                owner: hookOwner);
            RememberHook(RangedWeaponBeforeHook, method, ILuaCsHook.HookMethodType.Before);

            LuaCsSetup.Instance.EventService.HookMethod(
                RangedWeaponAfterHook,
                method,
                RangedWeaponUseAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
            RememberHook(RangedWeaponAfterHook, method, ILuaCsHook.HookMethodType.After);
        }

        private static void HookBowHud(IAssemblyPlugin hookOwner)
        {
            Type type = FindRangedWeaponType();
            if (type == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] RangedWeapon type was not found; bow charge bar disabled.");
                return;
            }

            MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "DrawHUD", StringComparison.Ordinal) &&
                                     m.GetParameters().Any(p => p.ParameterType == typeof(SpriteBatch)) &&
                                     m.GetParameters().Any(p => p.ParameterType == typeof(Character)));

            if (method == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] RangedWeapon.DrawHUD was not found; bow charge bar disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                BowDrawHudHook,
                method,
                BowDrawHudAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
            RememberHook(BowDrawHudHook, method, ILuaCsHook.HookMethodType.After);
        }

        private static void HookProjectileImpact(IAssemblyPlugin hookOwner)
        {
            Type projectileType = FindTypeByName("Projectile");
            if (projectileType == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] Projectile component type was not found; pastflower impact explosion disabled.");
                return;
            }

            List<MethodInfo> impactMethods = projectileType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsProjectileImpactMethod)
                .GroupBy(m => m.MetadataToken)
                .Select(g => g.First())
                .ToList();

            if (impactMethods.Count == 0)
            {
                LuaCsLogger.LogError("[ElysianRealm] Projectile impact method was not found; pastflower impact explosion disabled.");
            }

            foreach (MethodInfo method in impactMethods)
            {
                string hookIdentifier = ProjectileImpactHook + "." + method.Name + "." + method.MetadataToken;
                LuaCsSetup.Instance.EventService.HookMethod(
                    hookIdentifier,
                    method,
                    ProjectileImpactAfter,
                    ILuaCsHook.HookMethodType.After,
                    owner: hookOwner);
                RememberHook(hookIdentifier, method, ILuaCsHook.HookMethodType.After);
            }

            List<MethodInfo> shootMethods = projectileType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsProjectileShootMethod)
                .GroupBy(m => m.MetadataToken)
                .Select(g => g.First())
                .ToList();

            foreach (MethodInfo method in shootMethods)
            {
                string hookIdentifier = ProjectileShootHook + "." + method.Name + "." + method.MetadataToken;
                LuaCsSetup.Instance.EventService.HookMethod(
                    hookIdentifier,
                    method,
                    ProjectileShootAfter,
                    ILuaCsHook.HookMethodType.After,
                    owner: hookOwner);
                RememberHook(hookIdentifier, method, ILuaCsHook.HookMethodType.After);
            }

            LuaCsLogger.LogMessage("[ElysianRealm] Projectile hooks registered. impact=" + impactMethods.Count + ", shoot=" + shootMethods.Count);
        }

        private static void HookNightVisionLightMap(IAssemblyPlugin hookOwner)
        {
            if (nightVisionLightHookRegistered)
            {
                return;
            }

            object lightManager = FindGameMainLightManager();
            if (lightManager == null)
            {
                nightVisionLightHookFailed = true;
                LuaCsLogger.LogError("[ElysianRealm] LightManager was not found; stigmata night vision ambient boost disabled.");
                return;
            }

            MethodInfo method = lightManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "RenderLightMap", StringComparison.Ordinal) &&
                                     m.GetParameters().Length >= 2);

            if (method == null)
            {
                nightVisionLightHookFailed = true;
                LuaCsLogger.LogError("[ElysianRealm] LightManager.RenderLightMap was not found; stigmata night vision ambient boost disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                NightVisionLightMapBeforeHook,
                method,
                NightVisionLightMapBefore,
                ILuaCsHook.HookMethodType.Before,
                owner: hookOwner);
            RememberHook(NightVisionLightMapBeforeHook, method, ILuaCsHook.HookMethodType.Before);

            LuaCsSetup.Instance.EventService.HookMethod(
                NightVisionLightMapAfterHook,
                method,
                NightVisionLightMapAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
            RememberHook(NightVisionLightMapAfterHook, method, ILuaCsHook.HookMethodType.After);

            nightVisionLightHookFailed = false;
            nightVisionLightHookRegistered = true;
            LuaCsLogger.LogMessage("[ElysianRealm] Night vision ambient hook registered.");
        }

        private static bool IsProjectileImpactMethod(MethodInfo method)
        {
            if (method == null || method.IsSpecialName || method.GetParameters().Length == 0)
            {
                return false;
            }

            foreach (string name in new[] { "HandleProjectileCollision", "OnProjectileCollision", "OnCollision", "Impact" })
            {
                if (string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return method.Name.IndexOf("Collision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   method.Name.IndexOf("Impact", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsProjectileShootMethod(MethodInfo method)
        {
            if (method == null || method.IsSpecialName)
            {
                return false;
            }

            return string.Equals(method.Name, "Shoot", StringComparison.Ordinal) ||
                   string.Equals(method.Name, "Launch", StringComparison.Ordinal);
        }

        private static object CharacterControlAfter(object self, Dictionary<string, object> args)
        {
            UpdateDelayedItemRemovals();
            PruneExpiredPendingSuperShots();

            Character character = self as Character;
            if (!IsUsableCharacter(character))
            {
                return null;
            }

            float deltaTime = GetFloatArg(args, "deltaTime", 1.0f / 60.0f);
            UpdateBowCharge(character, deltaTime);
            return null;
        }

        private static object RangedWeaponUseBefore(object self, Dictionary<string, object> args)
        {
            object rangedWeapon = IsRangedWeapon(self) ? self : null;
            if (rangedWeapon == null)
            {
                return null;
            }

            Character character = GetCharacterArg(args);
            if (!IsUsableCharacter(character))
            {
                return null;
            }

            Item bow = GetComponentItem(rangedWeapon);
            if (!HasIdentifier(bow, BowIdentifier))
            {
                return null;
            }

            ChargeState state = GetChargeState(character);
            if (state.ChargeSeconds < BowMinChargeSeconds)
            {
                return null;
            }

            if (!IsInputDown(character, "Aim") || !IsHoldingOnlyBow(character, bow))
            {
                return null;
            }

            if (!EnsureBowLoadedFromInventory(character, bow))
            {
                ShowPastflowerNoAmmoHint(character);
                LogOnce("bow_no_inventory_arrow", "[ElysianRealm] Pastflower shot blocked: no lovespears in character inventory.");
                return null;
            }

            List<Item> ammo = FindArrowAmmo(character, bow);
            if (ammo.Count <= 0)
            {
                return null;
            }

            Item loadedArrow = FindLoadedArrow(bow) ?? ammo[0];
            bool isSuperShot = state.ChargeSeconds >= BowSuperChargeSeconds;
            int arrowCount = isSuperShot ? Math.Max(1, CountArrowAmount(ammo)) : 1;
            WeaponOverride weaponOverride = isSuperShot ?
                CreateSuperShotOverride(rangedWeapon, loadedArrow, arrowCount) :
                CreateNormalShotOverride(rangedWeapon);
            if (!weaponOverride.HasAnyChange)
            {
                LogOnce("bow_override_failed", "[ElysianRealm] Could not override bow projectile values; pastflower shot will use XML defaults.");
            }
            else
            {
                WeaponOverrides[rangedWeapon] = weaponOverride;
            }

            int consumed = 0;
            if (isSuperShot)
            {
                consumed = ConsumeExtraVolleyAmmo(ammo, int.MaxValue);
                Item superLoadedArrow;
                if (TryReplaceLoadedArrowWithSuperProjectile(character, bow, loadedArrow, out superLoadedArrow))
                {
                    loadedArrow = superLoadedArrow;
                }
                else
                {
                    LogOnce("bow_super_arrow_replace_failed", "[ElysianRealm] Failed to replace loaded lovespear with super impact projectile; C# impact fallback will be used.");
                }

                SuperShotData superShotData = new SuperShotData(character, arrowCount);
                if (loadedArrow != null)
                {
                    SuperProjectiles[loadedArrow] = superShotData;
                }
                QueuePendingSuperShot(character, loadedArrow, arrowCount);
                ApplyAffliction(character, HornBuffIdentifier, 10.0f);
                PlayRandomPastflowerSuperVoice(character);
                AddPastflowerBeamVisual(character, bow);
            }

            state.ChargeSeconds = 0.0f;
            state.WasReadyLogged = false;
            state.WasFullyChargedLogged = false;
            state.WasChargeSoundPlayed = false;

            if (isSuperShot)
            {
                LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super shot prepared. arrows=" + arrowCount + ", extraConsumed=" + consumed);
            }
            else
            {
                LuaCsLogger.LogMessage("[ElysianRealm] Pastflower normal shot prepared.");
            }
            return null;
        }

        private static object RangedWeaponUseAfter(object self, Dictionary<string, object> args)
        {
            WeaponOverride weaponOverride;
            if (self != null && WeaponOverrides.TryGetValue(self, out weaponOverride))
            {
                weaponOverride.Restore();
                WeaponOverrides.Remove(self);
            }

            return null;
        }

        private static object ProjectileShootAfter(object self, Dictionary<string, object> args)
        {
            Item projectileItem = GetComponentItem(self);
            if (!IsPastflowerArrow(projectileItem))
            {
                return null;
            }

            SuperShotData existingData;
            if (SuperProjectiles.TryGetValue(projectileItem, out existingData))
            {
                RemovePendingSuperShotForProjectile(projectileItem, existingData.Attacker);
                return null;
            }

            SuperShotData data;
            if (TryClaimPendingSuperShot(projectileItem, GetCharacterArg(args), out data))
            {
                SuperProjectiles[projectileItem] = data;
                LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super projectile bound on shoot. arrows=" + data.ArrowCount);
            }

            return null;
        }

        private static object ProjectileImpactAfter(object self, Dictionary<string, object> args)
        {
            Item projectileItem = GetComponentItem(self);
            if (projectileItem == null)
            {
                return null;
            }

            SuperShotData data;
            if (!TryTakeSuperProjectileData(projectileItem, out data))
            {
                return null;
            }

            Vector2 position;
            if (!TryGetProjectileImpactWorldPosition(args, projectileItem, out position) &&
                !TryGetWorldPosition(projectileItem, out position))
            {
                position = data.Attacker == null ? Vector2.Zero : data.Attacker.WorldPosition;
            }

            ApplyPastflowerExplosion(data.Attacker, position);
            ApplyPastflowerSuperDirectDamage(data.Attacker, position, data.ArrowCount);
            AddPastflowerExplosionVisual(position);
            ScheduleDelayedItemRemoval(projectileItem, PastflowerExplosionVisualSeconds);
            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super impact explosion applied. arrows=" + data.ArrowCount);
            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super projectile removal scheduled.");
            return null;
        }

        private static object NightVisionLightMapBefore(object self, Dictionary<string, object> args)
        {
            object lightManager = self ?? FindGameMainLightManager();
            RestoreNightVisionAmbient(lightManager);

            Character character = GetControlledCharacter();
            if (!HasNightVisionAffliction(character))
            {
                return null;
            }

            Color currentAmbient;
            if (!TryGetAmbientLight(lightManager, out currentAmbient))
            {
                if (!nightVisionLightHookFailed)
                {
                    nightVisionLightHookFailed = true;
                    LuaCsLogger.LogError("[ElysianRealm] Could not read LightManager.AmbientLight; stigmata night vision ambient boost disabled.");
                }
                return null;
            }

            Color boostedAmbient = BuildNightVisionAmbient(currentAmbient);
            if (boostedAmbient.PackedValue == currentAmbient.PackedValue)
            {
                return null;
            }

            nightVisionOriginalAmbient = currentAmbient;
            if (TrySetMemberValue(lightManager, "AmbientLight", boostedAmbient))
            {
                nightVisionAmbientRestorePending = true;
                LogOnce("nightvision_ambient_boost_active", "[ElysianRealm] Stigmata night vision ambient boost active.");
            }

            return null;
        }

        private static object NightVisionLightMapAfter(object self, Dictionary<string, object> args)
        {
            RestoreNightVisionAmbient(self ?? FindGameMainLightManager());
            return null;
        }

        private static object BowDrawHudAfter(object self, Dictionary<string, object> args)
        {
            object rangedWeapon = IsRangedWeapon(self) ? self : null;
            if (rangedWeapon == null)
            {
                return null;
            }

            Item bow = GetComponentItem(rangedWeapon);
            if (!HasIdentifier(bow, BowIdentifier))
            {
                return null;
            }

            Character character = GetCharacterArg(args);
            if (!IsUsableCharacter(character))
            {
                return null;
            }

            SpriteBatch spriteBatch = GetArgByType<SpriteBatch>(args, "spriteBatch");
            if (spriteBatch == null || GUI.WhiteTexture == null)
            {
                return null;
            }

            DrawPastflowerActiveShotVisuals(spriteBatch);
            DrawPastflowerNoAmmoHint(spriteBatch, character);

            if (!IsInputDown(character, "Aim"))
            {
                return null;
            }

            ChargeState state = GetChargeState(character);
            if (!HasSufficientArrowAmmoForCharge(character, bow))
            {
                ResetBowChargeState(state);
                return null;
            }

            if (state.ChargeSeconds < BowChargeVisualStartSeconds)
            {
                return null;
            }

            LogOnce("bow_charge_visuals", "[ElysianRealm] Pastflower charge visuals are drawing.");
            DrawBowChargeVisuals(spriteBatch, bow, character, state.ChargeSeconds);
            return null;
        }

        private static Character GetControlledCharacter()
        {
            return GetStaticMemberValue(typeof(Character), "Controlled") as Character;
        }

        private static bool HasNightVisionAffliction(Character character)
        {
            if (!IsUsableCharacter(character))
            {
                return false;
            }

            return GetAfflictionStrength(character, NightVisionAfflictionIdentifier) > NightVisionAfflictionThreshold;
        }

        private static object FindGameMainLightManager()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type gameMainType = assembly.GetType("Barotrauma.GameMain", false);
                if (gameMainType == null)
                {
                    continue;
                }

                object lightManager = GetStaticMemberValue(gameMainType, "LightManager");
                if (lightManager != null)
                {
                    return lightManager;
                }
            }

            return null;
        }

        private static bool TryGetAmbientLight(object lightManager, out Color ambientLight)
        {
            object value = GetMemberValue(lightManager, "AmbientLight");
            if (value is Color)
            {
                ambientLight = (Color)value;
                return true;
            }

            ambientLight = new Color(0, 0, 0, 0);
            return false;
        }

        private static Color BuildNightVisionAmbient(Color current)
        {
            return new Color(
                BuildNightVisionAmbientChannel(current.R, NightVisionAmbientTargetR),
                BuildNightVisionAmbientChannel(current.G, NightVisionAmbientTargetG),
                BuildNightVisionAmbientChannel(current.B, NightVisionAmbientTargetB),
                current.A);
        }

        private static int BuildNightVisionAmbientChannel(byte current, int target)
        {
            int value = current;
            if (value >= target)
            {
                return value;
            }

            float boosted = value + (target - value) * NightVisionAmbientBoost;
            return Math.Max(value, Math.Min(target, (int)Math.Round(boosted)));
        }

        private static void RestoreNightVisionAmbient(object lightManager)
        {
            if (!nightVisionAmbientRestorePending)
            {
                return;
            }

            TrySetMemberValue(lightManager, "AmbientLight", nightVisionOriginalAmbient);
            nightVisionAmbientRestorePending = false;
        }

        private static void UpdateBowCharge(Character character, float deltaTime)
        {
            ChargeState state = GetChargeState(character);
            Item bow = FindHeldItem(character, BowIdentifier);
            if (bow == null || !IsInputDown(character, "Aim") || !IsHoldingOnlyBow(character, bow))
            {
                if (state.ChargeSeconds > 0.0f)
                {
                    ResetBowChargeState(state);
                }
                return;
            }

            if (!HasSufficientArrowAmmoForCharge(character, bow))
            {
                if (IsInputHit(character, "Shoot") || IsInputHit(character, "Use"))
                {
                    ShowPastflowerNoAmmoHint(character);
                }

                if (state.ChargeSeconds > 0.0f)
                {
                    ResetBowChargeState(state);
                }
                return;
            }

            state.ChargeSeconds += Math.Max(0.0f, deltaTime);
            if (state.ChargeSeconds >= BowMinChargeSeconds && !state.WasChargeSoundPlayed)
            {
                state.WasChargeSoundPlayed = true;
                ApplyAffliction(character, ChargeSoundAfflictionIdentifier, 1.0f);
            }

            if (state.ChargeSeconds >= BowMinChargeSeconds && !state.WasReadyLogged)
            {
                state.WasReadyLogged = true;
                LogOnce("bow_ready", "[ElysianRealm] Pastflower bow normal charge ready.");
            }

            if (state.ChargeSeconds >= BowSuperChargeSeconds && !state.WasFullyChargedLogged)
            {
                state.WasFullyChargedLogged = true;
                LuaCsLogger.LogMessage("[ElysianRealm] Pastflower bow super charge ready.");
            }
        }

        private static void ShowPastflowerNoAmmoHint(Character character)
        {
            if (character == null)
            {
                return;
            }

            int now = Environment.TickCount;
            int previous;
            if (BowNoAmmoHintTicks.TryGetValue(character, out previous) &&
                TicksToSeconds(unchecked(now - previous)) < BowNoAmmoHintCooldownSeconds)
            {
                return;
            }

            BowNoAmmoHintTicks[character] = now;
        }

        private static void DrawPastflowerNoAmmoHint(SpriteBatch spriteBatch, Character character)
        {
            int started;
            if (character == null || !BowNoAmmoHintTicks.TryGetValue(character, out started))
            {
                return;
            }

            float age = TicksToSeconds(unchecked(Environment.TickCount - started));
            if (age >= BowNoAmmoHintSeconds)
            {
                BowNoAmmoHintTicks.Remove(character);
                return;
            }

            float fadeStart = Math.Max(0.1f, BowNoAmmoHintSeconds - 0.35f);
            float alpha = age < fadeStart ? 1.0f : MathHelper.Clamp((BowNoAmmoHintSeconds - age) / Math.Max(0.01f, BowNoAmmoHintSeconds - fadeStart), 0.0f, 1.0f);
            Vector2 pos = PlayerInput.MousePosition + new Vector2(34.0f, -54.0f);
            int x = Math.Max(18, (int)Math.Round(pos.X));
            int y = Math.Max(18, (int)Math.Round(pos.Y));
            Rectangle panel = new Rectangle(x, y, 318, 34);
            Rectangle accent = new Rectangle(x, y, 4, 34);
            Color hintColor = new Color(255, 86, 217, 255) * alpha;

            if (GUI.WhiteTexture != null)
            {
                spriteBatch.Draw(GUI.WhiteTexture, panel, Color.Black * (0.66f * alpha));
                spriteBatch.Draw(GUI.WhiteTexture, accent, hintColor);
            }

            DrawGuiString(spriteBatch, new Vector2(x + 12.0f, y + 8.0f), BowNoAmmoHintText, hintColor);
        }

        private static void DrawGuiString(SpriteBatch spriteBatch, Vector2 position, string text, Color color)
        {
            MethodInfo method = GetGuiDrawStringMethod(text);
            if (method == null)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] values = new object[parameters.Length];
                bool textSet = false;
                bool spriteBatchSet = false;
                bool positionSet = false;
                bool colorSet = false;

                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    object textValue;

                    if (!spriteBatchSet && parameterType.IsAssignableFrom(typeof(SpriteBatch)))
                    {
                        spriteBatchSet = true;
                        values[i] = spriteBatch;
                    }
                    else if (!positionSet && parameterType == typeof(Vector2))
                    {
                        positionSet = true;
                        values[i] = position;
                    }
                    else if (!textSet && TryCreateGuiTextParameter(parameterType, text, out textValue))
                    {
                        textSet = true;
                        values[i] = textValue;
                    }
                    else if (!colorSet && parameterType == typeof(Color))
                    {
                        colorSet = true;
                        values[i] = color;
                    }
                    else
                    {
                        values[i] = GetParameterDefault(parameters[i]);
                    }
                }

                method.Invoke(null, values);
            }
            catch (Exception ex)
            {
                if (!guiDrawStringInvokeFailed)
                {
                    guiDrawStringInvokeFailed = true;
                    LuaCsLogger.LogError("[ElysianRealm] Failed to draw no-ammo hint text: " + ex.Message);
                }
            }
        }

        private static MethodInfo GetGuiDrawStringMethod(string sampleText)
        {
            if (guiDrawStringMethod != null || guiDrawStringLookupFailed)
            {
                return guiDrawStringMethod;
            }

            MethodInfo[] methods = typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, "DrawString", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 4 ||
                    !parameters[0].ParameterType.IsAssignableFrom(typeof(SpriteBatch)) ||
                    parameters[1].ParameterType != typeof(Vector2) ||
                    !HasColorParameter(parameters))
                {
                    continue;
                }

                object ignored;
                if (!TryCreateGuiTextParameter(parameters[2].ParameterType, sampleText, out ignored))
                {
                    continue;
                }

                guiDrawStringMethod = method;
                return guiDrawStringMethod;
            }

            guiDrawStringLookupFailed = true;
            LogOnce("gui_draw_string_missing", "[ElysianRealm] GUI.DrawString method was not found; no-ammo hint text disabled.");
            return null;
        }

        private static bool HasColorParameter(ParameterInfo[] parameters)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(Color))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateGuiTextParameter(Type parameterType, string text, out object value)
        {
            if (parameterType == typeof(string) || parameterType.IsAssignableFrom(typeof(string)))
            {
                value = text;
                return true;
            }

            MethodInfo[] methods = parameterType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (MethodInfo method in methods)
            {
                if ((method.Name != "op_Implicit" && method.Name != "op_Explicit") ||
                    method.ReturnType != parameterType)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    value = method.Invoke(null, new object[] { text });
                    return true;
                }
            }

            ConstructorInfo constructor = parameterType.GetConstructor(new[] { typeof(string) });
            if (constructor != null)
            {
                value = constructor.Invoke(new object[] { text });
                return true;
            }

            value = null;
            return false;
        }

        private static object GetParameterDefault(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue &&
                parameter.DefaultValue != null &&
                parameter.DefaultValue != DBNull.Value &&
                parameter.DefaultValue != Missing.Value)
            {
                return parameter.DefaultValue;
            }

            return GetDefaultValue(parameter.ParameterType);
        }

        private static void DrawBowChargeVisuals(SpriteBatch spriteBatch, Item bow, Character character, float chargeSeconds)
        {
            float ratio = MathHelper.Clamp((chargeSeconds - BowChargeVisualStartSeconds) / Math.Max(0.1f, BowSuperChargeSeconds - BowChargeVisualStartSeconds), 0.0f, 1.0f);
            Vector2 pos = PlayerInput.MousePosition + new Vector2(28.0f, 28.0f);
            int width = 132;
            int height = 8;
            Rectangle outer = new Rectangle((int)pos.X - 2, (int)pos.Y - 2, width + 4, height + 4);
            Rectangle background = new Rectangle((int)pos.X, (int)pos.Y, width, height);
            Rectangle fill = new Rectangle((int)pos.X, (int)pos.Y, Math.Max(1, (int)(width * ratio)), height);
            Color fillColor = ratio >= 1.0f ? new Color(255, 65, 215, 240) : new Color(255, 155, 225, 210);

            spriteBatch.Draw(GUI.WhiteTexture, outer, Color.Black * 0.65f);
            spriteBatch.Draw(GUI.WhiteTexture, background, Color.Black * 0.45f);
            spriteBatch.Draw(GUI.WhiteTexture, fill, fillColor);

            Vector2 handScreenPosition;
            if (TryGetChargeVisualScreenPosition(bow, character, out handScreenPosition))
            {
                DrawBowChargeConvergence(spriteBatch, handScreenPosition, ratio, chargeSeconds);
            }
        }

        private static void DrawBowChargeConvergence(SpriteBatch spriteBatch, Vector2 center, float ratio, float chargeSeconds)
        {
            TryLoadPastflowerVisualSprites();

            int particleCount = 16 + (int)(ratio * 78.0f);
            float time = (Environment.TickCount & 0xFFFF) / 1000.0f;
            float alpha = MathHelper.Clamp(0.22f + ratio * 0.7f, 0.22f, 0.92f);
            float spread = 118.0f + ratio * 84.0f;
            float coreScale = 0.22f + ratio * 0.74f;
            Color coreColor = new Color(255, 86, 217, 255) * alpha;

            if (chargeSprite != null && chargeSprite.Texture != null)
            {
                DrawSpriteCentered(spriteBatch, chargeSprite, center, coreScale, coreColor, time * (0.8f + ratio * 1.4f));
            }
            else
            {
                int coreSize = Math.Max(10, (int)Math.Round(22.0f + ratio * 42.0f));
                Rectangle core = new Rectangle(
                    (int)Math.Round(center.X - coreSize * 0.5f),
                    (int)Math.Round(center.Y - coreSize * 0.5f),
                    coreSize,
                    coreSize);
                spriteBatch.Draw(GUI.WhiteTexture, core, coreColor * 0.38f);
            }

            for (int i = 0; i < particleCount; i++)
            {
                float seed = i * 2.399963f;
                float pulse = (float)Math.Sin(time * (1.8f + i * 0.05f) + seed + chargeSeconds * 0.22f);
                float distanceRatio = 0.2f + ((i * 37) % 100) / 100.0f * 0.8f;
                float flow = (time * (0.45f + ratio * 0.85f) + i * 0.071f) % 1.0f;
                float distance = spread * distanceRatio * (1.0f - flow) * (0.9f + pulse * 0.1f);
                float angle = seed + time * (0.65f + ratio * 1.15f);
                Vector2 direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                Vector2 offset = direction * distance;
                Vector2 particlePos = center + offset;
                int size = Math.Max(2, (int)Math.Round(3.0f + ratio * 8.0f + (1.0f - flow) * 2.0f));
                Rectangle rect = new Rectangle((int)(particlePos.X), (int)(particlePos.Y), size, size);
                Color color = (i % 4 == 0 ? new Color(255, 235, 250, 255) : new Color(255, 86, 217, 255)) * (alpha * (0.35f + flow * 0.65f));
                if (i % 3 == 0)
                {
                    GUI.DrawLine(spriteBatch, particlePos, center, color * (0.16f + ratio * 0.28f), 0.0f, 1.0f + ratio * 3.0f);
                }
                spriteBatch.Draw(GUI.WhiteTexture, rect, color);
            }
        }

        private static void DrawPastflowerActiveShotVisuals(SpriteBatch spriteBatch)
        {
            TryLoadPastflowerVisualSprites();
            int now = Environment.TickCount;

            for (int i = BeamVisuals.Count - 1; i >= 0; i--)
            {
                BeamVisual beam = BeamVisuals[i];
                float age = TicksToSeconds(unchecked(now - beam.CreatedTicks));
                if (age >= beam.Duration)
                {
                    BeamVisuals.RemoveAt(i);
                    continue;
                }

                float t = MathHelper.Clamp(age / Math.Max(0.01f, beam.Duration), 0.0f, 1.0f);
                float alpha = 1.0f - t;
                Vector2 beamVector = beam.EndScreen - beam.StartScreen;
                Vector2 pulse = beamVector.LengthSquared() <= 1.0f ? Vector2.UnitX : Vector2.Normalize(beamVector);
                Vector2 shimmer = new Vector2(-pulse.Y, pulse.X) * (float)Math.Sin(age * 72.0f) * (2.0f + alpha * 4.0f);
                Color core = new Color(255, 240, 255, 255) * alpha;
                Color glow = new Color(255, 86, 217, 255) * (alpha * 0.65f);

                GUI.DrawLine(spriteBatch, beam.StartScreen + shimmer, beam.EndScreen + shimmer, glow, 0.0f, 18.0f * alpha + 4.0f);
                GUI.DrawLine(spriteBatch, beam.StartScreen, beam.EndScreen, core, 0.0f, 5.0f * alpha + 2.0f);
            }

            for (int i = ExplosionVisuals.Count - 1; i >= 0; i--)
            {
                ExplosionVisual visual = ExplosionVisuals[i];
                float age = TicksToSeconds(unchecked(now - visual.CreatedTicks));
                if (age >= visual.Duration)
                {
                    ExplosionVisuals.RemoveAt(i);
                    continue;
                }

                Vector2 screenPosition;
                if (!TryWorldToScreen(visual.WorldPosition, out screenPosition))
                {
                    continue;
                }

                float t = MathHelper.Clamp(age / Math.Max(0.01f, visual.Duration), 0.0f, 1.0f);
                float alpha = 1.0f - t;
                float scale = (0.35f + t * 1.05f) * PastflowerExplosionVisualScale;
                Color color = new Color(255, 86, 217, 255) * alpha;
                if (explosionSprite != null && explosionSprite.Texture != null)
                {
                    DrawSpriteCentered(spriteBatch, explosionSprite, screenPosition, scale, color, age * 4.0f);
                }
                else
                {
                    int size = Math.Max(12, (int)Math.Round(48.0f * scale));
                    Rectangle rect = new Rectangle((int)(screenPosition.X - size * 0.5f), (int)(screenPosition.Y - size * 0.5f), size, size);
                    spriteBatch.Draw(GUI.WhiteTexture, rect, color * 0.45f);
                }
            }
        }

        private static void AddPastflowerBeamVisual(Character character, Item bow)
        {
            Vector2 start;
            if (!TryGetChargeVisualScreenPosition(bow, character, out start))
            {
                return;
            }

            Vector2 aim = PlayerInput.MousePosition;
            Vector2 direction = aim - start;
            if (direction.LengthSquared() <= 1.0f)
            {
                direction = Vector2.UnitX;
            }
            else
            {
                direction.Normalize();
            }

            BeamVisuals.Add(new BeamVisual(start, start + direction * 2400.0f, Environment.TickCount, PastflowerBeamVisualSeconds));
        }

        private static void AddPastflowerExplosionVisual(Vector2 worldPosition)
        {
            ExplosionVisuals.Add(new ExplosionVisual(worldPosition, Environment.TickCount, PastflowerExplosionVisualSeconds));
        }

        private static void ScheduleDelayedItemRemoval(Item item, float delaySeconds)
        {
            if (item == null || IsRemovedObject(item))
            {
                return;
            }

            int delayMs = Math.Max(0, (int)Math.Round(Math.Max(0.0f, delaySeconds) * 1000.0f));
            DelayedItemRemovals.Add(new DelayedItemRemoval(item, Environment.TickCount + delayMs));
        }

        private static void UpdateDelayedItemRemovals()
        {
            int now = Environment.TickCount;
            for (int i = DelayedItemRemovals.Count - 1; i >= 0; i--)
            {
                DelayedItemRemoval removal = DelayedItemRemovals[i];
                if (removal.Item == null || IsRemovedObject(removal.Item))
                {
                    DelayedItemRemovals.RemoveAt(i);
                    continue;
                }

                if (unchecked(now - removal.RemoveAtTicks) < 0)
                {
                    continue;
                }

                bool removed = TryRemoveItem(removal.Item);
                DelayedItemRemovals.RemoveAt(i);
                LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super projectile removed=" + removed);
            }
        }

        private static void PlayRandomPastflowerSuperVoice(Character character)
        {
            if (character == null || PastflowerSuperVoiceAfflictions.Length == 0 || PastflowerSuperVoiceFiles.Length == 0)
            {
                return;
            }

            int index = (Environment.TickCount & int.MaxValue) % PastflowerSuperVoiceAfflictions.Length;
            if (index >= PastflowerSuperVoiceFiles.Length)
            {
                index = 0;
            }

            bool playedDirectly = TryPlaySoundFromCharacter(
                character,
                PastflowerSuperVoiceFiles[index],
                PastflowerSuperVoiceRange,
                PastflowerSuperVoiceVolume);
            ApplyAffliction(character, PastflowerSuperVoiceAfflictions[index], 1.0f);
            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super voice played. index=" + index + ", direct=" + playedDirectly);
        }

        private static bool TryPlaySoundFromCharacter(Character character, string relativePath, float range, float volume)
        {
            if (character == null || string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            Vector2 position = character.WorldPosition;
            foreach (string soundPath in GetSoundPathCandidates(relativePath))
            {
                foreach (MethodInfo method in GetSoundPlayMethods())
                {
                    object[] values;
                    if (!TryBuildSoundPlayArguments(method.GetParameters(), soundPath, position, range, volume, out values))
                    {
                        continue;
                    }

                    try
                    {
                        object result = method.Invoke(null, values);
                        if (method.ReturnType == typeof(void) || result != null)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (!directVoicePlayFailed)
            {
                directVoicePlayFailed = true;
                LuaCsLogger.LogMessage("[ElysianRealm] Direct character voice playback unavailable; affliction voice fallback remains active.");
            }
            return false;
        }

        private static IEnumerable<string> GetSoundPathCandidates(string relativePath)
        {
            if (ownerPackage == null)
            {
                LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianGameplayPlugin>(out ownerPackage);
            }

            if (ownerPackage != null && !string.IsNullOrEmpty(ownerPackage.Dir))
            {
                yield return Path.Combine(ownerPackage.Dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }

            yield return relativePath;
            yield return relativePath.Replace('/', Path.DirectorySeparatorChar);
            yield return "%ModDir%/" + relativePath;
        }

        private static IEnumerable<MethodInfo> GetSoundPlayMethods()
        {
            if (soundPlayMethods != null)
            {
                return soundPlayMethods;
            }

            soundPlayMethods = new List<MethodInfo>();
            if (soundPlayLookupFailed)
            {
                return soundPlayMethods;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || !IsSoundPlaybackType(type))
                    {
                        continue;
                    }

                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!IsSoundPlayMethod(method))
                        {
                            continue;
                        }

                        soundPlayMethods.Add(method);
                    }
                }
            }

            if (soundPlayMethods.Count == 0)
            {
                soundPlayLookupFailed = true;
            }

            return soundPlayMethods;
        }

        private static bool IsSoundPlaybackType(Type type)
        {
            string name = type.Name.ToLowerInvariant();
            return name.Contains("soundplayer") || name.Contains("soundmanager");
        }

        private static bool IsSoundPlayMethod(MethodInfo method)
        {
            if (method == null || !method.IsStatic || method.Name.IndexOf("Play", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            bool hasSoundPath = false;
            bool hasPosition = false;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (CanPassSoundPath(parameter.ParameterType))
                {
                    hasSoundPath = true;
                }
                if (parameter.ParameterType == typeof(Vector2))
                {
                    hasPosition = true;
                }
            }

            return hasSoundPath && hasPosition;
        }

        private static bool TryBuildSoundPlayArguments(ParameterInfo[] parameters, string soundPath, Vector2 position, float range, float volume, out object[] values)
        {
            values = new object[parameters.Length];
            bool soundAssigned = false;
            bool positionAssigned = false;
            bool rangeAssigned = false;
            bool volumeAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                string name = parameters[i].Name ?? string.Empty;
                object soundValue;

                if (!soundAssigned && TryCreateSoundPathParameter(type, soundPath, out soundValue))
                {
                    values[i] = soundValue;
                    soundAssigned = true;
                }
                else if (!positionAssigned && type == typeof(Vector2))
                {
                    values[i] = position;
                    positionAssigned = true;
                }
                else if (type == typeof(float))
                {
                    if (!rangeAssigned && name.IndexOf("range", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        values[i] = range;
                        rangeAssigned = true;
                    }
                    else if (!volumeAssigned && (name.IndexOf("volume", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("gain", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        values[i] = volume;
                        volumeAssigned = true;
                    }
                    else if (!volumeAssigned)
                    {
                        values[i] = volume;
                        volumeAssigned = true;
                    }
                    else if (!rangeAssigned)
                    {
                        values[i] = range;
                        rangeAssigned = true;
                    }
                    else
                    {
                        values[i] = GetParameterDefault(parameters[i]);
                    }
                }
                else if (type == typeof(int))
                {
                    values[i] = name.IndexOf("range", StringComparison.OrdinalIgnoreCase) >= 0 ? (int)Math.Round(range) : (int)GetDefaultValue(type);
                }
                else if (type == typeof(bool))
                {
                    values[i] = GetParameterDefault(parameters[i]);
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return soundAssigned && positionAssigned;
        }

        private static bool CanPassSoundPath(Type parameterType)
        {
            if (parameterType == typeof(string) || parameterType == typeof(Identifier))
            {
                return true;
            }

            string typeName = parameterType.FullName ?? parameterType.Name;
            return typeName.IndexOf("ContentPath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   parameterType.GetConstructor(new[] { typeof(string) }) != null;
        }

        private static bool TryCreateSoundPathParameter(Type parameterType, string soundPath, out object value)
        {
            if (parameterType == typeof(string) || parameterType.IsAssignableFrom(typeof(string)))
            {
                value = soundPath;
                return true;
            }

            if (parameterType == typeof(Identifier))
            {
                value = CreateIdentifier(soundPath);
                return true;
            }

            ConstructorInfo constructor = parameterType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (constructor != null)
            {
                try
                {
                    value = constructor.Invoke(new object[] { soundPath });
                    return true;
                }
                catch
                {
                }
            }

            value = null;
            return false;
        }

        private static bool TryGetChargeVisualScreenPosition(Item bow, Character character, out Vector2 screenPosition)
        {
            Vector2 worldPosition;
            if (TryGetWorldPosition(bow, out worldPosition) && TryWorldToScreen(worldPosition, out screenPosition))
            {
                return true;
            }

            if (character != null && TryWorldToScreen(character.WorldPosition, out screenPosition))
            {
                screenPosition += new Vector2(0.0f, -28.0f);
                return true;
            }

            screenPosition = Vector2.Zero;
            return false;
        }

        private static void TryLoadPastflowerVisualSprites()
        {
            TryLoadSprite(ref chargeSprite, ref chargeSpriteLoadFailed, ChargeSpriteRelativePath, "charge");
            TryLoadSprite(ref explosionSprite, ref explosionSpriteLoadFailed, ExplosionSpriteRelativePath, "explosion");
        }

        private static void TryLoadSprite(ref Sprite sprite, ref bool failed, string relativePath, string label)
        {
            if (failed || (sprite != null && sprite.Texture != null))
            {
                return;
            }

            if (ownerPackage == null &&
                !LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianGameplayPlugin>(out ownerPackage))
            {
                failed = true;
                LuaCsLogger.LogError("[ElysianRealm] Cannot resolve package while loading " + label + " sprite.");
                return;
            }

            string fullPath = Path.Combine(ownerPackage.Dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                failed = true;
                LuaCsLogger.LogError("[ElysianRealm] Missing " + label + " sprite: " + fullPath);
                return;
            }

            try
            {
                sprite = new Sprite(fullPath, sourceRectangle: null, origin: new Vector2(0.5f, 0.5f));
                if (sprite.Texture == null)
                {
                    RemoveSprite(ref sprite);
                    failed = true;
                    LuaCsLogger.LogError("[ElysianRealm] Failed to load " + label + " sprite: " + fullPath);
                }
            }
            catch (Exception ex)
            {
                RemoveSprite(ref sprite);
                failed = true;
                LuaCsLogger.HandleException(ex, LuaCsMessageOrigin.LuaMod);
            }
        }

        private static void DrawSpriteCentered(SpriteBatch spriteBatch, Sprite sprite, Vector2 center, float scale, Color color, float rotation)
        {
            if (sprite == null || sprite.Texture == null)
            {
                return;
            }

            Rectangle sourceRect = sprite.SourceRect;
            spriteBatch.Draw(
                sprite.Texture,
                center,
                sourceRect,
                color,
                rotation,
                new Vector2(sourceRect.Width * 0.5f, sourceRect.Height * 0.5f),
                scale,
                SpriteEffects.None,
                0.0f);
        }

        private static void RemoveSprite(ref Sprite sprite)
        {
            if (sprite != null)
            {
                sprite.Remove();
                sprite = null;
            }
        }

        private static float TicksToSeconds(int ticks)
        {
            return Math.Max(0, ticks) / 1000.0f;
        }

        private static bool TryWorldToScreen(Vector2 worldPosition, out Vector2 screenPosition)
        {
            try
            {
                if (Screen.Selected == null || Screen.Selected.Cam == null)
                {
                    screenPosition = Vector2.Zero;
                    return false;
                }

                screenPosition = Screen.Selected.Cam.WorldToScreen(worldPosition);
                return true;
            }
            catch
            {
                screenPosition = Vector2.Zero;
                return false;
            }
        }

        private static ChargeState GetChargeState(Character character)
        {
            ChargeState state;
            if (!ChargeStates.TryGetValue(character, out state))
            {
                state = new ChargeState();
                ChargeStates[character] = state;
            }

            return state;
        }

        private static void ResetBowChargeState(ChargeState state)
        {
            if (state == null)
            {
                return;
            }

            state.ChargeSeconds = 0.0f;
            state.WasReadyLogged = false;
            state.WasFullyChargedLogged = false;
            state.WasChargeSoundPlayed = false;
        }

        private static bool HasSufficientArrowAmmoForCharge(Character character, Item bow)
        {
            return CountArrowAmount(FindArrowAmmo(character, bow)) > 0;
        }

        private static WeaponOverride CreateNormalShotOverride(object rangedWeapon)
        {
            WeaponOverride weaponOverride = new WeaponOverride(rangedWeapon);
            weaponOverride.TryOverrideInt("ProjectileCount", 1);
            return weaponOverride;
        }

        private static WeaponOverride CreateSuperShotOverride(object rangedWeapon, Item projectileItem, int arrowCount)
        {
            WeaponOverride weaponOverride = CreateNormalShotOverride(rangedWeapon);
            float damageMultiplier = Math.Max(1.0f, arrowCount);
            TryOverrideFloatMultiplier(weaponOverride, rangedWeapon, "WeaponDamageModifier", damageMultiplier);
            TryOverrideFloatMultiplier(weaponOverride, rangedWeapon, "DamageMultiplier", damageMultiplier);

            float launchImpulse;
            if (TryGetFloatMember(rangedWeapon, "LaunchImpulse", out launchImpulse))
            {
                float projectileImpulse = GetProjectileLaunchImpulse(projectileItem);
                float normalImpulse = Math.Max(1.0f, launchImpulse + projectileImpulse);
                float targetWeaponImpulse = Math.Max(0.0f, normalImpulse * BowSuperImpulseMultiplier - projectileImpulse);
                weaponOverride.TryOverrideFloat("LaunchImpulse", targetWeaponImpulse);
            }

            return weaponOverride;
        }

        private static void TryOverrideFloatMultiplier(WeaponOverride weaponOverride, object target, string memberName, float multiplier)
        {
            float original;
            if (TryGetFloatMember(target, memberName, out original))
            {
                weaponOverride.TryOverrideFloat(memberName, original * multiplier);
            }
        }

        private static void QueuePendingSuperShot(Character attacker, Item loadedArrow, int arrowCount)
        {
            PruneExpiredPendingSuperShots();
            PendingSuperShots.Add(new PendingSuperShot(attacker, loadedArrow, arrowCount, Environment.TickCount));
        }

        private static bool TryTakeSuperProjectileData(Item projectileItem, out SuperShotData data)
        {
            if (SuperProjectiles.TryGetValue(projectileItem, out data))
            {
                SuperProjectiles.Remove(projectileItem);
                RemovePendingSuperShotForProjectile(projectileItem, data.Attacker);
                return true;
            }

            return TryClaimPendingSuperShot(projectileItem, null, out data);
        }

        private static bool TryClaimPendingSuperShot(Item projectileItem, Character shooter, out SuperShotData data)
        {
            data = null;
            if (!IsPastflowerArrow(projectileItem))
            {
                return false;
            }

            PruneExpiredPendingSuperShots();

            int bestIndex = -1;
            for (int i = PendingSuperShots.Count - 1; i >= 0; i--)
            {
                PendingSuperShot pending = PendingSuperShots[i];
                if (shooter != null && pending.Attacker != null && !ReferenceEquals(pending.Attacker, shooter))
                {
                    continue;
                }

                if (pending.LoadedArrow != null && ReferenceEquals(pending.LoadedArrow, projectileItem))
                {
                    bestIndex = i;
                    break;
                }

                if (bestIndex < 0)
                {
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            PendingSuperShot selected = PendingSuperShots[bestIndex];
            PendingSuperShots.RemoveAt(bestIndex);
            if (selected.LoadedArrow != null && !ReferenceEquals(selected.LoadedArrow, projectileItem))
            {
                SuperProjectiles.Remove(selected.LoadedArrow);
            }

            data = new SuperShotData(selected.Attacker, selected.ArrowCount);
            return true;
        }

        private static void RemovePendingSuperShotForProjectile(Item projectileItem, Character attacker)
        {
            for (int i = PendingSuperShots.Count - 1; i >= 0; i--)
            {
                PendingSuperShot pending = PendingSuperShots[i];
                if ((projectileItem != null && pending.LoadedArrow != null && ReferenceEquals(pending.LoadedArrow, projectileItem)) ||
                    (attacker != null && pending.Attacker != null && ReferenceEquals(pending.Attacker, attacker)))
                {
                    PendingSuperShots.RemoveAt(i);
                }
            }
        }

        private static void PruneExpiredPendingSuperShots()
        {
            int now = Environment.TickCount;
            for (int i = PendingSuperShots.Count - 1; i >= 0; i--)
            {
                if (unchecked(now - PendingSuperShots[i].CreatedTicks) > PendingSuperShotMilliseconds)
                {
                    PendingSuperShots.RemoveAt(i);
                }
            }
        }

        private static bool TryReplaceLoadedArrowWithSuperProjectile(Character character, Item bow, Item loadedArrow, out Item superArrow)
        {
            superArrow = null;
            if (bow == null || loadedArrow == null)
            {
                return false;
            }

            object bowInventory = GetInventory(bow);
            if (bowInventory == null)
            {
                return false;
            }

            Vector2 position;
            if (!TryGetWorldPosition(loadedArrow, out position) &&
                !TryGetWorldPosition(bow, out position))
            {
                position = character == null ? Vector2.Zero : character.WorldPosition;
            }

            object submarine = GetMemberValue(loadedArrow, "Submarine") ?? GetMemberValue(bow, "Submarine");
            if (!TryCreateItem(SuperArrowIdentifier, position, submarine, out superArrow))
            {
                return false;
            }

            if (!TryRemoveItem(loadedArrow))
            {
                TryRemoveItem(superArrow);
                superArrow = null;
                return false;
            }

            if (TryPutItemIntoInventory(bowInventory, superArrow, character))
            {
                return true;
            }

            TryRemoveItem(superArrow);
            superArrow = null;
            return false;
        }

        private static bool TryCreateItem(string identifier, Vector2 position, object submarine, out Item item)
        {
            item = null;
            object prefab = GetItemPrefab(identifier);
            if (prefab == null)
            {
                LogOnce("item_prefab_" + identifier, "[ElysianRealm] Item prefab not found: " + identifier);
                return false;
            }

            foreach (ConstructorInfo constructor in typeof(Item).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(c => c.GetParameters().Length))
            {
                if (constructor.GetParameters().Any(p => p.ParameterType == typeof(Rectangle)))
                {
                    continue;
                }

                object[] values;
                if (!TryBuildItemConstructorArguments(constructor.GetParameters(), prefab, position, submarine, identifier, out values))
                {
                    continue;
                }

                try
                {
                    item = constructor.Invoke(values) as Item;
                    if (item != null)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            LogOnce("item_create_failed_" + identifier, "[ElysianRealm] Could not create item: " + identifier);
            return false;
        }

        private static bool TryBuildItemConstructorArguments(ParameterInfo[] parameters, object prefab, Vector2 position, object submarine, string identifier, out object[] values)
        {
            values = new object[parameters.Length];
            bool prefabAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (!prefabAssigned && prefab != null && type.IsInstanceOfType(prefab))
                {
                    values[i] = prefab;
                    prefabAssigned = true;
                }
                else if (type == typeof(Vector2))
                {
                    values[i] = position;
                }
                else if (submarine != null && type.IsInstanceOfType(submarine))
                {
                    values[i] = submarine;
                }
                else if ((type == typeof(string) || string.Equals(type.Name, "Identifier", StringComparison.Ordinal)) &&
                         CanPassIdentifier(type, CreateIdentifier(identifier)))
                {
                    values[i] = ConvertIdentifierForParameter(type, CreateIdentifier(identifier));
                }
                else if (type == typeof(bool))
                {
                    values[i] = GetParameterDefault(parameters[i]);
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return prefabAssigned;
        }

        private static object GetItemPrefab(string identifier)
        {
            object prefabs = GetStaticMemberValue(typeof(ItemPrefab), "Prefabs");
            if (prefabs == null)
            {
                return null;
            }

            object id = CreateIdentifier(identifier);
            foreach (MethodInfo method in prefabs.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "get_Item", StringComparison.Ordinal) &&
                    !string.Equals(method.Name, "Get", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !CanPassIdentifier(parameters[0].ParameterType, id))
                {
                    continue;
                }

                try
                {
                    return method.Invoke(prefabs, new[] { ConvertIdentifierForParameter(parameters[0].ParameterType, id) });
                }
                catch
                {
                }
            }

            return null;
        }

        private static List<Item> FindArrowAmmo(Character character, Item bow)
        {
            List<Item> result = new List<Item>();
            HashSet<Item> seen = new HashSet<Item>();

            foreach (Item item in EnumerateInventoryItems(GetInventory(bow)))
            {
                if (HasIdentifier(item, ArrowIdentifier) && seen.Add(item))
                {
                    result.Add(item);
                }
            }

            foreach (Item item in EnumerateInventoryItems(GetInventory(character)))
            {
                if (HasIdentifier(item, ArrowIdentifier) && seen.Add(item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        private static Item FindLoadedArrow(Item bow)
        {
            foreach (Item item in EnumerateInventoryItems(GetInventory(bow)))
            {
                if (HasIdentifier(item, ArrowIdentifier))
                {
                    return item;
                }
            }

            return null;
        }

        private static int CountArrowAmount(IEnumerable<Item> ammo)
        {
            int amount = 0;
            foreach (Item item in ammo)
            {
                if (item != null)
                {
                    amount += Math.Max(1, GetItemAmount(item));
                }
            }

            return amount;
        }

        private static int GetItemAmount(Item item)
        {
            foreach (string memberName in new[] { "Amount", "Quantity" })
            {
                object value = GetMemberValue(item, memberName);
                if (value is int)
                {
                    return Math.Max(1, (int)value);
                }
                if (value is float)
                {
                    return Math.Max(1, (int)(float)value);
                }
                if (value is double)
                {
                    return Math.Max(1, (int)(double)value);
                }
            }

            return 1;
        }

        private static float GetProjectileLaunchImpulse(Item projectileItem)
        {
            IEnumerable components = GetMemberValue(projectileItem, "Components") as IEnumerable;
            if (components == null)
            {
                return 0.0f;
            }

            foreach (object component in components)
            {
                if (component == null ||
                    component.GetType().Name.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                float launchImpulse;
                if (TryGetFloatMember(component, "LaunchImpulse", out launchImpulse))
                {
                    return launchImpulse;
                }
            }

            return 0.0f;
        }

        private static bool HasLoadedArrow(Item bow)
        {
            foreach (Item item in EnumerateInventoryItems(GetInventory(bow)))
            {
                if (HasIdentifier(item, ArrowIdentifier))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnsureBowLoadedFromInventory(Character character, Item bow)
        {
            if (HasLoadedArrow(bow))
            {
                return true;
            }

            Item arrow = FindInventoryArrow(character);
            if (arrow == null)
            {
                return false;
            }

            object bowInventory = GetInventory(bow);
            if (bowInventory == null)
            {
                LogOnce("bow_hidden_container_missing", "[ElysianRealm] Pastflower hidden chamber is missing; cannot autoload lovespears.");
                return false;
            }

            if (TryPutItemIntoInventory(bowInventory, arrow, character))
            {
                return true;
            }

            LogOnce("bow_autoload_failed", "[ElysianRealm] Failed to autoload lovespears from character inventory.");
            return false;
        }

        private static Item FindInventoryArrow(Character character)
        {
            foreach (Item item in EnumerateInventoryItems(GetInventory(character)))
            {
                if (HasIdentifier(item, ArrowIdentifier))
                {
                    return item;
                }
            }

            return null;
        }

        private static bool TryPutItemIntoInventory(object inventory, Item item, Character user)
        {
            if (inventory == null || item == null)
            {
                return false;
            }

            foreach (MethodInfo method in inventory.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "TryPutItem", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || !parameters[0].ParameterType.IsInstanceOfType(item))
                {
                    continue;
                }

                object[] values = BuildTryPutItemArguments(parameters, item, user);
                try
                {
                    object result = method.Invoke(inventory, values);
                    if (result is bool && (bool)result)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static object[] BuildTryPutItemArguments(ParameterInfo[] parameters, Item item, Character user)
        {
            object[] values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                string name = parameters[i].Name ?? string.Empty;

                if (type.IsInstanceOfType(item))
                {
                    values[i] = item;
                }
                else if (user != null && type.IsInstanceOfType(user))
                {
                    values[i] = user;
                }
                else if (type == typeof(bool))
                {
                    values[i] = !name.Equals("ignoreCondition", StringComparison.OrdinalIgnoreCase);
                }
                else if (type == typeof(int))
                {
                    values[i] = -1;
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return values;
        }

        private static int ConsumeExtraVolleyAmmo(List<Item> ammo, int extraToConsume)
        {
            int consumed = 0;
            for (int i = 1; i < ammo.Count && consumed < extraToConsume; i++)
            {
                Item item = ammo[i];
                if (item == null || RemovedVolleyAmmo.Contains(item))
                {
                    continue;
                }

                if (TryRemoveItem(item))
                {
                    RemovedVolleyAmmo.Add(item);
                    consumed += Math.Max(1, GetItemAmount(item));
                }
            }

            return consumed;
        }

        private static Item FindHeldItem(Character character, string identifier)
        {
            foreach (Item item in EnumerateHeldItems(character))
            {
                if (HasIdentifier(item, identifier))
                {
                    return item;
                }
            }

            return null;
        }

        private static bool IsHoldingOnlyBow(Character character, Item bow)
        {
            if (character == null || bow == null)
            {
                return false;
            }

            bool foundBow = false;
            foreach (Item item in EnumerateHeldItems(character))
            {
                if (item == null)
                {
                    continue;
                }

                if (ReferenceEquals(item, bow) || HasIdentifier(item, BowIdentifier))
                {
                    foundBow = true;
                    continue;
                }

                return false;
            }

            return foundBow;
        }

        private static IEnumerable<Item> EnumerateHeldItems(Character character)
        {
            object heldItems = GetMemberValue(character, "HeldItems");
            foreach (Item item in EnumerateItems(heldItems))
            {
                yield return item;
            }
        }

        private static object GetInventory(object owner)
        {
            if (owner == null)
            {
                return null;
            }

            object inventory = GetMemberValue(owner, "Inventory");
            if (inventory != null)
            {
                return inventory;
            }

            inventory = GetMemberValue(owner, "OwnInventory");
            if (inventory != null)
            {
                return inventory;
            }

            IEnumerable components = GetMemberValue(owner, "Components") as IEnumerable;
            if (components != null)
            {
                foreach (object component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    if (component.GetType().Name.IndexOf("ItemContainer", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        inventory = GetMemberValue(component, "Inventory");
                        if (inventory != null)
                        {
                            return inventory;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Item> EnumerateInventoryItems(object inventory)
        {
            foreach (string memberName in new[] { "AllItems", "Items" })
            {
                foreach (Item item in EnumerateItems(GetMemberValue(inventory, memberName)))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<Item> EnumerateItems(object value)
        {
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (object entry in enumerable)
            {
                Item item = entry as Item;
                if (item != null)
                {
                    yield return item;
                    continue;
                }

                IEnumerable nested = entry as IEnumerable;
                if (nested == null || entry is string)
                {
                    continue;
                }

                foreach (object nestedEntry in nested)
                {
                    Item nestedItem = nestedEntry as Item;
                    if (nestedItem != null)
                    {
                        yield return nestedItem;
                    }
                }
            }
        }

        private static bool TryRemoveItem(Item item)
        {
            MethodInfo removeMethod = item.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (removeMethod == null)
            {
                return false;
            }

            try
            {
                removeMethod.Invoke(item, null);
                return true;
            }
            catch (Exception ex)
            {
                LogOnce("remove_item_failed", "[ElysianRealm] Failed to remove item: " + ex.GetType().Name);
                return false;
            }
        }

        private static void ApplyPastflowerSuperDirectDamage(Character attacker, Vector2 position, int arrowCount)
        {
            Character target = FindClosestCharacter(position, attacker, PastflowerDirectHitRadius);
            if (target == null)
            {
                return;
            }

            float multiplier = Math.Max(1.0f, arrowCount);
            int applied = 0;
            float fallbackInternalDamage = 0.0f;
            float fallbackBleedingDamage = 0.0f;
            if (ApplyAffliction(target, "internaldamage", LoveSpearInternalDamage * multiplier))
            {
                applied++;
            }
            else
            {
                fallbackInternalDamage += LoveSpearInternalDamage * multiplier;
            }

            if (ApplyAffliction(target, "lacerations", LoveSpearLacerationDamage * multiplier))
            {
                applied++;
            }
            else
            {
                fallbackInternalDamage += LoveSpearLacerationDamage * multiplier;
            }

            if (ApplyAffliction(target, "bleeding", LoveSpearBleedingDamage * multiplier))
            {
                applied++;
            }
            else
            {
                fallbackBleedingDamage += LoveSpearBleedingDamage * multiplier;
            }

            if (ApplyAffliction(target, "stun", LoveSpearStunDamage * multiplier))
            {
                applied++;
            }

            if ((fallbackInternalDamage > 0.0f || fallbackBleedingDamage > 0.0f) &&
                TryAddCharacterDamage(target, fallbackInternalDamage, fallbackBleedingDamage, 0.0f))
            {
                applied++;
                LogOnce("pastflower_direct_fallback_damage", "[ElysianRealm] Pastflower direct fallback damage applied.");
            }

            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super direct damage applied. arrows=" + arrowCount + ", afflictions=" + applied);
        }

        private static Character FindClosestCharacter(Vector2 position, Character attacker, float range)
        {
            Character closest = null;
            float bestDistance = range * range;

            foreach (Character target in Character.CharacterList)
            {
                if (!IsUsableCharacter(target) || ReferenceEquals(target, attacker))
                {
                    continue;
                }

                float distance = GetClosestCharacterDistanceSquared(target, position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    closest = target;
                }
            }

            return closest;
        }

        private static float GetClosestCharacterDistanceSquared(Character character, Vector2 position)
        {
            float best = Vector2.DistanceSquared(character.WorldPosition, position);
            object limbs = character.AnimController == null ? null : GetMemberValue(character.AnimController, "Limbs");
            IEnumerable enumerable = limbs as IEnumerable;
            if (enumerable == null)
            {
                return best;
            }

            foreach (object limb in enumerable)
            {
                Vector2 limbPosition;
                if (TryGetWorldPosition(limb, out limbPosition))
                {
                    best = Math.Min(best, Vector2.DistanceSquared(limbPosition, position));
                }
            }

            return best;
        }

        private static void ApplyPastflowerExplosion(Character attacker, Vector2 position)
        {
            int characterHits = 0;
            int itemHits = 0;
            int structureHits = 0;
            float explosionRangeSquared = PastflowerExplosionRange * PastflowerExplosionRange;

            foreach (Character target in Character.CharacterList)
            {
                if (!IsUsableCharacter(target) || ReferenceEquals(target, attacker))
                {
                    continue;
                }

                if (GetClosestCharacterDistanceSquared(target, position) > explosionRangeSquared)
                {
                    continue;
                }

                if (ApplyPastflowerExplosionDamage(target))
                {
                    characterHits++;
                }
            }

            Type structureType = FindTypeByName("Structure");
            if (structureType != null)
            {
                foreach (object structure in EnumerateStaticObjects(structureType, new[] { "WallList", "StructureList", "Structures", "Loaded" }))
                {
                    Vector2 structurePosition;
                    if (!TryGetWorldPosition(structure, out structurePosition) ||
                        Vector2.Distance(structurePosition, position) > PastflowerExplosionRange)
                    {
                        continue;
                    }

                    if (TryApplyObjectDamage(structure, position, PastflowerExplosionStructureDamage))
                    {
                        structureHits++;
                    }
                }
            }

            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower explosion hits. characters=" + characterHits + ", items=" + itemHits + ", structures=" + structureHits);
        }

        private static bool ApplyPastflowerExplosionDamage(Character target)
        {
            if (ApplyAffliction(target, "internaldamage", PastflowerExplosionInternalDamage))
            {
                return true;
            }

            if (TryAddCharacterDamage(target, PastflowerExplosionInternalDamage, 0.0f, 0.0f))
            {
                LogOnce("pastflower_explosion_fallback_damage", "[ElysianRealm] Pastflower explosion fallback internal damage applied.");
                return true;
            }

            return false;
        }

        private static bool TryAddCharacterDamage(Character character, float internalDamage, float bleedingDamage, float burnDamage)
        {
            if (character == null || character.CharacterHealth == null ||
                (internalDamage <= 0.0f && bleedingDamage <= 0.0f && burnDamage <= 0.0f))
            {
                return false;
            }

            float internalAmount = Math.Max(0.0f, GetAfflictionStrength(character, "internaldamage")) + Math.Max(0.0f, internalDamage);
            float bleedingAmount = Math.Max(0.0f, GetAfflictionStrength(character, "bleeding")) + Math.Max(0.0f, bleedingDamage);
            float burnAmount = Math.Max(0.0f, GetAfflictionStrength(character, "burn")) + Math.Max(0.0f, burnDamage);

            foreach (MethodInfo method in character.CharacterHealth.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "SetAllDamage", StringComparison.Ordinal))
                {
                    continue;
                }

                object[] values;
                if (!TryBuildSetAllDamageArguments(method.GetParameters(), internalAmount, bleedingAmount, burnAmount, out values))
                {
                    continue;
                }

                try
                {
                    method.Invoke(character.CharacterHealth, values);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryBuildSetAllDamageArguments(ParameterInfo[] parameters, float internalDamage, float bleedingDamage, float burnDamage, out object[] values)
        {
            values = new object[parameters.Length];
            int floatIndex = 0;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                string name = (parameters[i].Name ?? string.Empty).ToLowerInvariant();

                if (type == typeof(float))
                {
                    if (name.Contains("bleed") || floatIndex == 1)
                    {
                        values[i] = bleedingDamage;
                    }
                    else if (name.Contains("burn") || floatIndex == 2)
                    {
                        values[i] = burnDamage;
                    }
                    else
                    {
                        values[i] = internalDamage;
                    }

                    floatIndex++;
                }
                else if (type == typeof(double))
                {
                    values[i] = (double)internalDamage;
                }
                else if (type == typeof(int))
                {
                    values[i] = (int)Math.Round(internalDamage);
                }
                else if (type == typeof(bool))
                {
                    values[i] = GetParameterDefault(parameters[i]);
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return floatIndex > 0;
        }

        private static IEnumerable<object> EnumerateStaticObjects(Type type, IEnumerable<string> memberNames)
        {
            foreach (string memberName in memberNames)
            {
                foreach (object entry in EnumerateObjects(GetStaticMemberValue(type, memberName)))
                {
                    yield return entry;
                }
            }
        }

        private static IEnumerable<object> EnumerateObjects(object value)
        {
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (object entry in enumerable)
            {
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }
        }

        private static bool TryApplyObjectDamage(object target, Vector2 position, float damage)
        {
            if (target == null || damage <= 0.0f)
            {
                return false;
            }

            float condition;
            if (TryGetFloatMember(target, "Condition", out condition) &&
                TrySetMemberValue(target, "Condition", Math.Max(0.0f, condition - damage)))
            {
                return true;
            }

            foreach (string methodName in new[] { "AddDamage", "Damage", "ApplyDamage" })
            {
                foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    object[] values = BuildDamageMethodArguments(method.GetParameters(), position, damage);
                    try
                    {
                        method.Invoke(target, values);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static object[] BuildDamageMethodArguments(ParameterInfo[] parameters, Vector2 position, float damage)
        {
            object[] values = new object[parameters.Length];
            bool damageAssigned = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == typeof(Vector2))
                {
                    values[i] = position;
                }
                else if (!damageAssigned && type == typeof(float))
                {
                    values[i] = damage;
                    damageAssigned = true;
                }
                else if (type == typeof(bool))
                {
                    values[i] = true;
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return values;
        }

        private static bool ApplyAffliction(Character character, string identifier, float strength)
        {
            if (character == null || character.CharacterHealth == null)
            {
                return false;
            }

            object prefab = GetAfflictionPrefab(identifier);
            if (prefab == null)
            {
                LogOnce("affliction_prefab_" + identifier, "[ElysianRealm] Affliction prefab not found: " + identifier);
                return false;
            }

            object affliction;
            if (!TryInstantiateAffliction(prefab, strength, character, out affliction))
            {
                LogOnce("affliction_instantiate_" + identifier, "[ElysianRealm] Affliction instantiate method not found: " + identifier);
                return false;
            }

            object limb = character.AnimController == null ? null : character.AnimController.MainLimb;
            return InvokeHealthMethod(character.CharacterHealth, "ApplyAffliction", limb, affliction);
        }

        private static bool TryInstantiateAffliction(object prefab, float strength, Character source, out object affliction)
        {
            affliction = null;
            if (prefab == null)
            {
                return false;
            }

            foreach (MethodInfo method in prefab.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "Instantiate", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object[] values;
                if (!TryBuildAfflictionInstantiateArguments(parameters, strength, source, out values))
                {
                    continue;
                }

                try
                {
                    affliction = method.Invoke(prefab, values);
                    if (affliction != null)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryBuildAfflictionInstantiateArguments(ParameterInfo[] parameters, float strength, Character source, out object[] values)
        {
            values = new object[parameters.Length];
            bool strengthAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                string name = parameters[i].Name ?? string.Empty;

                if (!strengthAssigned && (type == typeof(float) || type == typeof(double) || type == typeof(int)))
                {
                    strengthAssigned = true;
                    if (type == typeof(float))
                    {
                        values[i] = strength;
                    }
                    else if (type == typeof(double))
                    {
                        values[i] = (double)strength;
                    }
                    else
                    {
                        values[i] = (int)Math.Round(strength);
                    }
                }
                else if (source != null && type.IsInstanceOfType(source))
                {
                    values[i] = source;
                }
                else if (type == typeof(bool))
                {
                    values[i] = GetParameterDefault(parameters[i]);
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return strengthAssigned || parameters.Length == 0;
        }

        private static float GetAfflictionStrength(Character character, string identifier)
        {
            if (character == null || character.CharacterHealth == null)
            {
                return 0.0f;
            }

            object id = CreateIdentifier(identifier);
            float byIdentifier = InvokeGetAfflictionStrengthByIdentifier(character.CharacterHealth, id);
            if (byIdentifier > 0.0f)
            {
                return byIdentifier;
            }

            foreach (MethodInfo method in character.CharacterHealth.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "GetAfflictionStrength", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || !CanPassIdentifier(parameters[0].ParameterType, id))
                {
                    continue;
                }

                object[] values = BuildMethodArguments(parameters, id, true);
                try
                {
                    object result = method.Invoke(character.CharacterHealth, values);
                    if (result is float)
                    {
                        return (float)result;
                    }
                    if (result is double)
                    {
                        return (float)(double)result;
                    }
                }
                catch
                {
                }
            }

            return 0.0f;
        }

        private static float InvokeGetAfflictionStrengthByIdentifier(object health, object identifier)
        {
            if (health == null || identifier == null)
            {
                return 0.0f;
            }

            foreach (MethodInfo method in health.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "GetAfflictionStrengthByIdentifier", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || !CanPassIdentifier(parameters[0].ParameterType, identifier))
                {
                    continue;
                }

                object[] values = BuildMethodArguments(parameters, identifier, true);
                try
                {
                    object result = method.Invoke(health, values);
                    if (result is float)
                    {
                        return (float)result;
                    }
                    if (result is double)
                    {
                        return (float)(double)result;
                    }
                }
                catch
                {
                }
            }

            return 0.0f;
        }

        private static bool ReduceAffliction(Character character, string identifier, float strength)
        {
            if (character == null || character.CharacterHealth == null)
            {
                return false;
            }

            object id = CreateIdentifier(identifier);
            foreach (string methodName in new[] { "ReduceAfflictionOnAllLimbs", "ReduceAffliction" })
            {
                foreach (MethodInfo method in character.CharacterHealth.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length < 2 || !CanPassIdentifier(parameters[0].ParameterType, id))
                    {
                        continue;
                    }

                    object[] values = BuildMethodArguments(parameters, id, strength);
                    try
                    {
                        method.Invoke(character.CharacterHealth, values);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            LogOnce("reduce_affliction_failed", "[ElysianRealm] Could not find a compatible ReduceAffliction method.");
            return false;
        }

        private static object GetAfflictionPrefab(string identifier)
        {
            object prefabs = GetStaticMemberValue(typeof(AfflictionPrefab), "Prefabs");
            if (prefabs == null)
            {
                return null;
            }

            object id = CreateIdentifier(identifier);
            foreach (MethodInfo method in prefabs.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "get_Item", StringComparison.Ordinal) &&
                    !string.Equals(method.Name, "Get", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !CanPassIdentifier(parameters[0].ParameterType, id))
                {
                    continue;
                }

                try
                {
                    return method.Invoke(prefabs, new[] { ConvertIdentifierForParameter(parameters[0].ParameterType, id) });
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool InvokeHealthMethod(object health, string methodName, object limb, object affliction)
        {
            foreach (MethodInfo method in health.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2)
                {
                    continue;
                }

                object[] values;
                if (!TryBuildApplyAfflictionArguments(parameters, limb, affliction, out values))
                {
                    continue;
                }

                try
                {
                    method.Invoke(health, values);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryBuildApplyAfflictionArguments(ParameterInfo[] parameters, object limb, object affliction, out object[] values)
        {
            values = new object[parameters.Length];
            bool limbAssigned = false;
            bool afflictionAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (!afflictionAssigned && affliction != null && type.IsInstanceOfType(affliction))
                {
                    values[i] = affliction;
                    afflictionAssigned = true;
                }
                else if (!limbAssigned && limb != null && type.IsInstanceOfType(limb))
                {
                    values[i] = limb;
                    limbAssigned = true;
                }
                else if (type == typeof(bool))
                {
                    values[i] = GetParameterDefault(parameters[i]);
                }
                else
                {
                    values[i] = GetDefaultValue(type);
                }
            }

            return afflictionAssigned;
        }

        private static bool TryForceAiTarget(Character target, Character source)
        {
            object aiController = GetMemberValue(target, "AIController");
            if (aiController == null)
            {
                return false;
            }

            foreach (string methodName in new[] { "SetTarget", "SelectTarget", "AddTarget", "ForceTarget" })
            {
                foreach (MethodInfo method in aiController.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0 || !parameters[0].ParameterType.IsInstanceOfType(source))
                    {
                        continue;
                    }

                    object[] values = BuildMethodArguments(parameters, source, null);
                    try
                    {
                        method.Invoke(aiController, values);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool IsFriendly(Character source, Character target)
        {
            if (source == null || target == null)
            {
                return false;
            }

            return source.TeamID == target.TeamID;
        }

        private static bool IsUsableCharacter(Character character)
        {
            return character != null && !character.Removed && !character.IsDead;
        }

        private static bool IsRemovedObject(object instance)
        {
            foreach (string memberName in new[] { "Removed", "IsRemoved" })
            {
                object value = GetMemberValue(instance, memberName);
                if (value is bool)
                {
                    return (bool)value;
                }
            }

            return false;
        }

        private static Character GetCharacterArg(Dictionary<string, object> args)
        {
            if (args == null)
            {
                return null;
            }

            foreach (string key in new[] { "character", "user" })
            {
                object value;
                if (args.TryGetValue(key, out value))
                {
                    Character character = value as Character;
                    if (character != null)
                    {
                        return character;
                    }
                }
            }

            return GetArgByType<Character>(args, null);
        }

        private static T GetArgByType<T>(Dictionary<string, object> args, string preferredKey) where T : class
        {
            if (args == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(preferredKey))
            {
                object preferredValue;
                if (args.TryGetValue(preferredKey, out preferredValue))
                {
                    T preferred = preferredValue as T;
                    if (preferred != null)
                    {
                        return preferred;
                    }
                }
            }

            foreach (KeyValuePair<string, object> pair in args)
            {
                T value = pair.Value as T;
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static float GetFloatArg(Dictionary<string, object> args, string key, float fallback)
        {
            if (args == null)
            {
                return fallback;
            }

            object value;
            if (!args.TryGetValue(key, out value))
            {
                return fallback;
            }

            if (value is float)
            {
                return (float)value;
            }
            if (value is double)
            {
                return (float)(double)value;
            }
            if (value is int)
            {
                return (int)value;
            }

            return fallback;
        }

        private static bool IsInputDown(Character character, string inputTypeName)
        {
            return InvokeInputMethod(character, characterIsKeyDownMethod, inputTypeName);
        }

        private static bool IsInputHit(Character character, string inputTypeName)
        {
            return InvokeInputMethod(character, characterIsKeyHitMethod, inputTypeName);
        }

        private static bool InvokeInputMethod(Character character, MethodInfo method, string inputTypeName)
        {
            if (character == null || method == null)
            {
                return false;
            }

            object inputType = ParseInputType(inputTypeName);
            if (inputType == null)
            {
                return false;
            }

            try
            {
                object result = method.Invoke(character, new[] { inputType });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static void CacheInputMethods()
        {
            characterIsKeyDownMethod = typeof(Character).GetMethod("IsKeyDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(InputType) }, null);
            characterIsKeyHitMethod = typeof(Character).GetMethod("IsKeyHit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(InputType) }, null);
        }

        private static object ParseInputType(string name)
        {
            try
            {
                return Enum.Parse(typeof(InputType), name, true);
            }
            catch
            {
                LogOnce("input_missing_" + name, "[ElysianRealm] InputType not found: " + name);
                return null;
            }
        }

        private static Item GetComponentItem(object component)
        {
            return GetMemberValue(component, "Item") as Item;
        }

        private static Type FindRangedWeaponType()
        {
            if (rangedWeaponType == null)
            {
                rangedWeaponType = FindTypeByName("RangedWeapon");
            }

            return rangedWeaponType;
        }

        private static bool IsRangedWeapon(object instance)
        {
            Type type = FindRangedWeaponType();
            return instance != null && type != null && type.IsInstanceOfType(instance);
        }

        private static bool HasIdentifier(Item item, string identifier)
        {
            if (item == null)
            {
                return false;
            }

            if (item.Prefab != null && string.Equals(item.Prefab.Identifier.ToString(), identifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            object itemIdentifier = GetMemberValue(item, "Identifier");
            return itemIdentifier != null &&
                   string.Equals(itemIdentifier.ToString(), identifier, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPastflowerArrow(Item item)
        {
            return HasIdentifier(item, ArrowIdentifier) || HasIdentifier(item, SuperArrowIdentifier);
        }

        private static Type FindTypeByName(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static bool TryGetProjectileImpactWorldPosition(Dictionary<string, object> args, Item projectileItem, out Vector2 position)
        {
            if (args == null || args.Count == 0)
            {
                position = Vector2.Zero;
                return false;
            }

            List<Vector2> candidates = new List<Vector2>();
            foreach (string key in new[]
            {
                "worldPosition",
                "worldPos",
                "hitPosition",
                "hitPos",
                "collisionPosition",
                "collisionPos",
                "contactPosition",
                "contactPoint",
                "point",
                "position",
                "simPosition"
            })
            {
                object value;
                if (TryGetArgIgnoreCase(args, key, out value))
                {
                    CollectImpactVectorCandidates(value, candidates, new HashSet<object>(), 0);
                }
            }

            if (TryChooseImpactPosition(candidates, projectileItem, false, out position))
            {
                return true;
            }

            candidates.Clear();
            foreach (KeyValuePair<string, object> pair in args)
            {
                if (!IsLikelyImpactArgument(pair.Key, pair.Value))
                {
                    continue;
                }

                CollectImpactVectorCandidates(pair.Value, candidates, new HashSet<object>(), 0);
            }

            if (TryChooseImpactPosition(candidates, projectileItem, true, out position))
            {
                return true;
            }

            candidates.Clear();
            foreach (KeyValuePair<string, object> pair in args)
            {
                if (pair.Value is Vector2)
                {
                    candidates.Add((Vector2)pair.Value);
                }
            }

            return TryChooseImpactPosition(candidates, projectileItem, false, out position);
        }

        private static bool TryGetArgIgnoreCase(Dictionary<string, object> args, string key, out object value)
        {
            foreach (KeyValuePair<string, object> pair in args)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool IsLikelyImpactArgument(string key, object value)
        {
            string normalizedKey = key == null ? string.Empty : key.ToLowerInvariant();
            if (normalizedKey.Contains("contact") ||
                normalizedKey.Contains("collision") ||
                normalizedKey.Contains("hit") ||
                normalizedKey.Contains("fixture") ||
                normalizedKey.Contains("body") ||
                normalizedKey.Contains("position") ||
                normalizedKey.Contains("point"))
            {
                return true;
            }

            if (value == null)
            {
                return false;
            }

            string typeName = value.GetType().FullName ?? value.GetType().Name;
            typeName = typeName.ToLowerInvariant();
            return typeName.Contains("contact") ||
                   typeName.Contains("fixture") ||
                   typeName.Contains("body") ||
                   typeName.Contains("manifold");
        }

        private static void CollectImpactVectorCandidates(object value, List<Vector2> candidates, HashSet<object> visited, int depth)
        {
            if (value == null || depth > 3)
            {
                return;
            }

            if (value is Vector2)
            {
                Vector2 vector = (Vector2)value;
                if (vector != Vector2.Zero)
                {
                    candidates.Add(vector);
                }
                return;
            }

            if (value is string)
            {
                return;
            }

            Type valueType = value.GetType();
            if (!valueType.IsValueType)
            {
                if (visited.Contains(value))
                {
                    return;
                }
                visited.Add(value);
            }

            CollectContactWorldManifoldCandidates(value, candidates, visited, depth + 1);

            foreach (string memberName in new[]
            {
                "WorldPosition",
                "WorldPoint",
                "HitPosition",
                "CollisionPosition",
                "ContactPosition",
                "ContactPoint",
                "Point",
                "Position",
                "Value0",
                "Value1"
            })
            {
                object memberValue = GetMemberValue(value, memberName);
                if (memberValue != null && !ReferenceEquals(memberValue, value))
                {
                    CollectImpactVectorCandidates(memberValue, candidates, visited, depth + 1);
                }
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                return;
            }

            int count = 0;
            foreach (object entry in enumerable)
            {
                CollectImpactVectorCandidates(entry, candidates, visited, depth + 1);
                count++;
                if (count >= 4)
                {
                    break;
                }
            }
        }

        private static void CollectContactWorldManifoldCandidates(object value, List<Vector2> candidates, HashSet<object> visited, int depth)
        {
            if (value == null || depth > 3)
            {
                return;
            }

            foreach (MethodInfo method in value.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "GetWorldManifold", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                object[] values = new object[parameters.Length];
                bool canInvoke = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (!parameterType.IsByRef)
                    {
                        canInvoke = false;
                        break;
                    }

                    Type elementType = parameterType.GetElementType();
                    values[i] = GetDefaultValue(elementType ?? parameterType);
                }

                if (!canInvoke)
                {
                    continue;
                }

                try
                {
                    object result = method.Invoke(value, values);
                    if (method.ReturnType != typeof(void))
                    {
                        CollectImpactVectorCandidates(result, candidates, visited, depth + 1);
                    }

                    for (int i = 0; i < values.Length; i++)
                    {
                        CollectImpactVectorCandidates(values[i], candidates, visited, depth + 1);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool TryChooseImpactPosition(List<Vector2> candidates, Item projectileItem, bool preferSimUnits, out Vector2 position)
        {
            if (candidates == null || candidates.Count == 0)
            {
                position = Vector2.Zero;
                return false;
            }

            Vector2 projectilePosition;
            bool hasProjectilePosition = TryGetWorldPosition(projectileItem, out projectilePosition);
            float bestDistance = float.MaxValue;
            Vector2 best = Vector2.Zero;
            bool found = false;

            foreach (Vector2 candidate in candidates)
            {
                Vector2 world = NormalizeImpactCandidate(candidate, projectileItem, preferSimUnits);
                if (world == Vector2.Zero)
                {
                    continue;
                }

                float distance = hasProjectilePosition ? Vector2.DistanceSquared(world, projectilePosition) : 0.0f;
                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestDistance = distance;
                    best = world;
                }
            }

            position = best;
            return found;
        }

        private static Vector2 NormalizeImpactCandidate(Vector2 candidate, Item projectileItem, bool preferSimUnits)
        {
            Vector2 displayCandidate;
            if (!TryConvertSimToDisplayUnits(candidate, out displayCandidate))
            {
                return candidate;
            }

            if (preferSimUnits)
            {
                return displayCandidate;
            }

            Vector2 projectilePosition;
            if (!TryGetWorldPosition(projectileItem, out projectilePosition))
            {
                return candidate;
            }

            float directDistance = Vector2.DistanceSquared(candidate, projectilePosition);
            float convertedDistance = Vector2.DistanceSquared(displayCandidate, projectilePosition);
            return convertedDistance < directDistance ? displayCandidate : candidate;
        }

        private static bool TryConvertSimToDisplayUnits(Vector2 simPosition, out Vector2 displayPosition)
        {
            MethodInfo method = GetToDisplayUnitsMethod();
            if (method == null)
            {
                displayPosition = Vector2.Zero;
                return false;
            }

            try
            {
                object result = method.Invoke(null, new object[] { simPosition });
                if (result is Vector2)
                {
                    displayPosition = (Vector2)result;
                    return true;
                }
            }
            catch
            {
            }

            displayPosition = Vector2.Zero;
            return false;
        }

        private static MethodInfo GetToDisplayUnitsMethod()
        {
            if (toDisplayUnitsMethod != null || toDisplayUnitsLookupFailed)
            {
                return toDisplayUnitsMethod;
            }

            Type convertUnitsType = FindTypeByName("ConvertUnits");
            if (convertUnitsType != null)
            {
                foreach (MethodInfo method in convertUnitsType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (string.Equals(method.Name, "ToDisplayUnits", StringComparison.Ordinal) &&
                        method.ReturnType == typeof(Vector2) &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(Vector2))
                    {
                        toDisplayUnitsMethod = method;
                        return toDisplayUnitsMethod;
                    }
                }
            }

            toDisplayUnitsLookupFailed = true;
            return null;
        }

        private static bool TryGetWorldPosition(object instance, out Vector2 position)
        {
            foreach (string memberName in new[] { "WorldPosition", "SimPosition", "Position" })
            {
                object value = GetMemberValue(instance, memberName);
                if (value is Vector2)
                {
                    position = (Vector2)value;
                    return true;
                }
            }

            object body = GetMemberValue(instance, "body") ?? GetMemberValue(instance, "Body");
            if (body != null)
            {
                object value = GetMemberValue(body, "Position");
                if (value is Vector2)
                {
                    position = (Vector2)value;
                    return true;
                }
            }

            position = Vector2.Zero;
            return false;
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static object GetStaticMemberValue(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(null, null);
            }

            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(null);
        }

        private static bool TryGetFloatMember(object instance, string name, out float value)
        {
            object raw = GetMemberValue(instance, name);
            if (raw is float)
            {
                value = (float)raw;
                return true;
            }
            if (raw is double)
            {
                value = (float)(double)raw;
                return true;
            }
            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            value = 0.0f;
            return false;
        }

        private static bool TrySetMemberValue(object instance, string name, object value)
        {
            if (instance == null)
            {
                return false;
            }

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                try
                {
                    object converted = value != null && property.PropertyType.IsInstanceOfType(value) ?
                        value :
                        Convert.ChangeType(value, property.PropertyType);
                    property.SetValue(instance, converted, null);
                    return true;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    object converted = value != null && field.FieldType.IsInstanceOfType(value) ?
                        value :
                        Convert.ChangeType(value, field.FieldType);
                    field.SetValue(instance, converted);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static object CreateIdentifier(string value)
        {
            Type identifierType = typeof(Identifier);
            ConstructorInfo constructor = identifierType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (constructor != null)
            {
                return constructor.Invoke(new object[] { value });
            }

            return value;
        }

        private static bool CanPassIdentifier(Type parameterType, object identifier)
        {
            if (identifier == null)
            {
                return false;
            }

            return parameterType.IsInstanceOfType(identifier) || parameterType == typeof(string);
        }

        private static object ConvertIdentifierForParameter(Type parameterType, object identifier)
        {
            if (identifier != null && parameterType.IsInstanceOfType(identifier))
            {
                return identifier;
            }

            return identifier == null ? null : identifier.ToString();
        }

        private static object[] BuildMethodArguments(ParameterInfo[] parameters, object first, object second)
        {
            object[] values = new object[parameters.Length];
            values[0] = ConvertValueForParameter(parameters[0].ParameterType, first);
            if (parameters.Length > 1)
            {
                values[1] = ConvertValueForParameter(parameters[1].ParameterType, second);
            }

            for (int i = 2; i < values.Length; i++)
            {
                values[i] = GetDefaultValue(parameters[i].ParameterType);
            }

            return values;
        }

        private static object ConvertValueForParameter(Type parameterType, object value)
        {
            if (value == null)
            {
                return GetDefaultValue(parameterType);
            }

            if (parameterType.IsInstanceOfType(value))
            {
                return value;
            }

            if (parameterType == typeof(string))
            {
                return value.ToString();
            }

            if (parameterType == typeof(float) && value is double)
            {
                return (float)(double)value;
            }

            try
            {
                return Convert.ChangeType(value, parameterType);
            }
            catch
            {
                return GetDefaultValue(parameterType);
            }
        }

        private static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static void LogOnce(string key, string message)
        {
            if (LoggedOnce.Add(key))
            {
                LuaCsLogger.LogMessage(message);
            }
        }

        private sealed class ChargeState
        {
            public float ChargeSeconds;
            public bool WasReadyLogged;
            public bool WasFullyChargedLogged;
            public bool WasChargeSoundPlayed;
        }

        private sealed class SuperShotData
        {
            public readonly Character Attacker;
            public readonly int ArrowCount;

            public SuperShotData(Character attacker, int arrowCount)
            {
                Attacker = attacker;
                ArrowCount = Math.Max(1, arrowCount);
            }
        }

        private sealed class PendingSuperShot
        {
            public readonly Character Attacker;
            public readonly Item LoadedArrow;
            public readonly int ArrowCount;
            public readonly int CreatedTicks;

            public PendingSuperShot(Character attacker, Item loadedArrow, int arrowCount, int createdTicks)
            {
                Attacker = attacker;
                LoadedArrow = loadedArrow;
                ArrowCount = Math.Max(1, arrowCount);
                CreatedTicks = createdTicks;
            }
        }

        private sealed class BeamVisual
        {
            public readonly Vector2 StartScreen;
            public readonly Vector2 EndScreen;
            public readonly int CreatedTicks;
            public readonly float Duration;

            public BeamVisual(Vector2 startScreen, Vector2 endScreen, int createdTicks, float duration)
            {
                StartScreen = startScreen;
                EndScreen = endScreen;
                CreatedTicks = createdTicks;
                Duration = Math.Max(0.01f, duration);
            }
        }

        private sealed class ExplosionVisual
        {
            public readonly Vector2 WorldPosition;
            public readonly int CreatedTicks;
            public readonly float Duration;

            public ExplosionVisual(Vector2 worldPosition, int createdTicks, float duration)
            {
                WorldPosition = worldPosition;
                CreatedTicks = createdTicks;
                Duration = Math.Max(0.01f, duration);
            }
        }

        private sealed class DelayedItemRemoval
        {
            public readonly Item Item;
            public readonly int RemoveAtTicks;

            public DelayedItemRemoval(Item item, int removeAtTicks)
            {
                Item = item;
                RemoveAtTicks = removeAtTicks;
            }
        }

        private sealed class WeaponOverride
        {
            private readonly object target;
            private readonly Dictionary<string, object> originalValues = new Dictionary<string, object>();

            public WeaponOverride(object target)
            {
                this.target = target;
            }

            public bool HasAnyChange
            {
                get { return originalValues.Count > 0; }
            }

            public void TryOverrideInt(string memberName, int value)
            {
                object original = GetMemberValue(target, memberName);
                if (original == null)
                {
                    return;
                }

                if (TrySetMemberValue(target, memberName, value))
                {
                    originalValues[memberName] = original;
                }
            }

            public void TryOverrideFloat(string memberName, float value)
            {
                object original = GetMemberValue(target, memberName);
                if (original == null)
                {
                    return;
                }

                if (TrySetMemberValue(target, memberName, value))
                {
                    originalValues[memberName] = original;
                }
            }

            public void Restore()
            {
                foreach (KeyValuePair<string, object> pair in originalValues)
                {
                    TrySetMemberValue(target, pair.Key, pair.Value);
                }
                originalValues.Clear();
            }
        }
    }
}
