using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static BaseEntity;

namespace Oxide.Plugins
{
    [Info("Gather Rates", "WhiteThunder", "0.1.1")]
    [Description("Allows altering gather rates based on player permission.")]
    internal class GatherRates : CovalencePlugin
    {
        #region Fields

        private static GatherRates _pluginInstance;
        private static Configuration _pluginConfig;

        private const string PermissionRulesetFormat = "gatherrates.ruleset.{0}";

        private StoredData _pluginData;

        private object False = false;

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginInstance = this;
            _pluginData = StoredData.Load();

            foreach (var ruleset in _pluginConfig.GatherRateRulesets)
                permission.RegisterPermission(GetRulesetPermission(ruleset.Name), this);
        }

        private void Unload()
        {
            OnServerSave();
            _pluginConfig = null;
            _pluginInstance = null;
        }

        private void OnServerInitialized()
        {
            _pluginConfig.Validate();
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

            var ruleset = GetPlayerRuleset(player.UserIDString);
            if (ruleset == null)
                return null;

            foreach (var itemAmount in entity.itemList)
            {
                var item = ItemManager.Create(itemAmount.itemDef, (int)itemAmount.amount);
                if (item == null)
                    continue;

                var rate = ruleset.GetGatherRate(item.info, entity.ShortPrefabName);
                if (rate != 1 && !MultiplyGatherRateWasBlocked(entity, item, player.UserIDString))
                {
                    item.amount = (int)(item.amount * rate);
                }

                if (item.amount <= 0)
                {
                    item.Remove();
                    continue;
                }

                if (player != null)
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
                randomItemDispenser.DistributeItems(player, entity.transform.position);
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
                userId = quarry.OwnerID.ToString();

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

        private static bool MultiplyGatherRateWasBlocked(BaseEntity dispenser, Item item, string userId)
        {
            object hookResult = Interface.CallHook("OnGatherRateMultiply", dispenser, item, userId);
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
            if (rate == 1 || MultiplyGatherRateWasBlocked(entity, item, userId))
                return;

            item.amount = (int)(item.amount * rate);
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
                Interface.Oxide.DataFileSystem.ReadObject<StoredData>(_pluginInstance.Name) ?? new StoredData();

            public static StoredData Clear() => new StoredData().Save();

            public StoredData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(_pluginInstance.Name, this);
                return this;
            }
        }

        #endregion

        #region Configuration

        private GatherRateRuleset GetPlayerRuleset(string userId)
        {
            var rulesets = _pluginConfig.GatherRateRulesets;

            if (userId == string.Empty || rulesets == null)
                return null;

            for (var i = rulesets.Length - 1; i >= 0; i--)
            {
                var ruleset = rulesets[i];
                if (!string.IsNullOrEmpty(ruleset.Name)
                    && permission.UserHasPermission(userId, GetRulesetPermission(ruleset.Name)))
                    return ruleset;
            }

            return null;
        }

        private class Configuration : SerializableConfiguration
        {
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

            public void Validate()
            {
                var dispensers = GetValidResourceDispensers();

                if (GatherRateRulesets == null)
                    return;

                foreach (var ruleset in GatherRateRulesets)
                {
                    if (ruleset.ItemRateOverrides != null)
                    {
                        foreach (var itemShortName in ruleset.ItemRateOverrides.Keys)
                        {
                            if (ItemManager.FindItemDefinition(itemShortName) == null)
                            {
                                _pluginInstance.LogWarning($"Invalid item short name in config: '{itemShortName}'");
                            }
                        }
                    }

                    if (ruleset.DispenserRateOverrides != null)
                    {
                        foreach (var entry in ruleset.DispenserRateOverrides)
                        {
                            var entityShortName = entry.Key;
                            if (!dispensers.Contains(entry.Key))
                            {
                                _pluginInstance.LogWarning($"Invalid entity short name in config: '{entityShortName}'");
                                continue;
                            }

                            var itemRates = entry.Value;
                            foreach (var itemShortName in itemRates.Keys)
                            {
                                if (ItemManager.FindItemDefinition(itemShortName) == null)
                                {
                                    _pluginInstance.LogWarning($"Invalid item short name in config: '{itemShortName}'");
                                }
                            }
                        }
                    }
                }
            }
        }

        private class GatherRateRuleset
        {
            [JsonProperty("Name")]
            public string Name;

            [JsonProperty("DefaultRate")]
            public float DefaultRate = 1;

            [JsonProperty("ItemRateOverrides", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Dictionary<string, float> ItemRateOverrides = null;

            [JsonProperty("DispenserRateOverrides", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Dictionary<string, Dictionary<string, float>> DispenserRateOverrides = null;

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

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_pluginConfig, true);
        }

        #endregion
    }
}
