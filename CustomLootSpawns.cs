/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Rust;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Custom Loot Spawns", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class CustomLootSpawns : RustPlugin
    {
        #region Fields

        private static CustomLootSpawns _plugin;
        private static Configuration _config;
        private System.Random _randomGenerator = new System.Random();

        private SpawnGroupData _spawnGroupDataBeingEdited;

        public const int LAYER_PLAYERS = Layers.Mask.Player_Server;
        public const int LAYER_ENTITIES = Layers.Mask.Construction | Layers.Mask.Deployed;
        public const int LAYER_GROUND = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Default;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
            };
        }

        #endregion Configuration

        #region Stored Data

        public class SpawnGroupData
        {
            [JsonProperty("Alias")]
            public string Alias { get; set; }

            [JsonProperty("Id")]
            public Guid Id { get; set; }

            [JsonProperty("Maximum Population")]
            public int MaximumPopulation { get; set; }

            [JsonProperty("Minimum Number To Spawn Per Tick")]
            public int MinimumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Maximum Number To Spawn Per Tick")]
            public int MaximumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Minimum Respawn Delay Seconds")]
            public float MinimumRespawnDelaySeconds { get; set; }

            [JsonProperty("Maximum Respawn Delay Seconds")]
            public float MaximumRespawnDelaySeconds { get; set; }

            [JsonProperty("Randomize Y Rotation")]
            public bool RandomizeYRotation { get; set; }

            [JsonProperty("Prefabs")]
            public List<PrefabData> Prefabs { get; set; } = new List<PrefabData>();

            [JsonProperty("Spawn Points")]
            public List<SpawnPointData> SpawnPoints { get; set; } = new List<SpawnPointData>();
        }

        public class PrefabData
        {
            [JsonProperty("Prefab")]
            public string Prefab { get; set; }

            [JsonProperty("Weight")]
            public int Weight { get; set; }
        }

        public class SpawnPointData
        {
            [JsonProperty("Id")]
            public string Id { get; set; }

            [JsonProperty("Position")]
            public Vector3 Position { get; set; }

            [JsonProperty("Rotation")]
            public Vector3 Rotation { get; set; }

            [JsonProperty("Radius")]
            public float Radius { get; set; }
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {

        }

        #endregion Oxide Hooks

        #region Spawner

        public class LootSpawnerComponent : FacepunchBehaviour
        {
            #region Fields

            public SpawnGroupData Data { get; set; }
            public List<SpawnPointComponent> SpawnPoints { get; set; } = new List<SpawnPointComponent>();
            public List<BaseEntity> SpawnedEntities { get; set; } = new List<BaseEntity>();
            public int CurrentPopulation
            {
                get
                {
                    return SpawnedEntities.Count(entity => entity != null);
                }
            }

            #endregion Fields

            #region Initialization

            public static LootSpawnerComponent Create(SpawnGroupData data)
            {
                LootSpawnerComponent spawner = new GameObject().AddComponent<LootSpawnerComponent>();
                spawner.Initialize(data);

                return spawner;
            }

            public void Initialize(SpawnGroupData data)
            {
                Data = data;

                foreach (SpawnPointData spawnPointData in Data.SpawnPoints)
                {
                    SpawnPointComponent.Create(spawnPointData, this);
                }
            }

            public void Destroy()
            {
                DestroyImmediate(this);
            }

            #endregion Initialization

            #region Component Lifecycle

            private void Start()
            {
               // InvokeRepeating(new Action(RespawnScientists), _npcSpawnerConfig.InitialSpawnDelaySeconds, _npcSpawnerConfig.RespawnDelayMinutes * 60f);
            }

            private void OnDestroy()
            {
                for (int i = SpawnPoints.Count - 1; i >= 0; i--)
                {
                    if (SpawnPoints[i] != null)
                        SpawnPoints[i].Destroy();
                }
            }

            #endregion Component Lifecycle

            #region

            public void Spawn(int numberToSpawn)
            {
                numberToSpawn = Mathf.Min(numberToSpawn, Data.MaximumPopulation - CurrentPopulation);

                for (int i = 0; i < numberToSpawn; i++)
                {
                    string prefabPath = GetPrefab();
                    if (string.IsNullOrEmpty(prefabPath))
                        continue;

                    GameObject prefab = GameManager.server.FindPrefab(prefabPath);
                    if (prefab == null)
                        continue;

                    SpawnPointComponent spawnPoint = GetRandomSpawnPoint();
                    if (spawnPoint == null)
                        continue;

                    Vector3 position = spawnPoint.GetPosition(randomize: )
                    Quaternion rotation = Quaternion.Euler(spawnPoint.Data.Rotation);

                    if (Data.RandomizeYRotation)
                        rotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                    BaseEntity entity = GameManager.server.CreateEntity(prefabPath, position, rotation);
                    if (entity == null)
                        continue;

                    entity.Spawn();
                    SpawnedEntities.Add(entity);
                }
            }

            public string GetPrefab()
            {
                int totalWeight = Data.Prefabs.Sum(prefab => prefab.Weight);

                if (totalWeight <= 0)
                    return null;

                float randomValue = Random.Range(0, totalWeight);

                foreach (PrefabData prefab in Data.Prefabs)
                {
                    randomValue -= prefab.Weight;
                    if (randomValue <= 0)
                    {
                        return prefab.Prefab;
                    }
                }

                return Data.Prefabs.LastOrDefault().Prefab;
            }

            public SpawnPointComponent GetRandomSpawnPoint()
            {
                Shuffle(SpawnPoints);

                foreach (SpawnPointComponent spawnPoint in SpawnPoints)
                {
                    if (!spawnPoint.HasPlayersIntersecting())
                        return spawnPoint;
                }

                return SpawnPoints.FirstOrDefault();
            }

            #endregion 
        }

        #endregion Spawner

        #region Spawn Point

        public class SpawnPointComponent : FacepunchBehaviour
        {
            #region Fields
            
            public LootSpawnerComponent LootSpawner { get; set; }
            public SpawnPointData Data { get; set; }

            #endregion Fields

            #region Initialization

            public static SpawnPointComponent Create(SpawnPointData data, LootSpawnerComponent lootSpawner)
            {
                SpawnPointComponent spawnPoint = new GameObject().AddComponent<SpawnPointComponent>();
                spawnPoint.Initialize(data, lootSpawner);

                return spawnPoint;
            }

            public void Initialize(SpawnPointData data, LootSpawnerComponent lootSpawner)
            {
                Data = data;
                LootSpawner = lootSpawner;
            }

            public void Destroy()
            {
                DestroyImmediate(this);
            }

            #endregion Initialization

            #region Component Lifecycle

            private void OnDestroy()
            {
                
            }

            #endregion Component Lifecycle

            public Vector3 GetPosition()
            {
                Vector3 targetPosition;

                if (Data.Radius > 0)
                    targetPosition = TerrainUtil.GetRandomPositionAround(Data.Position, 0f, Data.Radius);
                else
                    targetPosition = Data.Position;

                if (TerrainUtil.GetGroundInfo(targetPosition, out RaycastHit hit, Data.Radius, LAYER_GROUND | LAYER_ENTITIES))
                    targetPosition = hit.point;

                return targetPosition;
            }

            public bool HasPlayersIntersecting(float checkDistance = 2f)
            {
                return !PlayerUtil.HasPlayerNearby(Data.Position, checkDistance);
            }
        }

        #endregion Spawn Point

        #region Helper Functions

        public static void Shuffle<T>(List<T> list)
        {
            int remainingItems = list.Count;

            while (remainingItems > 1)
            {
                remainingItems--;
                int randomIndex = _plugin._randomGenerator.Next(remainingItems + 1);

                T itemToSwap = list[randomIndex];
                list[randomIndex] = list[remainingItems];
                list[remainingItems] = itemToSwap;
            }
        }

        private string FindPrefabPath(string shortPrefabName)
        {
            foreach (string prefabPath in GameManifest.Current.entities)
            {
                // Extract the filename from the prefab path
                string prefabFileName = prefabPath.Substring(prefabPath.LastIndexOf("/") + 1).Replace(".prefab", "");

                // Match with the short prefab name (case-insensitive)
                if (string.Equals(prefabFileName, shortPrefabName, StringComparison.OrdinalIgnoreCase))
                {
                    return prefabPath; // Return the full prefab path
                }
            }

            return null; // Return null if no match is found
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class TerrainUtil
        {
            public static bool OnTopology(Vector3 position, TerrainTopology.Enum topology)
            {
                return (TerrainMeta.TopologyMap.GetTopology(position) & (int)topology) != 0;
            }

            public static bool OnRoadOrRail(Vector3 position)
            {
                var combinedTopology = TerrainTopology.Enum.Road | TerrainTopology.Enum.Roadside |
                                   TerrainTopology.Enum.Rail | TerrainTopology.Enum.Railside;

                return OnTopology(position, combinedTopology);
            }

            public static bool OnTerrain(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Terrain, QueryTriggerInteraction.Ignore);
            }

            public static bool InNoBuildZone(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Prevent_Building);
            }

            public static bool InWater(Vector3 position)
            {
                return WaterLevel.Test(position, false, false);
            }

            public static bool InsideRock(Vector3 position, float radius)
            {
                List<Collider> colliders = Pool.Get<List<Collider>>();
                Vis.Colliders(position, radius, colliders, Layers.Mask.World, QueryTriggerInteraction.Ignore);

                bool result = false;

                foreach (Collider collider in colliders)
                {
                    if (collider.name.Contains("rock", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("cliff", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("formation", CompareOptions.OrdinalIgnoreCase))
                    {
                        result = true;
                        break;
                    }
                }

                Pool.FreeUnmanaged(ref colliders);
                return result;
            }

            public static bool InRadTown(Vector3 position, bool shouldDisplayOnMap = false)
            {
                foreach (var monumentInfo in TerrainMeta.Path.Monuments)
                {
                    bool inBounds = monumentInfo.IsInBounds(position);

                    bool hasLandMarker = true;
                    if (shouldDisplayOnMap)
                        hasLandMarker = monumentInfo.shouldDisplayOnMap;

                    if (inBounds && hasLandMarker)
                        return true;
                }

                return OnTopology(position, TerrainTopology.Enum.Monument);
            }

            public static bool HasEntityNearby(Vector3 position, float radius, LayerMask mask, string prefabName = null)
            {
                List<Collider> hitColliders = Pool.Get<List<Collider>>();
                GamePhysics.OverlapSphere(position, radius, hitColliders, mask, QueryTriggerInteraction.Ignore);

                bool hasEntityNearby = false;
                foreach (Collider collider in hitColliders)
                {
                    BaseEntity entity = collider.gameObject.ToBaseEntity();
                    if (entity != null)
                    {
                        if (prefabName == null || entity.PrefabName == prefabName)
                        {
                            hasEntityNearby = true;
                            break;
                        }
                    }
                }

                Pool.FreeUnmanaged(ref hitColliders);
                return hasEntityNearby;
            }

            public static Vector3 GetRandomPositionAround(Vector3 centerPosition, float minimumRadius, float maximumRadius)
            {
                Vector3 randomDirection = Random.onUnitSphere;
                randomDirection.y = 0;
                float randomDistance = Random.Range(minimumRadius, maximumRadius);
                Vector3 randomPosition = centerPosition + randomDirection * randomDistance;

                return randomPosition;
            }

            public static Vector3 GetPositionOnCircle(Vector3 centerPosition, float radius, float angle)
            {
                float radians = angle * Mathf.Deg2Rad;
                return new Vector3(centerPosition.x + Mathf.Cos(radians) * radius, centerPosition.y, centerPosition.z + Mathf.Sin(radians) * radius);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask)
            {
                return Physics.Linecast(startPosition + new Vector3(0.0f, range, 0.0f), startPosition - new Vector3(0.0f, range, 0.0f), out raycastHit, mask);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask, Transform ignoreTransform = null)
            {
                startPosition.y += 0.25f;
                range += 0.25f;
                raycastHit = default;

                RaycastHit hit;
                if (!GamePhysics.Trace(new Ray(startPosition, Vector3.down), 0f, out hit, range, mask, QueryTriggerInteraction.UseGlobal, null))
                    return false;

                if (ignoreTransform != null && hit.collider != null
                    && (hit.collider.transform == ignoreTransform || hit.collider.transform.IsChildOf(ignoreTransform)))
                {
                    return GetGroundInfo(startPosition - new Vector3(0f, 0.01f, 0f), out raycastHit, range, mask, ignoreTransform);
                }

                raycastHit = hit;
                return true;
            }
        }

        public static class DataFileUtil
        {
            private const string FOLDER = "CustomLootSpawns";

            public static string GetFilePath(string filename = null)
            {
                if (filename == null)
                    filename = _plugin.Name;

                return Path.Combine(FOLDER, filename);
            }

            public static string[] GetAllFilePaths()
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(FOLDER);

                for (int i = 0; i < filePaths.Length; i++)
                {
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);
                }

                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        public static class DrawUtil
        {
            public static void Box(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.box", durationSeconds, color, position, radius);
            }

            public static void Sphere(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", durationSeconds, color, position, radius);
            }

            public static void Line(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition)
            {
                player.SendConsoleCommand("ddraw.line", durationSeconds, color, fromPosition, toPosition);
            }

            public static void Arrow(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", durationSeconds, color, fromPosition, toPosition, headSize);
            }

            public static void Text(BasePlayer player, float durationSeconds, Color color, Vector3 position, string text)
            {
                player.SendConsoleCommand("ddraw.text", durationSeconds, color, position, text);
            }
        }

        public static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static bool HasPlayerNearby(Vector3 position, float radius)
            {
                return BaseNetworkable.HasCloseConnections(position, radius);
            }
        }

        #endregion Helper Classes

        #region Permissions

        private static class PermissionUtil
        {
            public const string ADMIN = "customlootspawns.admin";
            private static readonly List<string> _permissions = new List<string>
            {
                ADMIN,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Commands

        private static class Cmd
        {
            /// <summary>
            /// cls.spawngroup <spawnGroupAlias>
            /// cls.spawngroup edit <spawnGroupAlias>
            /// cls.spawngroup remove
            /// cls.spawngroup set <property> <value>
            /// - maxpop
            /// - minspawn
            /// - maxspawn
            /// - mindelay
            /// - maxdelay
            /// - randrot
            /// </summary>
            public const string SPAWN_GROUP = "cls.spawngroup";

            /// <summary>
            /// cls.spawnpoint add <position> [radius]
            /// cls.spawnpoint remove <spawnPointId>
            /// </summary>
            public const string SPAWN_POINT = "cls.spawnpoint";

            /// <summary>
            /// cls.prefab add <shortPrefabName> [weight]
            /// cls.prefab remove <shortPrefabName>
            /// </summary>
            public const string PREFAB = "cls.prefab";
        }

        [ConsoleCommand(Cmd.SPAWN_GROUP)]
        private void cmdSpawnGroup(ConsoleSystem.Arg conArgs)
        {
            if (conArgs == null)
                return;

            BasePlayer player = conArgs.Player();
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
                return;

            string[] args = conArgs.Args;
            if (args == null || args.Length == 0)
            {
                MessagePlayer(player, "Usage: cls.spawngroup <create/edit/remove> or cls.spawngroup set <property> <value>");
                return;
            }

            string subCommand = args[0];
            switch (subCommand)
            {
                case "create":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, "Usage: cls.spawngroup create <alias>");
                            return;
                        }

                        string alias = args[1];
                        string filePath = DataFileUtil.GetFilePath(alias);
                        if (DataFileUtil.Exists(filePath))
                        {
                            MessagePlayer(player, $"Spawn group with alias '{alias}' already exists.");
                            return;
                        }

                        SpawnGroupData newGroup = new SpawnGroupData
                        {
                            Alias = alias,
                            Id = Guid.NewGuid(),
                            MaximumPopulation = 5,
                            MinimumNumberToSpawnPerTick = 1,
                            MaximumNumberToSpawnPerTick = 2,
                            MinimumRespawnDelaySeconds = 10f,
                            MaximumRespawnDelaySeconds = 20f,
                            RandomizeYRotation = true
                        };

                        DataFileUtil.Save(filePath, newGroup);
                        _spawnGroupDataBeingEdited = newGroup;

                        MessagePlayer(player, $"Spawn group '{alias}' created and selected for editing.");
                        break;
                    }
                case "edit":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, "Usage: cls.spawngroup edit <alias>");
                            return;
                        }

                        string alias = args[1];
                        string filePath = DataFileUtil.GetFilePath(alias);
                        SpawnGroupData group = DataFileUtil.LoadIfExists<SpawnGroupData>(filePath);
                        if (group == null)
                        {
                            MessagePlayer(player, $"Spawn group '{alias}' not found.");
                            return;
                        }

                        _spawnGroupDataBeingEdited = group;
                        MessagePlayer(player, $"Spawn group '{alias}' selected for editing.");

                        VisualizeSpawnGroup(player);
                        break;
                    }
                case "remove":
                    {
                        if (_spawnGroupDataBeingEdited == null)
                        {
                            MessagePlayer(player, "You must edit a spawn group before removing it.");
                            return;
                        }

                        string alias = _spawnGroupDataBeingEdited.Alias;
                        string filePath = DataFileUtil.GetFilePath(alias);
                        DataFileUtil.Delete(filePath);
                        _spawnGroupDataBeingEdited = null;

                        MessagePlayer(player, $"Spawn group '{alias}' removed.");
                        break;
                    }
                case "set":
                    {
                        if (_spawnGroupDataBeingEdited == null)
                        {
                            MessagePlayer(player, "You must edit a spawn group before setting its properties.");
                            return;
                        }

                        if (args.Length < 3)
                        {
                            MessagePlayer(player, "Usage: cls.spawngroup set <property> <value>");
                            return;
                        }

                        string property = args[1].ToLower();
                        string value = args[2];
                        bool isValueValid = true;

                        switch (property)
                        {
                            case "maxpop":
                                if (int.TryParse(value, out int maxPopulation) && maxPopulation >= 0)
                                {
                                    _spawnGroupDataBeingEdited.MaximumPopulation = maxPopulation;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'MaximumPopulation'. Please enter a non-negative integer.");
                                    isValueValid = false;
                                }
                                break;

                            case "minspawn":
                                if (int.TryParse(value, out int minSpawn) && minSpawn >= 0)
                                {
                                    _spawnGroupDataBeingEdited.MinimumNumberToSpawnPerTick = minSpawn;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'MinimumNumberToSpawnPerTick'. Please enter a non-negative integer.");
                                    isValueValid = false;
                                }
                                break;

                            case "maxspawn":
                                if (int.TryParse(value, out int maxSpawn) && maxSpawn >= _spawnGroupDataBeingEdited.MinimumNumberToSpawnPerTick)
                                {
                                    _spawnGroupDataBeingEdited.MaximumNumberToSpawnPerTick = maxSpawn;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'MaximumNumberToSpawnPerTick'. It must be an integer greater than or equal to 'MinimumNumberToSpawnPerTick'.");
                                    isValueValid = false;
                                }
                                break;

                            case "mindelay":
                                if (float.TryParse(value, out float minDelay) && minDelay > 0)
                                {
                                    _spawnGroupDataBeingEdited.MinimumRespawnDelaySeconds = minDelay;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'MinimumRespawnDelaySeconds'. Please enter a positive number.");
                                    isValueValid = false;
                                }
                                break;

                            case "maxdelay":
                                if (float.TryParse(value, out float maxDelay) && maxDelay >= _spawnGroupDataBeingEdited.MinimumRespawnDelaySeconds)
                                {
                                    _spawnGroupDataBeingEdited.MaximumRespawnDelaySeconds = maxDelay;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'MaximumRespawnDelaySeconds'. It must be a number greater than or equal to 'MinimumRespawnDelaySeconds'.");
                                    isValueValid = false;
                                }
                                break;

                            case "randrot":
                                if (bool.TryParse(value, out bool randomizeYRotation))
                                {
                                    _spawnGroupDataBeingEdited.RandomizeYRotation = randomizeYRotation;
                                }
                                else
                                {
                                    MessagePlayer(player, "Invalid value for 'RandomizeYRotation'. Please enter 'true' or 'false'.");
                                    isValueValid = false;
                                }
                                break;

                            default:
                                MessagePlayer(player, $"Unknown property '{property}'.");
                                return;
                        }

                        if (isValueValid)
                        {
                            DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);
                            MessagePlayer(player, $"Property '{property}' set to '{value}' for spawn group '{_spawnGroupDataBeingEdited.Alias}'.");
                        }
                        break;
                    }
                default:
                    MessagePlayer(player, $"Unknown command '{subCommand}'.");
                    break;
            }
        }

        [ConsoleCommand(Cmd.SPAWN_POINT)]
        private void CmdSpawnPoint(ConsoleSystem.Arg conArgs)
        {
            if (conArgs == null)
                return;

            BasePlayer player = conArgs.Player();
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
                return;

            string[] args = conArgs.Args;
            if (args == null || args.Length == 0)
            {
                MessagePlayer(player, "Usage: cls.spawnpoint <add/remove> <position> [radius]");
                return;
            }

            if (_spawnGroupDataBeingEdited == null)
            {
                MessagePlayer(player, "You must edit a spawn group before managing spawn points.");
                return;
            }

            string subCommand = args[0];
            switch (subCommand)
            {
                case "add":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, "Usage: cls.spawnpoint add <position> [radius]");
                            return;
                        }

                        Vector3 position = conArgs.GetVector3(1, Vector3.zero);
                        if (conArgs.GetString(1).ToLower() == "here" && player != null)
                        {
                            if (TerrainUtil.GetGroundInfo(player.transform.position, out RaycastHit hitInfo, 2f, LAYER_GROUND | LAYER_ENTITIES))
                                position = hitInfo.point;
                            else
                                position = player.ServerPosition;
                        }

                        if (position == Vector3.zero)
                        {
                            MessagePlayer(player, "Invalid position. Please provide a valid position or type 'here'.");
                            return;
                        }

                        float radius = 1f;
                        if (args.Length > 2 && (!float.TryParse(args[2], out radius) || radius <= 0))
                        {
                            MessagePlayer(player, "Invalid radius. Please specify a positive value.");
                            return;
                        }

                        string spawnPointId = Guid.NewGuid().ToString();
                        SpawnPointData newSpawnPoint = new SpawnPointData
                        {
                            Id = spawnPointId,
                            Position = position,
                            Radius = radius
                        };

                        _spawnGroupDataBeingEdited.SpawnPoints.Add(newSpawnPoint);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, $"Spawn point added with id '{spawnPointId}', radius {radius} at position {position}.");
                        break;
                    }

                case "remove":
                    {
                        if (args.Length > 1)
                        {
                            string spawnPointId = args[1];
                            var spawnPointToRemove = _spawnGroupDataBeingEdited.SpawnPoints
                                .FirstOrDefault(sp => sp.Id.Equals(spawnPointId, StringComparison.OrdinalIgnoreCase));

                            if (spawnPointToRemove == null)
                            {
                                MessagePlayer(player, $"No spawn point found with id '{spawnPointId}'.");
                                return;
                            }

                            _spawnGroupDataBeingEdited.SpawnPoints.Remove(spawnPointToRemove);
                            DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);

                            VisualizeSpawnGroup(player);
                            MessagePlayer(player, $"Spawn point with id '{spawnPointId}' removed.");
                            return;
                        }

                        Vector3 playerPosition = player.transform.position;
                        float threshold = 1f;

                        var closestSpawnPoint = _spawnGroupDataBeingEdited.SpawnPoints
                            .OrderBy(sp => Vector3.Distance(sp.Position, playerPosition))
                            .FirstOrDefault(sp => Vector3.Distance(sp.Position, playerPosition) <= threshold);

                        if (closestSpawnPoint == null)
                        {
                            MessagePlayer(player, "No spawn point found within range.");
                            return;
                        }

                        string removedId = closestSpawnPoint.Id;

                        _spawnGroupDataBeingEdited.SpawnPoints.Remove(closestSpawnPoint);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, $"Spawn point with id '{removedId}' at position {closestSpawnPoint.Position} removed.");
                        break;
                    }

                default:
                    MessagePlayer(player, $"Unknown subcommand '{subCommand}'. Valid options are 'add' or 'remove'.");
                    break;
            }
        }

        [ConsoleCommand(Cmd.PREFAB)]
        private void CmdPrefab(ConsoleSystem.Arg conArgs)
        {
            if (conArgs == null)
                return;

            BasePlayer player = conArgs.Player();
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
                return;

            string[] args = conArgs.Args;
            if (args == null || args.Length == 0)
            {
                MessagePlayer(player, "Usage: cls.prefab <add/remove> <shortPrefabName> [weight]");
                return;
            }

            if (_spawnGroupDataBeingEdited == null)
            {
                MessagePlayer(player, "You must edit a spawn group before managing prefabs.");
                return;
            }

            string subCommand = args[0];
            switch (subCommand)
            {
                case "add":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, "Usage: cls.prefab add <shortPrefabName> [weight]");
                            return;
                        }

                        string shortPrefabName = args[1];
                        string fullPrefabPath = FindPrefabPath(shortPrefabName);

                        if (string.IsNullOrEmpty(fullPrefabPath))
                        {
                            MessagePlayer(player, $"Prefab '{shortPrefabName}' not found.");
                            return;
                        }

                        fullPrefabPath = fullPrefabPath.ToLowerInvariant();

                        if (_spawnGroupDataBeingEdited.Prefabs.Any(p => p.Prefab.Equals(fullPrefabPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            MessagePlayer(player, $"Prefab '{shortPrefabName}' is already in the spawn group.");
                            return;
                        }

                        int weight = 1; // Default weight
                        if (args.Length > 2 && (!int.TryParse(args[2], out weight) || weight <= 0))
                        {
                            MessagePlayer(player, "Invalid weight. Please specify a positive integer.");
                            return;
                        }

                        PrefabData newPrefab = new PrefabData
                        {
                            Prefab = fullPrefabPath,
                            Weight = weight
                        };

                        _spawnGroupDataBeingEdited.Prefabs.Add(newPrefab);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, $"Prefab '{shortPrefabName}' added to the spawn group with weight {weight}.");
                        break;
                    }

                case "remove":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, "Usage: cls.prefab remove <shortPrefabName>");
                            return;
                        }

                        string shortPrefabName = args[1];
                        string fullPrefabPath = FindPrefabPath(shortPrefabName)?.ToLowerInvariant(); // Ensure lowercase

                        if (string.IsNullOrEmpty(fullPrefabPath))
                        {
                            MessagePlayer(player, $"Prefab '{shortPrefabName}' not found.");
                            return;
                        }

                        var prefabToRemove = _spawnGroupDataBeingEdited.Prefabs
                            .FirstOrDefault(p => p.Prefab.Equals(fullPrefabPath, StringComparison.OrdinalIgnoreCase));

                        if (prefabToRemove == null)
                        {
                            MessagePlayer(player, $"Prefab '{shortPrefabName}' not found in the spawn group.");
                            return;
                        }

                        _spawnGroupDataBeingEdited.Prefabs.Remove(prefabToRemove);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupDataBeingEdited.Alias), _spawnGroupDataBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, $"Prefab '{shortPrefabName}' removed from the spawn group.");
                        break;
                    }

                default:
                    MessagePlayer(player, $"Unknown subcommand '{subCommand}'. Valid options are 'add' or 'remove'.");
                    break;
            }
        }

        #endregion Commands

        #region Command Helpers

        private void VisualizeSpawnGroup(BasePlayer player)
        {
            if (_spawnGroupDataBeingEdited == null || _spawnGroupDataBeingEdited.SpawnPoints.Count == 0)
                return;

            Vector3 center = Vector3.zero;
            foreach (SpawnPointData spawnPoint in _spawnGroupDataBeingEdited.SpawnPoints)
            {
                center += spawnPoint.Position;
            }
            center /= _spawnGroupDataBeingEdited.SpawnPoints.Count;
            center.y += 3f;

            string spawnGroupInfo = $"<size=30>Spawn Group: {_spawnGroupDataBeingEdited.Alias}</size>\n" +
                               $"<size=25>Maximum Population: {_spawnGroupDataBeingEdited.MaximumPopulation}</size>\n" +
                               $"<size=25>Minimum Number To Spawn Per Tick: {_spawnGroupDataBeingEdited.MinimumNumberToSpawnPerTick}</size>\n" +
                               $"<size=25>Maximum Number To Spawn Per Tick: {_spawnGroupDataBeingEdited.MaximumNumberToSpawnPerTick}</size>\n" +
                               $"<size=25>Minimum Respawn Delay Seconds: {_spawnGroupDataBeingEdited.MinimumRespawnDelaySeconds}</size>\n" +
                               $"<size=25>Maximum Respawn Delay Seconds: {_spawnGroupDataBeingEdited.MaximumRespawnDelaySeconds}</size>\n" +
                               $"<size=25>Randomize Y Rotation: {_spawnGroupDataBeingEdited.RandomizeYRotation}</size>\n" +
                               $"<size=25>Total Spawn Points: {_spawnGroupDataBeingEdited.SpawnPoints.Count}</size>\n" +
                               $"<size=25>Total Prefabs: {_spawnGroupDataBeingEdited.Prefabs.Count}</size>";

            DrawUtil.Text(player, 20f, Color.white, center, spawnGroupInfo);

            foreach (SpawnPointData spawnPoint in _spawnGroupDataBeingEdited.SpawnPoints)
            {
                DrawUtil.Sphere(player, 20f, Color.green, spawnPoint.Position, spawnPoint.Radius);
                DrawUtil.Arrow(player, 20f, Color.black, center, spawnPoint.Position, 0.5f);

                string spawnPointInfo = $"<size=30>Spawn Point</size>\n" +
                    $"<size=25>{spawnPoint.Id}</size>";
                DrawUtil.Text(player, 20f, Color.white, spawnPoint.Position, spawnPointInfo);
            }
        }

        #endregion Command Helpers

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You don't have permission to use this command.",

            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        public static void ShowToast(BasePlayer player, string messageKey, GameTip.Styles style = GameTip.Styles.Blue_Normal, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            player.SendConsoleCommand("gametip.showtoast", (int)style, message);
        }

        #endregion Localization
    }
}