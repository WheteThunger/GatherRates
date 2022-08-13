using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static BaseEntity;
using static RandomItemDispenser;

namespace Oxide.Plugins
{
    [Info("Gather Rates", "WhiteThunder", "0.2.0")]
    [Description("Allows altering gather rates based on player permission.")]
    internal class GatherRates : CovalencePlugin
    {
        #region Fields

        private Configuration _config;

        private const string PermissionRulesetFormat = "gatherrates.ruleset.{0}";

        private StoredData _pluginData;

        private object False = false;

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginData = StoredData.Load();
            _config.Init(this);
        }

        private void Unload()
        {
            OnServerSave();
        }

        private void OnServerInitialized()
        {
            _config.OnServerInitialized(this);
        }

        private void OnServerSave()
        {
            _pluginData.Save();
        }

        private void OnNewSave()
        {
            _pluginData = StoredData.Clear();
        }

        private void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
        {
            ProcessGather(growable, item, player);

            if (item.amount < 1)
            {
                NextTick(() =>
                {
                    if (item.amount < 1)
                    {
                        item.RemoveFromContainer();
                        item.Remove();
                    }
                });
            }
        }

        private object OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            ProcessGather(dispenser.baseEntity, item, player);
            if (item.amount < 1)
                return False;

            return null;
        }

        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            ProcessGather(dispenser.baseEntity, item, player);

            if (item.amount < 1)
            {
                NextTick(() =>
                {
                    if (item.amount < 1)
                    {
                        item.RemoveFromContainer();
                        item.Remove();
                    }
                });
            }
        }

        private object OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)
        {
            if (entity.IsDestroyed)
            {
                // Another plugin probably handled this hook.
                return null;
            }

            var ruleset = GetPlayerRuleset(player?.UserIDString);
            if (ruleset == null)
                return null;

            foreach (var itemAmount in entity.itemList)
            {
                var item = ItemManager.Create(itemAmount.itemDef, (int)itemAmount.amount);
                if (item == null)
                    continue;

                var rate = ruleset.GetGatherRate(item.info, entity.ShortPrefabName);
                if (rate != 1 && !MultiplyGatherRateWasBlocked(entity, item.info, player?.UserIDString))
                {
                    item.amount = (int)(item.amount * rate);
                }

                if (item.amount <= 0)
                {
                    item.Remove();
                    continue;
                }

                if ((object)player != null)
                {
                    player.GiveItem(item, GiveItemReason.ResourceHarvested);
                }
                else
                {
                    item.Drop(entity.transform.position + Vector3.up * 0.5f, Vector3.up);
                }
            }

            entity.itemList = null;

            if (entity.pickupEffect.isValid)
            {
                Effect.server.Run(entity.pickupEffect.resourcePath, entity.transform.position, entity.transform.up);
            }

            RandomItemDispenser randomItemDispenser = PrefabAttribute.server.Find<RandomItemDispenser>(entity.prefabID);
            if (randomItemDispenser != null)
            {
                DistributeRandomItems(entity, randomItemDispenser, player, entity.transform.position, ruleset);
            }

            entity.Kill();
            return False;
        }

        private object OnQuarryGather(MiningQuarry quarry, Item item)
        {
            string userId;
            if (quarry.OwnerID == 0)
            {
                if (!_pluginData.QuarryStarters.TryGetValue(quarry.net.ID, out userId))
                    return null;
            }
            else
            {
                userId = quarry.OwnerID.ToString();
            }

            ProcessGather(quarry, item, userId);
            if (item.amount < 1)
            {
                // The hook is already coded to remove the item if returning non-null.
                return False;
            }

            return null;
        }

        private object OnExcavatorGather(ExcavatorArm excavator, Item item)
        {
            string userId;
            if (!_pluginData.ExcavatorStarters.TryGetValue(excavator.net.ID, out userId))
                return null;

            ProcessGather(excavator, item, userId);
            if (item.amount < 1)
            {
                item.Remove();
                return False;
            }

            return null;
        }

        private void OnQuarryToggled(MiningQuarry miningQuarry, BasePlayer player)
        {
            if (!miningQuarry.IsOn())
                return;

            _pluginData.QuarryStarters[miningQuarry.net.ID] = player.UserIDString;
        }

        private void OnExcavatorResourceSet(ExcavatorArm excavatorArm, string resourceName, BasePlayer player)
        {
            if (excavatorArm.IsOn())
                return;

            _pluginData.ExcavatorStarters[excavatorArm.net.ID] = player.UserIDString;
        }

        #endregion

        #region Commands

        [Command("gatherrates.listdispensers")]
        private void CommandDispensers(IPlayer player)
        {
            if (!player.IsServer && !player.IsAdmin)
                return;

            var dispenserShortNames = GetValidResourceDispensers();
            var sb = new StringBuilder();

            foreach (var shortName in dispenserShortNames)
                sb.AppendLine(shortName);

            player.Reply(sb.ToString());
        }

        #endregion

        #region Helper Methods

        private static bool MultiplyGatherRateWasBlocked(BaseEntity dispenser, ItemDefinition itemDefinition, string userId)
        {
            object hookResult = Interface.CallHook("OnGatherRateMultiply", dispenser, itemDefinition, userId);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static string GetRulesetPermission(string name) => string.Format(PermissionRulesetFormat, name);

        private static HashSet<string> GetValidResourceDispensers()
        {
            var validDispensers = new HashSet<string>();

            foreach (var prefabPath in GameManifest.Current.entities)
            {
                var entity = GameManager.server.FindPrefab(prefabPath.ToLower())?.GetComponent<BaseEntity>();

                if (entity == null || string.IsNullOrEmpty(entity.ShortPrefabName))
                    continue;

                var collectibleEntity = entity as CollectibleEntity;
                if (collectibleEntity != null)
                {
                    validDispensers.Add(collectibleEntity.ShortPrefabName);
                    continue;
                }

                var growableEntity = entity as GrowableEntity;
                if (growableEntity != null)
                {
                    validDispensers.Add(growableEntity.ShortPrefabName);
                    continue;
                }

                var excavatorArm = entity as ExcavatorArm;
                if (excavatorArm != null)
                {
                    validDispensers.Add(excavatorArm.ShortPrefabName);
                    continue;
                }

                var miningQuarry = entity as MiningQuarry;
                if (miningQuarry != null)
                {
                    validDispensers.Add(miningQuarry.ShortPrefabName);
                }

                var dispenser = entity.GetComponent<ResourceDispenser>();
                if (dispenser != null)
                {
                    validDispensers.Add(entity.ShortPrefabName);
                    continue;
                }
            }

            return validDispensers;
        }

        private void ProcessGather(BaseEntity entity, Item item, BasePlayer player) =>
            ProcessGather(entity, item, player.UserIDString);

        private void ProcessGather(BaseEntity entity, Item item, string userId)
        {
            var ruleset = GetPlayerRuleset(userId);
            if (ruleset == null)
                return;

            var rate = ruleset.GetGatherRate(item.info, entity.ShortPrefabName);
            if (rate == 1 || MultiplyGatherRateWasBlocked(entity, item.info, userId))
                return;

            item.amount = (int)(item.amount * rate);
        }

        private bool TryRandomAward(RandomItemDispenser randomItemDispenser, RandomItemChance itemChance, BasePlayer player, Vector3 distributorPosition, float rate)
        {
            if (Interface.CallHook("OnRandomItemAward", randomItemDispenser, itemChance, player, distributorPosition) != null)
                return false;

            if (UnityEngine.Random.Range(0f, 1f) > itemChance.Chance)
                return false;

            var amount = (int)(itemChance.Amount * rate);
            if (amount <= 0)
                return false;

            var item = ItemManager.Create(itemChance.Item, amount);
            if (item == null)
                return false;

            if ((object)player != null)
            {
                player.GiveItem(item, GiveItemReason.ResourceHarvested);
            }
            else
            {
                item.Drop(distributorPosition + Vector3.up * 0.5f, Vector3.up);
            }

            return true;
        }

        private void DistributeRandomItems(BaseEntity dispenser, RandomItemDispenser randomItemDispenser, BasePlayer player, Vector3 distributorPosition, GatherRateRuleset ruleset)
        {
            foreach (var itemChance in randomItemDispenser.Chances)
            {
                // Note: One minor problem is that if the hook was blocked, we don't know if the item was awarded, so extra bonus items may be awarded.
                if (MultiplyGatherRateWasBlocked(dispenser, itemChance.Item, player?.UserIDString))
                    continue;

                var rate = ruleset.GetGatherRate(itemChance.Item, dispenser.ShortPrefabName);

                bool flag = TryRandomAward(randomItemDispenser, itemChance, player, distributorPosition, rate);
                if (randomItemDispenser.OnlyAwardOne && flag)
                    break;
            }
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("QuarryStarters")]
            public Dictionary<uint, string> QuarryStarters = new Dictionary<uint, string>();

            [JsonProperty("ExcavatorStarters")]
            public Dictionary<uint, string> ExcavatorStarters = new Dictionary<uint, string>();

            public static StoredData Load() =>
                Interface.Oxide.DataFileSystem.ReadObject<StoredData>(nameof(GatherRates)) ?? new StoredData();

            public static StoredData Clear() => new StoredData().Save();

            public StoredData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(nameof(GatherRates), this);
                return this;
            }
        }

        #endregion

        #region Configuration

        private GatherRateRuleset GetPlayerRuleset(string userId)
        {
            if (userId == null)
                return _config.DefaultRuleset;

            var rulesets = _config.GatherRateRulesets;

            if (userId == string.Empty || rulesets == null)
                return null;

            for (var i = rulesets.Length - 1; i >= 0; i--)
            {
                var ruleset = rulesets[i];
                if (!string.IsNullOrEmpty(ruleset.Name)
                    && permission.UserHasPermission(userId, GetRulesetPermission(ruleset.Name)))
                    return ruleset;
            }

            return _config.DefaultRuleset;
        }

        private class GatherRateRuleset
        {
            [JsonProperty("Name", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string Name;

            [JsonProperty("DefaultRate")]
            public float DefaultRate = 1;

            [JsonProperty("ItemRateOverrides", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Dictionary<string, float> ItemRateOverrides = null;

            [JsonProperty("DispenserRateOverrides", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Dictionary<string, Dictionary<string, float>> DispenserRateOverrides = null;

            public void Init(GatherRates plugin)
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    plugin.permission.RegisterPermission(GetRulesetPermission(Name), plugin);
                }

                if (ItemRateOverrides != null)
                {
                    foreach (var itemShortName in ItemRateOverrides.Keys)
                    {
                        if (ItemManager.FindItemDefinition(itemShortName) == null)
                        {
                            plugin.LogWarning($"Invalid item short name in config: '{itemShortName}'");
                        }
                    }
                }

                if (DispenserRateOverrides != null)
                {
                    foreach (var entry in DispenserRateOverrides)
                    {
                        var itemRates = entry.Value;
                        foreach (var itemShortName in itemRates.Keys)
                        {
                            if (ItemManager.FindItemDefinition(itemShortName) == null)
                            {
                                plugin.LogWarning($"Invalid item short name in config: '{itemShortName}'");
                            }
                        }
                    }
                }
            }

            public void OnServerInitialized(GatherRates plugin, HashSet<string> validDispensers)
            {
                if (DispenserRateOverrides != null)
                {
                    foreach (var entry in DispenserRateOverrides)
                    {
                        var entityShortName = entry.Key;
                        if (!validDispensers.Contains(entry.Key))
                        {
                            plugin.LogWarning($"Invalid entity short name in config: '{entityShortName}'");
                        }
                    }
                }
            }

            public float GetGatherRate(ItemDefinition itemDefinition, string shortPrefabName)
            {
                Dictionary<string, float> itemRates;
                float rate;
                if (DispenserRateOverrides != null
                    && DispenserRateOverrides.TryGetValue(shortPrefabName, out itemRates)
                    && itemRates.TryGetValue(itemDefinition.shortname, out rate))
                    return rate;

                if (ItemRateOverrides != null
                    && ItemRateOverrides.TryGetValue(itemDefinition.shortname, out rate))
                    return rate;

                return DefaultRate;
            }
        }

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty("DefaultRuleset")]
            public GatherRateRuleset DefaultRuleset = new GatherRateRuleset();

            [JsonProperty("GatherRateRulesets")]
            public GatherRateRuleset[] GatherRateRulesets = new GatherRateRuleset[]
            {
                new GatherRateRuleset()
                {
                    Name = "2x",
                    DefaultRate = 2,
                },
                new GatherRateRuleset()
                {
                    Name = "5x",
                    DefaultRate = 5,
                },
                new GatherRateRuleset()
                {
                    Name = "10x",
                    DefaultRate = 10,
                },
                new GatherRateRuleset()
                {
                    Name = "100x",
                    DefaultRate = 100,
                },
                new GatherRateRuleset()
                {
                    Name = "1000x",
                    DefaultRate = 1000,
                }
            };

            public void Init(GatherRates plugin)
            {
                DefaultRuleset.Init(plugin);

                if (GatherRateRulesets != null)
                {
                    foreach (var ruleset in GatherRateRulesets)
                    {
                        ruleset.Init(plugin);
                    }
                }
            }

            public void OnServerInitialized(GatherRates plugin)
            {
                var validDispensers = GetValidResourceDispensers();

                DefaultRuleset.OnServerInitialized(plugin, validDispensers);

                if (GatherRateRulesets != null)
                {
                    foreach (var ruleset in GatherRateRulesets)
                    {
                        ruleset.OnServerInitialized(plugin, validDispensers);
                    }
                }
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Boilerplate

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion
    }
}
