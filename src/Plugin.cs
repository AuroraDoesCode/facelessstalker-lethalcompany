using BepInEx;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using static LethalLib.Modules.Levels;
using static LethalLib.Modules.Enemies;
using BepInEx.Logging;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;
using SlendermanMod.Behaviours;
using System.Collections.Generic;
using System.Linq;

namespace SlendermanMod
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        const string GUID = "sparble.slendermanmod";
        const string NAME = "SlendermanMod";
        const string VERSION = "1.2.5";
        public static Harmony _harmony;
        public static EnemyType SlendermanEnemy;
        //public static Item pageItem;
        public static Item page1Item;
        public static Item page2Item;
        public static Item page3Item;
        public static Item page4Item;
        public static Item page5Item;
        public static Item page6Item;
        public static Item page7Item;
        public static Item page8Item;
        internal static new ManualLogSource Logger;
        public static AssetBundle SlendermanAssets;
        public static SlendermanConfig SlendermanConfig { get; internal set; }
        public static float slendermanHauntCooldown;
        public static float slendermanHauntIntervalLength;
        public static float slendermanStalkingIntervalLength;
        public static float slendermanChaseDuration;
        public static bool slendermanFlipsLightBreaker;
        public static bool slendermanPlaysSpawningSound;
        public static bool slendermanPlaysApproachingSound;
        public static float slendermanApproachingSoundChance;
        public static float slendermanSpotFoundSoundChance;
        public static bool slendermanClosedDoorsTargetSwitch;
        public static float slendermanVolume;

        // Preparing the mod for patching, UnityNetcodePatcher
        // Required by https://github.com/EvaisaDev/UnityNetcodePatcher
        private static void NetcodePatcher()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        private void Awake()
        {
            Logger = base.Logger;
            Assets.PopulateAssets();

            // Creating Config
            SlendermanConfig = new SlendermanConfig(Config);

            NetcodePatcher(); // ONLY RUN ONCE

            // Load SlendermanEnemy
            SlendermanEnemy = Assets.SlendermanAssets.LoadAsset<EnemyType>("SlendermanEnemy");
            var slendermanTerminalNode = Assets.SlendermanAssets.LoadAsset<TerminalNode>("SlendermanEnemyTN");

            // Network Prefabs need to be registered first. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            //DawnLib.RegisterNetworkPrefab(SlendermanEnemy.enemyPrefab);
            //DawnLib.DefineEnemy(SlendermanModKeys.Slenderman, SlendermanEnemy, builder => { builder.DefineInside(builder => { builder.SetWeights(builder => { builder.SetGlobalWeight(SlendermanConfig.configSlendermanSpawnChances.Value); }); }); });
            NetworkPrefabs.RegisterNetworkPrefab(SlendermanEnemy.enemyPrefab);
            RegisterEnemy(SlendermanEnemy, SlendermanConfig.Instance.configSlendermanSpawnChances.Value, LevelTypes.All, SpawnType.Default, slendermanTerminalNode);
            // Loading the page items
            string configMoonRarity = SlendermanConfig.configPageMoons.Value;
            (Dictionary<LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) = ConfigParsing(configMoonRarity);

           // Item pageItem = Assets.SlendermanAssets.LoadAsset<Item>("Assets/Items/pageItem.asset");
           // SpawnSlendermanEnemyItem pageScript = pageItem.spawnPrefab.AddComponent<SpawnSlendermanEnemyItem>(); // Add custom interactions to pageItem
           // pageScript.grabbable = true;
           // pageScript.grabbableToEnemies = true;
           // pageScript.itemProperties = pageItem;
           // Utilities.FixMixerGroups(pageItem.spawnPrefab); //Fixes empty audio -- mixer lethallib.modules.
           // NetworkPrefabs.RegisterNetworkPrefab(pageItem.spawnPrefab);
            //Items.RegisterScrap(pageItem, spawnRateByLevelType, spawnRateByCustomLevelType);

            Item pageItem = Assets.SlendermanAssets.LoadAsset<Item>("Assets/slender/Items/pageItem.asset");
            SpawnSlendermanEnemyItem pageScript = pageItem.spawnPrefab.AddComponent<SpawnSlendermanEnemyItem>(); // Add custom interactions to pageItem
            pageScript.grabbable = true;
            pageScript.grabbableToEnemies = true;
            pageScript.itemProperties = pageItem;
            Utilities.FixMixerGroups(pageItem.spawnPrefab); //Fixes empty audio -- mixer lethallib.modules.
            NetworkPrefabs.RegisterNetworkPrefab(pageItem.spawnPrefab);
            Items.RegisterScrap(pageItem, spawnRateByLevelType, spawnRateByCustomLevelType);

            slendermanHauntCooldown = SlendermanConfig.Instance.configSlendermanHauntCooldown.Value;
            slendermanHauntIntervalLength = SlendermanConfig.Instance.configSlendermanAbsentIntervalRate.Value;
            slendermanStalkingIntervalLength = SlendermanConfig.Instance.configSlendermanStalkingIntervalRate.Value;
            slendermanChaseDuration = SlendermanConfig.Instance.configSlendermanChaseDuration.Value;
            slendermanFlipsLightBreaker = SlendermanConfig.Instance.configSlendermanLightBreaker.Value;
            slendermanVolume = SlendermanConfig.Instance.configSlendermanVolume.Value;
            slendermanPlaysSpawningSound = SlendermanConfig.Instance.configSlendermanPlaysSpawnSound.Value;
            slendermanPlaysApproachingSound = SlendermanConfig.Instance.configSlendermanPlaysApproachingSound.Value;
            slendermanApproachingSoundChance = SlendermanConfig.Instance.configSlendermanApproachingSoundChance.Value;
            slendermanSpotFoundSoundChance = SlendermanConfig.Instance.configSlendermanSpotFoundSoundChance.Value;
            slendermanClosedDoorsTargetSwitch = SlendermanConfig.Instance.configSlendermanDoorsClosedTargetSwitch.Value;

            Logger.LogInfo($"Plugin FacelessStalker is loaded!");
        }
        private (Dictionary<LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType)
        ConfigParsing(string configMoonRarity)
        {
            Dictionary<LevelTypes, int> spawnRateByLevelType = new Dictionary<LevelTypes, int>();
            Dictionary<string, int> spawnRateByCustomLevelType = new Dictionary<string, int>();
            foreach (string entry in configMoonRarity.Split(',').Select(s => s.Trim()))
            {
                string[] entryParts = entry.Split('@');

                if (entryParts.Length != 2)
                {
                    continue;
                }

                string name = entryParts[0];
                int spawnrate;

                if (!int.TryParse(entryParts[1], out spawnrate))
                {
                    continue;
                }

                if (System.Enum.TryParse<LevelTypes>(name, true, out LevelTypes levelType))
                {
                    spawnRateByLevelType[levelType] = spawnrate;
                    Plugin.Logger.LogInfo($"Registered spawn rate for level type {levelType} to {spawnrate}");
                }
                else
                {
                    spawnRateByCustomLevelType[name] = spawnrate;
                    Plugin.Logger.LogInfo($"Registered spawn rate for custom level type {name} to {spawnrate}");
                }
            }
            return (spawnRateByLevelType, spawnRateByCustomLevelType);
        }
    }

    public static class Assets
    {
        public static AssetBundle SlendermanAssets = null;
        public static void PopulateAssets()
        {
            string sAssemblyLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "slendermanbundle");
            
            SlendermanAssets = AssetBundle.LoadFromFile(sAssemblyLocation);
            if (SlendermanAssets == null)
            {
                Plugin.Logger.LogError("Failed to load slendermanbundle assets.");
                return;
            }
        }
    }

    [System.Serializable]
    public class SlendermanConfig : SyncedInstance<SlendermanConfig>
    {
        public readonly ConfigEntry<string> configPageMoons;
        public readonly ConfigEntry<int> configSlendermanSpawnChances;
        public readonly ConfigEntry<float> configSlendermanHauntCooldown;
        public readonly ConfigEntry<float> configSlendermanAbsentIntervalRate;
        public readonly ConfigEntry<float> configSlendermanStalkingIntervalRate;
        public readonly ConfigEntry<float> configSlendermanChaseDuration;
        public readonly ConfigEntry<float> configSlendermanVolume;
        public readonly ConfigEntry<bool> configSlendermanLightBreaker;
        public readonly ConfigEntry<bool> configSlendermanPlaysSpawnSound;
        public readonly ConfigEntry<bool> configSlendermanPlaysApproachingSound;
        public readonly ConfigEntry<float> configSlendermanApproachingSoundChance;
        public readonly ConfigEntry<float> configSlendermanSpotFoundSoundChance;
        public readonly ConfigEntry<bool> configSlendermanDoorsClosedTargetSwitch;
        public SlendermanConfig(ConfigFile cfg)
        {
            InitInstance(this);
            configPageMoons = cfg.Bind(
                "PageItem.Values",
                "Page Spawning",
                "All@2,AssuranceLevel@1,VowLevel@10,MarchLevel@10,DineLevel@7,RendLevel@8,TitanLevel@5,AdamanceLevel@10,ArtificeLevel@7,Gloom@10,Auralis@10,Junic@9,Acidir@10,Cosmocos@10,Polarus@1,Desolation@10,Embrion@10,Affliction@10,Eve@10,Gratar@7,Infernis@6,Harloth@7,Asteroid@4,Celest@10,Siabudabu@9,Ganimedes@7,Kast@7,Secret Labs@11,Solace@10,Synthesis@10,PsychSanctum@10,Sanguine@10,Harloth@4",
                "The LevelTypes/Moons that the page items can spawn on. \nUses the format 'Moon@Rarity,Moon2@Rarity2,' etc, also supports custom moons. \nThe spawn rarity counts for every single one of the eight pages."
            );
            configSlendermanSpawnChances = cfg.Bind(
                    "Slenderman",
                    "Natural Spawn Chances",
                    0,
                    "The chances of spawning Slenderman, independent from any pages. \nDefault value is 0, which will cause him to never spawn naturally and only spawn him when a page is picked up."    // Description
            );
            configSlendermanHauntCooldown = cfg.Bind(
                    "Slenderman.Behavior",
                    "Absent State Cooldown",
                    35.0f,
                    "The duration of the cooldown after first picking up a page, looking at slenderman and after a chase before Slenderman tries to spawn again (in seconds). \nThe higher the value, the more time the player has before Slenderman spawns in."    // Description
            );
            configSlendermanAbsentIntervalRate = cfg.Bind(
                    "Slenderman.Behavior",
                    "Absent State Interval Length",
                    15.0f,
                    "The duration of intervals of the Slenderman trying to find a haunting spot during his Absent state (in seconds). If he fails to find a spot, he will wait x seconds again before trying to find a spot again. \nThe higher the value, the longer the intervals are and the longer it will take for him to try to re-spawn. \nHigh numbers may result in him almost never spawning at all on small moons without many outside objects. (0 = Very frequent spawn intervals, 60 = Wait 60 seconds before trying to find a spawning spot again)."    // Description
            );
            configSlendermanStalkingIntervalRate = cfg.Bind(
                    "Slenderman.Behavior",
                    "Stalking State Interval Length",
                    20.0f,
                    "The duration of intervals (in seconds) of the Slenderman stalking the player before creeping closer - The value MUST be higher than 4.0 and it is recommended to not set this number too high or low. \nThe higher the value, the longer the intervals are and the longer it takes him to creep closer to the haunted player. \nHigh numbers mean the player has much time to spot Slenderman, while very low numbers mean they will have to constantly check their surroundings. (4.5 = Pretty much immediate chase, 60 = Wait 60 seconds before creeping closer)."    // Description
            );
            configSlendermanChaseDuration = cfg.Bind(
                    "Slenderman.Behavior",
                    "Chase Duration",
                    20.0f,
                    "The duration of a chase (in seconds)."
            );
            configSlendermanLightBreaker = cfg.Bind(
                    "Slenderman.Behavior",
                    "Light Breaker Flip",
                    false,
                    "Whether Slenderman should shut off the facility lights after being seen the first time."
            );
            configSlendermanPlaysSpawnSound = cfg.Bind(
                    "Slenderman.Behavior",
                    "Play Spawning Sound",
                    true,
                    "Whether the global spawning sound should be played after Slenderman spawned in the round (both naturally and through picking up a page)."
            );
            configSlendermanPlaysApproachingSound = cfg.Bind(
                    "Slenderman.Behavior",
                    "Play Approaching Sound",
                    true,
                    "Whether the approaching sound should be enabled for the haunted player."
            );
            configSlendermanApproachingSoundChance = cfg.Bind(
                    "Slenderman.Behavior",
                    "Approaching Sound Chance",
                    25.0f,
                    "(Requires the 'Play Approaching Sound' option to be set to 'true') - The chance of Slenderman playing a static noise for the haunted player when creeping closer in % (must be between 0 and 100) - 0 means no sound will be played at all, 100 means for every interval he will play the sound 100%."
            );
            configSlendermanSpotFoundSoundChance = cfg.Bind(
                    "Slenderman.Behavior",
                    "Play Spot Found-Sound",
                    50.0f,
                    "Chance for the bass sound playing after Slenderman found a spot to spawn in. Value in %, must be between 0 and 100 (0 = Never play the sound, 100 = always play the sound)."
            );
            configSlendermanDoorsClosedTargetSwitch = cfg.Bind(
                    "Slenderman.Behavior",
                    "Closed Doors Target Switch",
                    true,
                    "Whether Slenderman should change targets when the haunted player is inside the ship and the doors are closed during a chase."
            );
            configSlendermanVolume = cfg.Bind(
                    "Slenderman.Volume",
                    "Slenderman Volume",
                    1.0f,
                    "The volume of the Slenderman enemy voice. 1.0 means 100% (default value), 0.8 means 80% etc."
            );
        }

        public static void RequestSync()
        {
            if (!IsClient) return;

            using FastBufferWriter stream = new(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage("SlenderMod_OnRequestConfigSync", 0uL, stream);
        }

        public static void OnRequestSync(ulong clientId, FastBufferReader _)
        {
            if (!IsHost) return;

            Plugin.Logger.LogInfo($"Config sync request received from client: {clientId}");

            byte[] array = SerializeToBytes(Instance);
            int value = array.Length;

            using FastBufferWriter stream = new(value + IntSize, Allocator.Temp);

            try
            {
                stream.WriteValueSafe(in value, default);
                stream.WriteBytesSafe(array);

                MessageManager.SendNamedMessage("SlenderMod_OnReceiveConfigSync", clientId, stream);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogInfo($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        public static void OnReceiveSync(ulong _, FastBufferReader reader)
        {
            if (!reader.TryBeginRead(IntSize))
            {
                Plugin.Logger.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int val, default);
            if (!reader.TryBeginRead(val))
            {
                Plugin.Logger.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[val];
            reader.ReadBytesSafe(ref data, val);

            SyncInstance(data);

            Plugin.Logger.LogInfo("Successfully synced config with host.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer()
        {
            if (IsHost)
            {
                MessageManager.RegisterNamedMessageHandler("SlenderMod_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler("SlenderMod_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave()
        {
            SlendermanConfig.RevertSync();
        }
    }
}
