using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private const string RangedWeaponBeforeHook = "elysianrealm.gameplay.rangedweapon.use.before";
        private const string RangedWeaponAfterHook = "elysianrealm.gameplay.rangedweapon.use.after";

        private const string BowIdentifier = "pastflower";
        private const string HornIdentifier = "elysiahorn";
        private const string ArrowIdentifier = "lovespears";
        private const string HornBuffIdentifier = "elysiaencouragement";
        private const string HumanSourceIdentifier = "asourcefromherrscherofhuman";
        private const string OriginSourceIdentifier = "asourcefromherrscheroforigin";

        private const float BowSuperChargeSeconds = 15.0f;
        private const float BowMinChargeSeconds = 0.5f;
        private const float BowSuperImpulseMultiplier = 10.0f;
        private const float PastflowerExplosionRange = 500.0f;
        private const float PastflowerExplosionInternalDamage = 500.0f;
        private const float PastflowerExplosionStructureDamage = 300.0f;
        private const float PastflowerExplosionItemDamage = 200.0f;
        private const float HornRange = 1000.0f;
        private const float HornCooldownSeconds = 2.0f;
        private const float StigmataGateInterval = 0.5f;

        private static readonly Dictionary<Character, ChargeState> ChargeStates = new Dictionary<Character, ChargeState>();
        private static readonly Dictionary<Character, float> HornCooldowns = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, float> StigmataGateTimers = new Dictionary<Character, float>();
        private static readonly Dictionary<object, WeaponOverride> WeaponOverrides = new Dictionary<object, WeaponOverride>();
        private static readonly Dictionary<Item, SuperShotData> SuperProjectiles = new Dictionary<Item, SuperShotData>();
        private static readonly HashSet<Item> RemovedVolleyAmmo = new HashSet<Item>();
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

        private static ContentPackage ownerPackage;
        private static MethodInfo characterIsKeyDownMethod;
        private static MethodInfo characterIsKeyHitMethod;
        private static Type rangedWeaponType;

        public void PreInitPatching()
        {
            LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianGameplayPlugin>(out ownerPackage);
            CacheInputMethods();
            HookCharacterControl(this);
            HookBowHud(this);
            HookProjectileImpact(this);
            HookRangedWeaponUse(this);

            string packageDir = ownerPackage == null ? "<unresolved>" : ownerPackage.Dir;
            LuaCsLogger.LogMessage("[ElysianRealm] Gameplay plugin registered. Package=" + packageDir);
        }

        public void Initialize()
        {
        }

        public void OnLoadCompleted()
        {
        }

        public void Dispose()
        {
            ChargeStates.Clear();
            HornCooldowns.Clear();
            StigmataGateTimers.Clear();
            WeaponOverrides.Clear();
            SuperProjectiles.Clear();
            RemovedVolleyAmmo.Clear();
            LoggedOnce.Clear();
            ownerPackage = null;
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

            LuaCsSetup.Instance.EventService.HookMethod(
                RangedWeaponAfterHook,
                method,
                RangedWeaponUseAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
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
        }

        private static void HookProjectileImpact(IAssemblyPlugin hookOwner)
        {
            Type projectileType = FindTypeByName("Projectile");
            if (projectileType == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] Projectile component type was not found; pastflower impact explosion disabled.");
                return;
            }

            string[] preferredNames = new[]
            {
                "HandleProjectileCollision",
                "OnProjectileCollision",
                "OnCollision",
                "Impact"
            };

            MethodInfo method = projectileType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => preferredNames.Any(n => string.Equals(m.Name, n, StringComparison.Ordinal))) ??
                projectileType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name.IndexOf("Collision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     m.Name.IndexOf("Impact", StringComparison.OrdinalIgnoreCase) >= 0);

            if (method == null)
            {
                LuaCsLogger.LogError("[ElysianRealm] Projectile impact method was not found; pastflower impact explosion disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                ProjectileImpactHook,
                method,
                ProjectileImpactAfter,
                ILuaCsHook.HookMethodType.After,
                owner: hookOwner);
        }

        private static object CharacterControlAfter(object self, Dictionary<string, object> args)
        {
            Character character = self as Character;
            if (!IsUsableCharacter(character))
            {
                return null;
            }

            float deltaTime = GetFloatArg(args, "deltaTime", 1.0f / 60.0f);
            UpdateBowCharge(character, deltaTime);
            UpdateHorn(character, deltaTime);
            UpdateStigmataGate(character, deltaTime);
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
                if (loadedArrow != null)
                {
                    SuperProjectiles[loadedArrow] = new SuperShotData(character, arrowCount);
                }
                ApplyAffliction(character, HornBuffIdentifier, 10.0f);
            }

            state.ChargeSeconds = 0.0f;
            state.WasReadyLogged = false;
            state.WasFullyChargedLogged = false;

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

        private static object ProjectileImpactAfter(object self, Dictionary<string, object> args)
        {
            Item projectileItem = GetComponentItem(self);
            if (projectileItem == null)
            {
                return null;
            }

            SuperShotData data;
            if (!SuperProjectiles.TryGetValue(projectileItem, out data))
            {
                return null;
            }

            SuperProjectiles.Remove(projectileItem);
            Vector2 position;
            if (!TryGetWorldPosition(projectileItem, out position))
            {
                position = data.Attacker == null ? Vector2.Zero : data.Attacker.WorldPosition;
            }

            ApplyPastflowerExplosion(data.Attacker, position);
            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super impact explosion applied. arrows=" + data.ArrowCount);
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
            if (!IsUsableCharacter(character) || !IsInputDown(character, "Aim"))
            {
                return null;
            }

            SpriteBatch spriteBatch = GetArgByType<SpriteBatch>(args, "spriteBatch");
            if (spriteBatch == null || GUI.WhiteTexture == null)
            {
                return null;
            }

            ChargeState state = GetChargeState(character);
            if (state.ChargeSeconds <= 0.0f)
            {
                return null;
            }

            LogOnce("bow_charge_visuals", "[ElysianRealm] Pastflower charge visuals are drawing.");
            DrawBowChargeVisuals(spriteBatch, bow, character, state.ChargeSeconds);
            return null;
        }

        private static void UpdateBowCharge(Character character, float deltaTime)
        {
            ChargeState state = GetChargeState(character);
            Item bow = FindHeldItem(character, BowIdentifier);
            if (bow == null || !IsInputDown(character, "Aim") || !IsHoldingOnlyBow(character, bow) || !HasAnyArrowAvailable(character, bow))
            {
                if (state.ChargeSeconds > 0.0f)
                {
                    state.ChargeSeconds = 0.0f;
                    state.WasReadyLogged = false;
                    state.WasFullyChargedLogged = false;
                }
                return;
            }

            state.ChargeSeconds += Math.Max(0.0f, deltaTime);
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

        private static void UpdateHorn(Character character, float deltaTime)
        {
            float cooldown;
            HornCooldowns.TryGetValue(character, out cooldown);
            cooldown = Math.Max(0.0f, cooldown - Math.Max(0.0f, deltaTime));
            HornCooldowns[character] = cooldown;

            Item horn = FindHeldItem(character, HornIdentifier);
            if (horn == null || cooldown > 0.0f)
            {
                return;
            }

            bool triggered = IsInputHit(character, "Shoot") || IsInputHit(character, "Use");
            if (!triggered)
            {
                return;
            }

            HornCooldowns[character] = HornCooldownSeconds;
            int buffed = 0;
            int taunted = 0;

            foreach (Character target in Character.CharacterList)
            {
                if (!IsUsableCharacter(target))
                {
                    continue;
                }

                float distance = Vector2.Distance(character.WorldPosition, target.WorldPosition);
                if (distance > HornRange)
                {
                    continue;
                }

                if (target == character || IsFriendly(character, target))
                {
                    ApplyAffliction(target, HornBuffIdentifier, 10.0f);
                    buffed++;
                    continue;
                }

                if (TryForceAiTarget(target, character))
                {
                    taunted++;
                }
                else
                {
                    ApplyAffliction(target, "psychosis", 2.0f);
                }
            }

            LuaCsLogger.LogMessage("[ElysianRealm] Horn used. buffed=" + buffed + ", taunted=" + taunted);
        }

        private static void DrawBowChargeVisuals(SpriteBatch spriteBatch, Item bow, Character character, float chargeSeconds)
        {
            float ratio = MathHelper.Clamp(chargeSeconds / BowSuperChargeSeconds, 0.0f, 1.0f);
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

            DrawBowChargeParticles(spriteBatch, PlayerInput.MousePosition, ratio, chargeSeconds, 1.0f);
            DrawBowChargeParticles(spriteBatch, pos + new Vector2(width * 0.5f, height * 0.5f), ratio, chargeSeconds, 0.55f);

            Vector2 bowScreenPosition;
            if (TryGetChargeVisualScreenPosition(bow, character, out bowScreenPosition))
            {
                DrawBowChargeParticles(spriteBatch, bowScreenPosition, ratio, chargeSeconds, 1.2f);
            }
        }

        private static void DrawBowChargeParticles(SpriteBatch spriteBatch, Vector2 center, float ratio, float chargeSeconds, float scale)
        {
            int particleCount = 10 + (int)(ratio * 70.0f);
            float time = (Environment.TickCount & 0xFFFF) / 1000.0f;
            float alpha = MathHelper.Clamp(0.24f + ratio * 0.66f, 0.24f, 0.9f);
            float spread = (22.0f + ratio * 72.0f) * scale;
            int coreSize = Math.Max(6, (int)Math.Round((10.0f + ratio * 22.0f) * scale));
            Rectangle core = new Rectangle(
                (int)Math.Round(center.X - coreSize * 0.5f),
                (int)Math.Round(center.Y - coreSize * 0.5f),
                coreSize,
                coreSize);

            spriteBatch.Draw(GUI.WhiteTexture, core, new Color(255, 90, 215, 255) * (0.12f + ratio * 0.22f));

            for (int i = 0; i < particleCount; i++)
            {
                float seed = i * 2.399963f;
                float pulse = (float)Math.Sin(time * (1.5f + i * 0.07f) + seed + chargeSeconds * 0.2f);
                float distanceRatio = 0.2f + ((i * 37) % 100) / 100.0f * 0.8f;
                float distance = spread * distanceRatio * (0.82f + pulse * 0.18f);
                float angle = seed + time * (1.25f + ratio * 1.25f);
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance;
                int size = Math.Max(2, (int)Math.Round((3.0f + ratio * 7.0f) * scale * (0.75f + distanceRatio * 0.5f)));
                Rectangle rect = new Rectangle((int)(center.X + offset.X), (int)(center.Y + offset.Y), size, size);
                Color color = (i % 3 == 0 ? new Color(255, 225, 245, 255) : new Color(255, 95, 220, 255)) * alpha;
                spriteBatch.Draw(GUI.WhiteTexture, rect, color);
            }
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

        private static void UpdateStigmataGate(Character character, float deltaTime)
        {
            float timer;
            StigmataGateTimers.TryGetValue(character, out timer);
            timer -= Math.Max(0.0f, deltaTime);
            if (timer > 0.0f)
            {
                StigmataGateTimers[character] = timer;
                return;
            }

            StigmataGateTimers[character] = StigmataGateInterval;

            bool hasHumanSource = GetAfflictionStrength(character, HumanSourceIdentifier) > 0.01f;
            bool hasOriginSource = GetAfflictionStrength(character, OriginSourceIdentifier) > 0.01f;

            if (!hasHumanSource)
            {
                ReduceAffliction(character, "elysiastigmata_top_human_effect", 1000.0f);
                ReduceAffliction(character, "elysiastigmata_mid_human_effect", 1000.0f);
                ReduceAffliction(character, "elysiastigmata_bottom_human_effect", 1000.0f);
            }

            if (!hasOriginSource)
            {
                ReduceAffliction(character, "elysiastigmata_top_origin_effect", 1000.0f);
                ReduceAffliction(character, "elysiastigmata_mid_origin_effect", 1000.0f);
                ReduceAffliction(character, "elysiastigmata_bottom_origin_effect", 1000.0f);
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

        private static bool HasAnyArrowAvailable(Character character, Item bow)
        {
            return HasLoadedArrow(bow) || FindInventoryArrow(character) != null;
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
                LogOnce("remove_item_failed", "[ElysianRealm] Failed to remove volley arrow: " + ex.GetType().Name);
                return false;
            }
        }

        private static void ApplyPastflowerExplosion(Character attacker, Vector2 position)
        {
            int characterHits = 0;
            int itemHits = 0;
            int structureHits = 0;

            foreach (Character target in Character.CharacterList)
            {
                if (!IsUsableCharacter(target) || ReferenceEquals(target, attacker))
                {
                    continue;
                }

                if (Vector2.Distance(target.WorldPosition, position) > PastflowerExplosionRange)
                {
                    continue;
                }

                if (ApplyAffliction(target, "internaldamage", PastflowerExplosionInternalDamage))
                {
                    characterHits++;
                }
            }

            object itemList = GetStaticMemberValue(typeof(Item), "ItemList");
            foreach (Item item in EnumerateItems(itemList))
            {
                if (item == null || IsRemovedObject(item))
                {
                    continue;
                }

                Vector2 itemPosition;
                if (!TryGetWorldPosition(item, out itemPosition) ||
                    Vector2.Distance(itemPosition, position) > PastflowerExplosionRange)
                {
                    continue;
                }

                if (TryApplyObjectDamage(item, position, PastflowerExplosionItemDamage))
                {
                    itemHits++;
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

            MethodInfo instantiate = prefab.GetType().GetMethod("Instantiate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float) }, null);
            if (instantiate == null)
            {
                LogOnce("affliction_instantiate_" + identifier, "[ElysianRealm] Affliction instantiate method not found: " + identifier);
                return false;
            }

            object affliction = instantiate.Invoke(prefab, new object[] { strength });
            object limb = character.AnimController == null ? null : character.AnimController.MainLimb;
            return InvokeHealthMethod(character.CharacterHealth, "ApplyAffliction", limb, affliction);
        }

        private static float GetAfflictionStrength(Character character, string identifier)
        {
            if (character == null || character.CharacterHealth == null)
            {
                return 0.0f;
            }

            object id = CreateIdentifier(identifier);
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

            LogOnce("reduce_affliction_failed", "[ElysianRealm] Could not find a compatible ReduceAffliction method; stigmata gate fallback disabled.");
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

                object[] values = new object[parameters.Length];
                values[0] = limb;
                values[1] = affliction;
                for (int i = 2; i < values.Length; i++)
                {
                    values[i] = GetDefaultValue(parameters[i].ParameterType);
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
                    property.SetValue(instance, Convert.ChangeType(value, property.PropertyType), null);
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
                    field.SetValue(instance, Convert.ChangeType(value, field.FieldType));
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
