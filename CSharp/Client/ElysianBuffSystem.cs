using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using Barotrauma;

namespace Barotrauma.ElysianRealm
{
    internal sealed class ElysianBuffEngine
    {
        private const string StigmataSystemId = "stigmata_slot";
        private const string TalentAfflictionSystemId = "talent_affliction";

        private readonly ElysianBuffGameApi api;
        private readonly BuffStateStore stateStore;
        private readonly BuffArbiter arbiter;
        private readonly List<IBuffTrigger> triggers;
        private readonly List<IBuffCondition> conditions;
        private bool initialized;

        public ElysianBuffEngine(ElysianBuffGameApi api)
        {
            this.api = api;
            stateStore = new BuffStateStore();
            arbiter = new BuffArbiter(api, stateStore);
            triggers = new List<IBuffTrigger>();
            conditions = new List<IBuffCondition>();
        }

        public void Initialize(string packageDir)
        {
            triggers.Clear();
            conditions.Clear();
            stateStore.Clear();

            StigmataRuleSet stigmataRuleSet = BuffRuleLoader.LoadStigmataRules(packageDir, api);
            TalentAfflictionRuleSet talentRuleSet = BuffRuleLoader.LoadTalentAfflictionRules(packageDir, api);
            triggers.Add(new StigmataSlotTickTrigger(stigmataRuleSet));
            triggers.Add(new TalentAfflictionTickTrigger(talentRuleSet));
            conditions.Add(new StigmataSlotCondition(api));
            conditions.Add(new TalentAfflictionCondition(api));

            initialized = true;
            api.Log("[ElysianRealm] Buff engine initialized. stigmataRules=" + stigmataRuleSet.Rules.Count + ", talentRules=" + talentRuleSet.Rules.Count);
        }

        public void UpdateCharacter(Character character, float deltaTime)
        {
            if (!initialized || character == null)
            {
                return;
            }

            foreach (IBuffTrigger trigger in triggers)
            {
                BuffTriggerResult result = trigger.Evaluate(character, deltaTime);
                if (result == null || !result.ShouldArbitrate)
                {
                    continue;
                }

                List<BuffBlackboard> accepted = new List<BuffBlackboard>();
                foreach (BuffBlackboard blackboard in result.Contexts)
                {
                    if (ConditionsPass(blackboard))
                    {
                        accepted.Add(blackboard);
                    }
                }

                arbiter.Apply(character, result.SystemId, accepted);
            }
        }

        public void Dispose()
        {
            arbiter.ClearAll();
            stateStore.Clear();
            triggers.Clear();
            conditions.Clear();
            initialized = false;
        }

