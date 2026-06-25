using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.LuaCs.Compatibility;
using Microsoft.Xna.Framework;

[assembly: IgnoreAccessChecksTo("Barotrauma")]
[assembly: IgnoreAccessChecksTo("BarotraumaCore")]
[assembly: IgnoreAccessChecksTo("DedicatedServer")]

namespace Barotrauma.ElysianRealm
{
    public sealed class ElysianGameplayPlugin : IAssemblyPlugin
    {
        private const string CharacterControlHook = "elysianrealm.gameplay.character.control";
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
        private const float HornRange = 1000.0f;
        private const float HornCooldownSeconds = 2.0f;
        private const float StigmataGateInterval = 0.5f;

        private static readonly Dictionary<Character, ChargeState> ChargeStates = new Dictionary<Character, ChargeState>();
        private static readonly Dictionary<Character, float> HornCooldowns = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, float> StigmataGateTimers = new Dictionary<Character, float>();
        private static readonly Dictionary<object, WeaponOverride> WeaponOverrides = new Dictionary<object, WeaponOverride>();
        private static readonly HashSet<Item> RemovedVolleyAmmo = new HashSet<Item>();
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

        private static ContentPackage ownerPackage;
        private static MethodInfo characterIsKeyDownMethod;
        private static MethodInfo characterIsKeyHitMethod;

        public void PreInitPatching()
        {
            LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianGameplayPlugin>(out ownerPackage);
            CacheInputMethods();
            HookCharacterControl(this);
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
            RemovedVolleyAmmo.Clear();
            LoggedOnce.Clear();
            ownerPackage = null;
        }

        private static void HookCharacterControl(object hookOwner)
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

        private static void HookRangedWeaponUse(object hookOwner)
        {
            MethodInfo method = typeof(RangedWeapon).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
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
            RangedWeapon rangedWeapon = self as RangedWeapon;
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
            if (state.ChargeSeconds < BowSuperChargeSeconds)
            {
                return null;
            }

            if (!IsInputDown(character, "Aim"))
            {
                return null;
            }

            List<Item> ammo = FindArrowAmmo(character, bow);
            if (ammo.Count <= 0 || !HasLoadedArrow(bow))
            {
                return null;
            }

            int volleyCount = Math.Max(1, ammo.Count);
            WeaponOverride weaponOverride = CreateWeaponOverride(rangedWeapon, volleyCount);
            if (!weaponOverride.HasAnyChange)
            {
                LogOnce("bow_override_failed", "[ElysianRealm] Could not override bow projectile values; super shot will only consume extra arrows.");
            }
            else
            {
                WeaponOverrides[rangedWeapon] = weaponOverride;
            }

            int consumed = ConsumeExtraVolleyAmmo(ammo, Math.Max(0, volleyCount - 1));
            ApplyAffliction(character, HornBuffIdentifier, 10.0f);
            state.ChargeSeconds = 0.0f;
            state.WasFullyChargedLogged = false;

            LuaCsLogger.LogMessage("[ElysianRealm] Pastflower super shot prepared. arrows=" + volleyCount + ", extraConsumed=" + consumed);
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

        private static void UpdateBowCharge(Character character, float deltaTime)
        {
            ChargeState state = GetChargeState(character);
            Item bow = FindHeldItem(character, BowIdentifier);
            if (bow == null || !IsInputDown(character, "Aim"))
            {
                if (state.ChargeSeconds > 0.0f)
                {
                    state.ChargeSeconds = 0.0f;
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

        private static WeaponOverride CreateWeaponOverride(RangedWeapon rangedWeapon, int projectileCount)
        {
            WeaponOverride weaponOverride = new WeaponOverride(rangedWeapon);
            weaponOverride.TryOverrideInt("ProjectileCount", projectileCount);

            float launchImpulse;
            if (TryGetFloatMember(rangedWeapon, "LaunchImpulse", out launchImpulse))
            {
                weaponOverride.TryOverrideFloat("LaunchImpulse", launchImpulse * BowSuperImpulseMultiplier);
            }

            return weaponOverride;
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
                    consumed++;
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

        private static bool HasIdentifier(Item item, string identifier)
        {
            if (item == null)
            {
                return false;
            }

            return string.Equals(item.Prefab.Identifier.ToString(), identifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Identifier.ToString(), identifier, StringComparison.OrdinalIgnoreCase);
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