        private bool ConditionsPass(BuffBlackboard blackboard)
        {
            foreach (IBuffCondition condition in conditions)
            {
                if (condition.Supports(blackboard) && !condition.IsMet(blackboard))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class BuffRuleLoader
        {
            public static StigmataRuleSet LoadStigmataRules(string packageDir, ElysianBuffGameApi api)
            {
                StigmataRuleSet ruleSet = new StigmataRuleSet();
                XmlNode root = LoadRuleRoot(packageDir, api, "StigmataSlotRules");
                if (root == null)
                {
                    return ruleSet;
                }

                ruleSet.RefreshInterval = ReadFloat(root, "refreshinterval", 0.5f);
                foreach (XmlNode node in root.SelectNodes("./StigmataRule"))
                {
                    StigmataBuffRule rule;
                    if (TryReadStigmataRule(node, out rule))
                    {
                        ruleSet.Rules.Add(rule);
                    }
                }

                api.Log("[ElysianRealm] Stigmata buff rules loaded. rules=" + ruleSet.Rules.Count + ", refresh=" + ruleSet.RefreshInterval.ToString(CultureInfo.InvariantCulture));
                return ruleSet;
            }

            public static TalentAfflictionRuleSet LoadTalentAfflictionRules(string packageDir, ElysianBuffGameApi api)
            {
                TalentAfflictionRuleSet ruleSet = new TalentAfflictionRuleSet();
                XmlNode root = LoadRuleRoot(packageDir, api, "TalentAfflictionRules");
                if (root == null)
                {
                    return ruleSet;
                }

                ruleSet.RefreshInterval = ReadFloat(root, "refreshinterval", 0.5f);
                foreach (XmlNode node in root.SelectNodes("./TalentAfflictionRule"))
                {
                    TalentAfflictionRule rule;
                    if (TryReadTalentAfflictionRule(node, out rule))
                    {
                        ruleSet.Rules.Add(rule);
                    }
                }

                api.Log("[ElysianRealm] Talent affliction buff rules loaded. rules=" + ruleSet.Rules.Count + ", refresh=" + ruleSet.RefreshInterval.ToString(CultureInfo.InvariantCulture));
                return ruleSet;
            }

            private static XmlNode LoadRuleRoot(string packageDir, ElysianBuffGameApi api, string nodeName)
            {
                if (string.IsNullOrWhiteSpace(packageDir))
                {
                    api.LogOnce("buff_config_package_missing", "[ElysianRealm] Buff rule config skipped: package directory is unresolved.");
                    return null;
                }

                string path = Path.Combine(packageDir, "Config", "ElysianBuffRules.xml");
                if (!File.Exists(path))
                {
                    api.LogOnce("buff_config_missing", "[ElysianRealm] Buff rule config not found: " + path);
                    return null;
                }

                try
                {
                    XmlDocument document = new XmlDocument();
                    document.Load(path);
                    XmlNode root = document.SelectSingleNode("/ElysianBuffRules/" + nodeName);
                    if (root == null)
                    {
                        api.LogOnce("buff_config_" + nodeName + "_missing", "[ElysianRealm] Buff rule config has no " + nodeName + " node.");
                        return null;
                    }

                    return root;
                }
                catch (Exception ex)
                {
                    api.LogOnce("buff_config_load_failed", "[ElysianRealm] Failed to load buff rule config: " + ex.GetType().Name);
                    return null;
                }
            }

            private static bool TryReadStigmataRule(XmlNode node, out StigmataBuffRule rule)
            {
                rule = null;
                if (node == null)
                {
                    return false;
                }

                string item = ReadString(node, "item", string.Empty);
                string source = ReadString(node, "source", string.Empty);
                string effect = ReadString(node, "effect", string.Empty);
                int slot = ReadInt(node, "slot", -1);
                float strength = ReadFloat(node, "strength", 100.0f);
                if (string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(effect) || slot < 0)
                {
                    return false;
                }

                rule = new StigmataBuffRule(item, slot, source, effect, strength);
                return true;
            }

            private static bool TryReadTalentAfflictionRule(XmlNode node, out TalentAfflictionRule rule)
            {
                rule = null;
                if (node == null)
                {
                    return false;
                }

                string marker = ReadString(node, "conditionaffliction", string.Empty);
                string source = ReadString(node, "source", string.Empty);
                string effect = ReadString(node, "effect", string.Empty);
                float strength = ReadFloat(node, "strength", 100.0f);
                float minStrength = ReadFloat(node, "minstrength", 0.01f);
                if (string.IsNullOrWhiteSpace(marker) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(effect))
                {
                    return false;
                }

                rule = new TalentAfflictionRule(marker, source, effect, strength, minStrength);
                return true;
            }

            private static string ReadString(XmlNode node, string name, string fallback)
            {
                XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[name];
                return attribute == null ? fallback : attribute.Value;
            }

            private static int ReadInt(XmlNode node, string name, int fallback)
            {
                int value;
                return int.TryParse(ReadString(node, name, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }

            private static float ReadFloat(XmlNode node, string name, float fallback)
            {
                float value;
                return float.TryParse(ReadString(node, name, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
            }
        }

        private sealed class StigmataSlotTickTrigger : IBuffTrigger
        {
            private const string StigmataSlotIdentifier = "stigmataslot";

            private readonly StigmataRuleSet ruleSet;
            private readonly Dictionary<Character, float> timers = new Dictionary<Character, float>();

            public StigmataSlotTickTrigger(StigmataRuleSet ruleSet)
            {
                this.ruleSet = ruleSet;
            }

            public BuffTriggerResult Evaluate(Character character, float deltaTime)
            {
                float timer;
                timers.TryGetValue(character, out timer);
                timer -= Math.Max(0.0f, deltaTime);
                if (timer > 0.0f)
                {
                    timers[character] = timer;
                    return BuffTriggerResult.Skip(StigmataSystemId);
                }

                timers[character] = Math.Max(0.05f, ruleSet.RefreshInterval);

                List<BuffBlackboard> contexts = new List<BuffBlackboard>();
                object characterInventory = ReflectionInventory.GetInventory(character);
                foreach (Item slotItem in ReflectionInventory.EnumerateDirectInventoryItems(characterInventory))
                {
                    if (!ReflectionInventory.HasIdentifier(slotItem, StigmataSlotIdentifier))
                    {
                        continue;
                    }

                    object slotInventory = ReflectionInventory.GetInventory(slotItem);
                    foreach (Item containedItem in ReflectionInventory.EnumerateInventoryItems(slotInventory))
                    {
                        StigmataBuffRule rule = ruleSet.FindByItem(containedItem);
                        if (rule == null)
                        {
                            continue;
                        }

                        int slotIndex = ReflectionInventory.FindInventoryItemIndex(slotInventory, containedItem);
                        contexts.Add(new BuffBlackboard(character, "OnTick", StigmataSystemId, slotItem, containedItem, slotIndex, deltaTime, rule));
                    }
                }

                return BuffTriggerResult.Apply(StigmataSystemId, contexts);
            }
        }

        private sealed class StigmataSlotCondition : IBuffCondition
        {
            private readonly ElysianBuffGameApi api;

            public StigmataSlotCondition(ElysianBuffGameApi api)
            {
                this.api = api;
            }

            public bool Supports(BuffBlackboard blackboard)
            {
                return blackboard != null && blackboard.Rule is StigmataBuffRule;
            }

            public bool IsMet(BuffBlackboard blackboard)
            {
                StigmataBuffRule rule = blackboard.Rule as StigmataBuffRule;
                if (rule == null || blackboard.ContainedItem == null)
                {
                    return false;
                }

                if (!ReflectionInventory.HasIdentifier(blackboard.ContainedItem, rule.ItemIdentifier))
                {
                    return false;
                }

                if (blackboard.SlotIndex < 0)
                {
                    api.LogOnce("buff_stigmata_slot_index_unresolved", "[ElysianRealm] Buff engine could not resolve stigmata slot index; XML restrictions are used as fallback.");
                    return true;
                }

                if (blackboard.SlotIndex != rule.SlotIndex)
                {
                    api.LogOnce(
                        "buff_stigmata_wrong_slot_" + rule.ItemIdentifier,
                        "[ElysianRealm] Buff engine ignored " + rule.ItemIdentifier + " in slot " + (blackboard.SlotIndex + 1) + ", expected slot " + (rule.SlotIndex + 1) + ".");
                    return false;
                }

                return true;
            }
        }

        private sealed class TalentAfflictionTickTrigger : IBuffTrigger
        {
            private readonly TalentAfflictionRuleSet ruleSet;
            private readonly Dictionary<Character, float> timers = new Dictionary<Character, float>();

            public TalentAfflictionTickTrigger(TalentAfflictionRuleSet ruleSet)
            {
                this.ruleSet = ruleSet;
            }

            public BuffTriggerResult Evaluate(Character character, float deltaTime)
            {
                float timer;
                timers.TryGetValue(character, out timer);
                timer -= Math.Max(0.0f, deltaTime);
                if (timer > 0.0f)
                {
                    timers[character] = timer;
                    return BuffTriggerResult.Skip(TalentAfflictionSystemId);
                }

                timers[character] = Math.Max(0.05f, ruleSet.RefreshInterval);

                List<BuffBlackboard> contexts = new List<BuffBlackboard>();
                foreach (TalentAfflictionRule rule in ruleSet.Rules)
                {
                    contexts.Add(new BuffBlackboard(character, "OnTick", TalentAfflictionSystemId, null, null, -1, deltaTime, rule));
                }

                return BuffTriggerResult.Apply(TalentAfflictionSystemId, contexts);
            }
        }

        private sealed class TalentAfflictionCondition : IBuffCondition
        {
            private readonly ElysianBuffGameApi api;

            public TalentAfflictionCondition(ElysianBuffGameApi api)
            {
                this.api = api;
            }

            public bool Supports(BuffBlackboard blackboard)
            {
                return blackboard != null && blackboard.Rule is TalentAfflictionRule;
            }

            public bool IsMet(BuffBlackboard blackboard)
            {
                TalentAfflictionRule rule = blackboard.Rule as TalentAfflictionRule;
                if (rule == null)
                {
                    return false;
                }

                return api.GetAfflictionStrength(blackboard.Character, rule.ConditionAfflictionId) >= rule.MinStrength;
            }
        }

        private sealed class BuffArbiter
        {
            private readonly BuffStateStore stateStore;
            private readonly IBuffEffect applyEffect;
            private readonly IBuffEffect removeEffect;

            public BuffArbiter(ElysianBuffGameApi api, BuffStateStore stateStore)
            {
                this.stateStore = stateStore;
                applyEffect = new ApplyAfflictionBuffEffect(api);
                removeEffect = new RemoveAfflictionBuffEffect(api);
            }

            public void Apply(Character character, string systemId, List<BuffBlackboard> activeContexts)
            {
                HashSet<string> activeSources = new HashSet<string>();
                foreach (BuffBlackboard context in activeContexts)
                {
                    BuffRule rule = context.Rule;
                    if (rule == null)
                    {
                        continue;
                    }

                    BuffStateEntry existing = stateStore.Get(character, rule.SourceId);
                    if (existing != null && !string.Equals(existing.EffectId, rule.EffectId, StringComparison.OrdinalIgnoreCase))
                    {
                        removeEffect.Apply(new BuffBlackboard(character, context.TriggerId, systemId, context.SourceItem, context.ContainedItem, context.SlotIndex, context.DeltaTime, existing.ToRule()));
                        stateStore.Remove(character, rule.SourceId);
                    }

                    if (applyEffect.Apply(context))
                    {
                        activeSources.Add(rule.SourceId);
                        stateStore.Set(character, new BuffStateEntry(systemId, rule.SourceId, rule.EffectId, rule.Strength));
                    }
                }

                foreach (BuffStateEntry entry in stateStore.GetBySystem(character, systemId))
                {
                    if (activeSources.Contains(entry.SourceId))
                    {
                        continue;
                    }

                    removeEffect.Apply(new BuffBlackboard(character, "OnRemove", systemId, null, null, -1, 0.0f, entry.ToRule()));
                    stateStore.Remove(character, entry.SourceId);
                }
            }

            public void ClearAll()
            {
                foreach (BuffStateSnapshot snapshot in stateStore.GetAll())
                {
                    removeEffect.Apply(new BuffBlackboard(snapshot.Character, "OnDispose", snapshot.Entry.SystemId, null, null, -1, 0.0f, snapshot.Entry.ToRule()));
                }
            }
        }

        private sealed class ApplyAfflictionBuffEffect : IBuffEffect
        {
            private readonly ElysianBuffGameApi api;

            public ApplyAfflictionBuffEffect(ElysianBuffGameApi api)
            {
                this.api = api;
            }

            public bool Apply(BuffBlackboard blackboard)
            {
                BuffRule rule = blackboard == null ? null : blackboard.Rule;
                return rule != null && api.ApplyAffliction(blackboard.Character, rule.EffectId, rule.Strength);
            }
        }

        private sealed class RemoveAfflictionBuffEffect : IBuffEffect
        {
            private readonly ElysianBuffGameApi api;

            public RemoveAfflictionBuffEffect(ElysianBuffGameApi api)
            {
                this.api = api;
            }

            public bool Apply(BuffBlackboard blackboard)
            {
                BuffRule rule = blackboard == null ? null : blackboard.Rule;
                return rule != null && api.ReduceAffliction(blackboard.Character, rule.EffectId, 1000.0f);
            }
        }

        private interface IBuffTrigger
        {
            BuffTriggerResult Evaluate(Character character, float deltaTime);
        }

        private interface IBuffCondition
        {
            bool Supports(BuffBlackboard blackboard);
            bool IsMet(BuffBlackboard blackboard);
        }

        private interface IBuffEffect
        {
            bool Apply(BuffBlackboard blackboard);
        }

        private class BuffRule
        {
            public readonly string SourceId;
            public readonly string EffectId;
            public readonly float Strength;

            public BuffRule(string sourceId, string effectId, float strength)
            {
                SourceId = sourceId;
                EffectId = effectId;
                Strength = strength;
            }
        }

        private sealed class StigmataBuffRule : BuffRule
        {
            public readonly string ItemIdentifier;
            public readonly int SlotIndex;

            public StigmataBuffRule(string itemIdentifier, int slotIndex, string sourceId, string effectId, float strength)
                : base(sourceId, effectId, strength)
            {
                ItemIdentifier = itemIdentifier;
                SlotIndex = Math.Max(0, slotIndex);
            }
        }

        private sealed class StigmataRuleSet
        {
            public readonly List<StigmataBuffRule> Rules = new List<StigmataBuffRule>();
            public float RefreshInterval = 0.5f;

            public StigmataBuffRule FindByItem(Item item)
            {
                foreach (StigmataBuffRule rule in Rules)
                {
                    if (ReflectionInventory.HasIdentifier(item, rule.ItemIdentifier))
                    {
                        return rule;
                    }
                }

                return null;
            }
        }

        private sealed class TalentAfflictionRule : BuffRule
        {
            public readonly string ConditionAfflictionId;
            public readonly float MinStrength;

            public TalentAfflictionRule(string conditionAfflictionId, string sourceId, string effectId, float strength, float minStrength)
                : base(sourceId, effectId, strength)
            {
                ConditionAfflictionId = conditionAfflictionId;
                MinStrength = Math.Max(0.0f, minStrength);
            }
        }

        private sealed class TalentAfflictionRuleSet
        {
            public readonly List<TalentAfflictionRule> Rules = new List<TalentAfflictionRule>();
            public float RefreshInterval = 0.5f;
        }

        private sealed class BuffBlackboard
        {
            public readonly Character Character;
            public readonly string TriggerId;
            public readonly string SystemId;
            public readonly Item SourceItem;
            public readonly Item ContainedItem;
            public readonly int SlotIndex;
            public readonly float DeltaTime;
            public readonly BuffRule Rule;

            public BuffBlackboard(Character character, string triggerId, string systemId, Item sourceItem, Item containedItem, int slotIndex, float deltaTime, BuffRule rule)
            {
                Character = character;
                TriggerId = triggerId;
                SystemId = systemId;
                SourceItem = sourceItem;
                ContainedItem = containedItem;
                SlotIndex = slotIndex;
                DeltaTime = Math.Max(0.0f, deltaTime);
                Rule = rule;
            }
        }

        private sealed class BuffTriggerResult
        {
            public readonly string SystemId;
            public readonly bool ShouldArbitrate;
            public readonly List<BuffBlackboard> Contexts;

            private BuffTriggerResult(string systemId, bool shouldArbitrate, List<BuffBlackboard> contexts)
            {
                SystemId = systemId;
                ShouldArbitrate = shouldArbitrate;
                Contexts = contexts ?? new List<BuffBlackboard>();
            }

            public static BuffTriggerResult Skip(string systemId)
            {
                return new BuffTriggerResult(systemId, false, null);
            }

            public static BuffTriggerResult Apply(string systemId, List<BuffBlackboard> contexts)
            {
                return new BuffTriggerResult(systemId, true, contexts);
            }
        }

        private sealed class BuffStateStore
        {
            private readonly Dictionary<Character, Dictionary<string, BuffStateEntry>> entries = new Dictionary<Character, Dictionary<string, BuffStateEntry>>();

            public BuffStateEntry Get(Character character, string sourceId)
            {
                Dictionary<string, BuffStateEntry> characterEntries;
                if (character == null || string.IsNullOrWhiteSpace(sourceId) || !entries.TryGetValue(character, out characterEntries))
                {
                    return null;
                }

                BuffStateEntry entry;
                return characterEntries.TryGetValue(sourceId, out entry) ? entry : null;
            }

            public void Set(Character character, BuffStateEntry entry)
            {
                if (character == null || entry == null || string.IsNullOrWhiteSpace(entry.SourceId))
                {
                    return;
                }

                Dictionary<string, BuffStateEntry> characterEntries;
                if (!entries.TryGetValue(character, out characterEntries))
                {
                    characterEntries = new Dictionary<string, BuffStateEntry>();
                    entries[character] = characterEntries;
                }

                characterEntries[entry.SourceId] = entry;
            }

            public void Remove(Character character, string sourceId)
            {
                Dictionary<string, BuffStateEntry> characterEntries;
                if (character == null || string.IsNullOrWhiteSpace(sourceId) || !entries.TryGetValue(character, out characterEntries))
                {
                    return;
                }

                characterEntries.Remove(sourceId);
                if (characterEntries.Count == 0)
                {
                    entries.Remove(character);
                }
            }

            public List<BuffStateEntry> GetBySystem(Character character, string systemId)
            {
                List<BuffStateEntry> results = new List<BuffStateEntry>();
                Dictionary<string, BuffStateEntry> characterEntries;
                if (character == null || !entries.TryGetValue(character, out characterEntries))
                {
                    return results;
                }

                foreach (BuffStateEntry entry in characterEntries.Values)
                {
                    if (string.Equals(entry.SystemId, systemId, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(entry);
                    }
                }

                return results;
            }

            public List<BuffStateSnapshot> GetAll()
            {
                List<BuffStateSnapshot> results = new List<BuffStateSnapshot>();
                foreach (KeyValuePair<Character, Dictionary<string, BuffStateEntry>> characterPair in entries)
                {
                    foreach (BuffStateEntry entry in characterPair.Value.Values)
                    {
                        results.Add(new BuffStateSnapshot(characterPair.Key, entry));
                    }
                }

                return results;
            }

            public void Clear()
            {
                entries.Clear();
            }
        }

        private sealed class BuffStateEntry
        {
            public readonly string SystemId;
            public readonly string SourceId;
            public readonly string EffectId;
            public readonly float Strength;

            public BuffStateEntry(string systemId, string sourceId, string effectId, float strength)
            {
                SystemId = systemId;
                SourceId = sourceId;
                EffectId = effectId;
                Strength = strength;
            }

            public BuffRule ToRule()
            {
                return new BuffRule(SourceId, EffectId, Strength);
            }
        }

        private sealed class BuffStateSnapshot
        {
            public readonly Character Character;
            public readonly BuffStateEntry Entry;

            public BuffStateSnapshot(Character character, BuffStateEntry entry)
            {
                Character = character;
                Entry = entry;
            }
        }

        private static class ReflectionInventory
        {
            public static object GetInventory(object owner)
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

            public static IEnumerable<Item> EnumerateInventoryItems(object inventory)
            {
                foreach (string memberName in new[] { "AllItems", "Items" })
                {
                    foreach (Item item in EnumerateItems(GetMemberValue(inventory, memberName)))
                    {
                        yield return item;
                    }
                }
            }

            public static IEnumerable<Item> EnumerateDirectInventoryItems(object inventory)
            {
                foreach (Item item in EnumerateItems(GetMemberValue(inventory, "Items")))
                {
                    yield return item;
                }
            }

            public static int FindInventoryItemIndex(object inventory, Item item)
            {
                if (inventory == null || item == null)
                {
                    return -1;
                }

                foreach (MethodInfo method in inventory.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, "FindIndex", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(item))
                    {
                        continue;
                    }

                    try
                    {
                        object result = method.Invoke(inventory, new object[] { item });
                        if (result is int)
                        {
                            return (int)result;
                        }
                    }
                    catch
                    {
                    }
                }

                return FindInventoryItemIndexFromItems(GetMemberValue(inventory, "Items"), item);
            }

            public static bool HasIdentifier(Item item, string identifier)
            {
                if (item == null || string.IsNullOrWhiteSpace(identifier))
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

            private static int FindInventoryItemIndexFromItems(object value, Item targetItem)
            {
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable == null)
                {
                    return -1;
                }

                int index = 0;
                foreach (object entry in enumerable)
                {
                    Item item = entry as Item;
                    if (ReferenceEquals(item, targetItem))
                    {
                        return index;
                    }

                    IEnumerable nested = entry as IEnumerable;
                    if (nested != null && !(entry is string))
                    {
                        foreach (object nestedEntry in nested)
                        {
                            Item nestedItem = nestedEntry as Item;
                            if (ReferenceEquals(nestedItem, targetItem))
                            {
                                return index;
                            }
                        }
                    }

                    index++;
                }

                return -1;
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
        }
    }

    internal sealed class ElysianBuffGameApi
    {
        private readonly Func<Character, string, float, bool> applyAffliction;
        private readonly Func<Character, string, float, bool> reduceAffliction;
        private readonly Func<Character, string, float> getAfflictionStrength;
        private readonly Action<string> log;
        private readonly Action<string, string> logOnce;

        public ElysianBuffGameApi(
            Func<Character, string, float, bool> applyAffliction,
            Func<Character, string, float, bool> reduceAffliction,
            Func<Character, string, float> getAfflictionStrength,
            Action<string> log,
            Action<string, string> logOnce)
        {
            this.applyAffliction = applyAffliction;
            this.reduceAffliction = reduceAffliction;
            this.getAfflictionStrength = getAfflictionStrength;
            this.log = log;
            this.logOnce = logOnce;
        }

        public bool ApplyAffliction(Character character, string identifier, float strength)
        {
            return character != null &&
                   applyAffliction != null &&
                   !string.IsNullOrWhiteSpace(identifier) &&
                   applyAffliction(character, identifier, strength);
        }

        public bool ReduceAffliction(Character character, string identifier, float strength)
        {
            return character != null &&
                   reduceAffliction != null &&
                   !string.IsNullOrWhiteSpace(identifier) &&
                   reduceAffliction(character, identifier, strength);
        }

        public float GetAfflictionStrength(Character character, string identifier)
        {
            if (character == null || getAfflictionStrength == null || string.IsNullOrWhiteSpace(identifier))
            {
                return 0.0f;
            }

            return Math.Max(0.0f, getAfflictionStrength(character, identifier));
        }

        public void Log(string message)
        {
            if (log != null && !string.IsNullOrWhiteSpace(message))
            {
                log(message);
            }
        }

        public void LogOnce(string key, string message)
        {
            if (logOnce != null)
            {
                logOnce(key, message);
                return;
            }

            Log(message);
        }
    }
}
