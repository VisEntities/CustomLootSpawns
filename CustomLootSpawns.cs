﻿/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
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
    [Description("Lets you add additional spawn points for loot containers.")]
    public class CustomLootSpawns : RustPlugin
    {
        #region Fields

        private static CustomLootSpawns _plugin;
        private System.Random _randomGenerator = new System.Random();

        private SpawnGroupData _spawnGroupBeingEdited;
        private List<LootSpawnerComponent> _lootSpawners = new List<LootSpawnerComponent>();

        public const int LAYER_LOOT_CRATES = Layers.Mask.Default;
        public const int LAYER_PLAYERS = Layers.Mask.Player_Server;
        public const int LAYER_BUILDINGS = Layers.Mask.Construction | Layers.Mask.Deployed;
        public const int LAYER_GROUND = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Default;
        
        private static readonly Dictionary<string, string> _lootContainerPrefabs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"junk", "assets/bundled/prefabs/radtown/crate_normal_2.prefab"},
            {"military", "assets/bundled/prefabs/radtown/crate_normal.prefab"},
            {"elite", "assets/bundled/prefabs/radtown/crate_elite.prefab"},
            {"medical", "assets/bundled/prefabs/radtown/crate_normal_2_medical.prefab"},
            {"food", "assets/bundled/prefabs/radtown/crate_normal_2_food.prefab"},
            {"ammo", "assets/bundled/prefabs/radtown/dmloot/dm ammo.prefab"},
            {"explosives", "assets/bundled/prefabs/radtown/dmloot/dm c4.prefab"},
            {"foodbox", "assets/bundled/prefabs/radtown/foodbox.prefab"},
            {"tools", "assets/bundled/prefabs/radtown/crate_tools.prefab"},
            {"vehicleparts", "assets/bundled/prefabs/radtown/vehicle_parts.prefab"},

            {"waterammo", "assets/bundled/prefabs/radtown/underwater_labs/crate_ammunition.prefab"},
            {"watermedical", "assets/bundled/prefabs/radtown/underwater_labs/crate_medical.prefab"},
            {"waterfood", "assets/bundled/prefabs/radtown/underwater_labs/crate_food_1.prefab"},
            {"waterfoodbox", "assets/bundled/prefabs/radtown/underwater_labs/crate_food_2.prefab"},
            {"waterfuel", "assets/bundled/prefabs/radtown/underwater_labs/crate_fuel.prefab"},
            {"watertechparts", "assets/bundled/prefabs/radtown/underwater_labs/tech_parts_2.prefab"},

            {"hackable", "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab"},
            {"hackableoilrig", "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate_oilrig.prefab"},
            {"bradley", "assets/prefabs/npc/m2bradley/bradley_crate.prefab"},
            {"patrolheli", "assets/prefabs/npc/patrol helicopter/heli_crate.prefab"},
            {"supplydrop", "assets/prefabs/misc/supply drop/supply_drop.prefab"},

            {"barrel", "assets/bundled/prefabs/radtown/loot_barrel_2.prefab"},
            {"barrelblue", "assets/bundled/prefabs/radtown/loot_barrel_1.prefab"},
            {"barreloil", "assets/bundled/prefabs/radtown/oil_barrel.prefab"},
            {"barreldiesel", "assets/prefabs/resource/diesel barrel/diesel_barrel_world.prefab"},
        };

        #endregion Fields

        #region Stored Data

        public class SpawnGroupData
        {
            [JsonProperty("Alias")]
            public string Alias { get; set; }

            [JsonProperty("Id")]
            public Guid Id { get; set; }

            [JsonProperty("Active")]
            public bool Active { get; set; }

            [JsonProperty("Maximum Population")]
            public int MaximumPopulation { get; set; }

            [JsonProperty("Minimum Number To Spawn Per Tick")]
            public int MinimumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Maximum Number To Spawn Per Tick")]
            public int MaximumNumberToSpawnPerTick { get; set; }

            [JsonProperty("Initial Spawn")]
            public bool InitialSpawn { get; set; }

            [JsonProperty("Minimum Respawn Delay Seconds")]
            public float MinimumRespawnDelaySeconds { get; set; }

            [JsonProperty("Maximum Respawn Delay Seconds")]
            public float MaximumRespawnDelaySeconds { get; set; }

            [JsonProperty("Randomize Y Rotation")]
            public bool RandomizeYRotation { get; set; }
            
            [JsonProperty("Replace Looted Containers")]
            public bool ReplaceLootedContainers { get; set; }

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
            if (_lootSpawners != null)
            {
                for (int i = _lootSpawners.Count - 1; i >= 0; i--)
                {
                    LootSpawnerComponent lootSpawner = _lootSpawners[i];
                    if (lootSpawner != null)
                        lootSpawner.Destroy();
                }

                _lootSpawners.Clear();
            }

            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            string[] spawnGroupFiles = DataFileUtil.GetAllFilePaths();
            foreach (string filePath in spawnGroupFiles)
            {
                SpawnGroupData spawnGroup = DataFileUtil.LoadIfExists<SpawnGroupData>(filePath);
                if (spawnGroup != null)
                {
                    LootSpawnerComponent.Create(spawnGroup);
                }
            }
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer lootContainer)
        {
            if (player == null || lootContainer == null)
                return;

            if (lootContainer.inventory == null || lootContainer.inventory.itemList == null || lootContainer.inventory.itemList.Count == 0)
            {
                LootSpawnerComponent lootSpawner = GetSpawnerForLootContainer(lootContainer);
                if (lootSpawner != null)
                {
                    lootSpawner.OnLootContainerLooted(lootContainer);
                }
            }
        }

        #endregion Oxide Hooks

        #region Loot Spawner

        public class LootSpawnerComponent : FacepunchBehaviour
        {
            #region Fields

            private bool _isDestroying;
            public SpawnGroupData Data { get; set; }
            public List<SpawnPointComponent> SpawnPoints { get; set; } = new List<SpawnPointComponent>();
            public List<LootContainer> SpawnedEntities { get; set; } = new List<LootContainer>();
            public int CurrentPopulation
            {
                get
                {
                    return SpawnedEntities.Count(lootContainer => lootContainer != null);
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

                _plugin._lootSpawners.Add(this);
            }

            public void Destroy()
            {
                _isDestroying = true;
                DestroyImmediate(this);
            }

            #endregion Initialization

            #region Component Lifecycle

            private void Start()
            {
                if (!Data.Active)
                    return;

                float initialSpawnTime = GetNextSpawnTime();
                if (Data.InitialSpawn)
                    initialSpawnTime = 0f;

                InvokeRepeating(nameof(TimedSpawn), initialSpawnTime, GetNextSpawnTime());
            }

            private void OnDestroy()
            {
                CancelInvoke(nameof(TimedSpawn));
                Clear();

                for (int i = SpawnPoints.Count - 1; i >= 0; i--)
                {
                    if (SpawnPoints[i] != null)
                        SpawnPoints[i].Destroy();
                }

                _plugin._lootSpawners.Remove(this);
            }

            #endregion Component Lifecycle

            #region Spawn Lifecycle

            private void TimedSpawn()
            {
                if (!_isDestroying && CurrentPopulation < Data.MaximumPopulation)
                {
                    int numberToSpawn = Random.Range(Data.MinimumNumberToSpawnPerTick, Data.MaximumNumberToSpawnPerTick + 1);
                    Spawn(numberToSpawn);
                }
            }

            private float GetNextSpawnTime()
            {
                return Random.Range(Data.MinimumRespawnDelaySeconds, Data.MaximumRespawnDelaySeconds);
            }

            public void Spawn(int numberToSpawn)
            {
                numberToSpawn = Mathf.Min(numberToSpawn, Data.MaximumPopulation - CurrentPopulation);

                for (int i = 0; i < numberToSpawn; i++)
                {
                    string prefabPath = GetPrefab();
                    if (string.IsNullOrEmpty(prefabPath))
                        continue;

                    if (GetSpawnPoint(prefabPath, out Vector3 position, out Quaternion rotation) == null)
                        continue;

                    if (OnCustomLootContainerSpawn(Data.Alias, prefabPath, position, rotation))
                        continue;

                    LootContainer lootContainer = GameManager.server.CreateEntity(prefabPath, position, rotation) as LootContainer;
                    if (lootContainer == null)
                        continue;

                    // TODO: Consider parenting the spawned entity if it's placed on another entity.
                    lootContainer.Spawn();
                    lootContainer.EnableSaving(false);
                    SpawnedEntities.Add(lootContainer);
                }
            }

            public void Fill()
            {
                int numberToFill = Data.MaximumPopulation - CurrentPopulation;
                Spawn(numberToFill);
            }

            public void Clear()
            {
                for (int i = SpawnedEntities.Count - 1; i >= 0; i--)
                {
                    LootContainer lootContainer = SpawnedEntities[i];
                    if (lootContainer != null)
                        lootContainer.Kill();

                    SpawnedEntities.RemoveAt(i);
                }
            }

            #endregion Spawn Lifecycle

            #region Prefab Selection

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
                        if (_lootContainerPrefabs.TryGetValue(prefab.Prefab, out string fullPrefabPath))
                            return fullPrefabPath;
                        else
                            return null;
                    }
                }

                PrefabData lastPrefab = Data.Prefabs.LastOrDefault();
                if (lastPrefab != null && _lootContainerPrefabs.TryGetValue(lastPrefab.Prefab, out string lastPrefabPath))
                    return lastPrefabPath;

                return null;
            }

            #endregion Prefab Selection

            #region Spawn Point Selection

            public SpawnPointComponent GetSpawnPoint(string prefabPath, out Vector3 position, out Quaternion rotation)
            {
                Shuffle(SpawnPoints);

                foreach (SpawnPointComponent spawnPoint in SpawnPoints)
                {
                    if (spawnPoint.HasPlayersIntersecting())
                        continue;

                    for (int attempts = 0; attempts < 5; attempts++)
                    {
                        position = spawnPoint.GetPosition();
                        rotation = spawnPoint.GetRotation();

                        if (Data.RandomizeYRotation)
                            rotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                        if (!spawnPoint.HasSpaceToSpawn(prefabPath, position, rotation))
                            continue;

                        return spawnPoint;
                    }
                }

                position = default;
                rotation = default;
                return null;
            }

            #endregion Spawn Point Selection

            #region Loot Containers Removal

            public void OnLootContainerLooted(LootContainer lootContainer)
            {
                if (!_isDestroying && Data.ReplaceLootedContainers)
                    Spawn(1);
            }

            #endregion Loot Containers Removal
        }

        #endregion Loot Spawner

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
                LootSpawner.SpawnPoints.Add(this);
            }

            public void Destroy()
            {
                DestroyImmediate(this);
            }

            #endregion Initialization

            #region Component Lifecycle

            private void OnDestroy()
            {
                LootSpawner.SpawnPoints.Remove(this);
            }

            #endregion Component Lifecycle

            #region Position and Rotation Retrieval

            public Vector3 GetPosition()
            {
                Vector3 targetPosition;

                if (Data.Radius > 0)
                    targetPosition = TerrainUtil.GetRandomPositionAround(Data.Position, 0f, Data.Radius);
                else
                    targetPosition = Data.Position;

                if (TerrainUtil.GetGroundInfo(targetPosition, out RaycastHit hit, Data.Radius, LAYER_GROUND | LAYER_BUILDINGS))
                    targetPosition = hit.point;

                return targetPosition;
            }

            public Quaternion GetRotation()
            {
                Quaternion baseRotation = Quaternion.Euler(Data.Rotation);

                if (TerrainUtil.GetGroundInfo(Data.Position, out RaycastHit hit, 2f, LAYER_GROUND | LAYER_BUILDINGS))
                {
                    Quaternion terrainAlignment = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    return terrainAlignment * baseRotation;
                }

                return baseRotation;
            }

            #endregion Position and Rotation Retrieval

            #region Collision and Space Validation

            public bool HasSpaceToSpawn(string prefabPath, Vector3 position, Quaternion rotation)
            {
                GameObject prefab = GameManager.server.FindPrefab(prefabPath);
                if (prefab == null)
                    return false;

                BaseEntity prefabEntity = prefab.GetComponent<BaseEntity>();
                if (prefabEntity == null)
                    return false;

                OBB entityBounds = new OBB(position, rotation, prefabEntity.bounds);

                Collider[] colliders = Physics.OverlapBox(
                    entityBounds.position,
                    entityBounds.extents,
                    entityBounds.rotation,
                    LAYER_LOOT_CRATES,
                    QueryTriggerInteraction.Ignore
                );

                if (colliders.Length > 0)
                    return false;

                return true;
            }

            public bool HasPlayersIntersecting()
            {
                return PlayerUtil.HasPlayerNearby(Data.Position, Mathf.Max(Data.Radius, 2f));
            }

            #endregion Collision and Space Validation
        }

        #endregion Spawn Point

        #region API
        
        [HookMethod(nameof(API_ForceSpawnLootContainers))]
        public void API_ForceSpawnLootContainers(string spawnGroupAlias)
        {
            LootSpawnerComponent lootSpawner = GetSpawnerByAlias(spawnGroupAlias);
            if (lootSpawner != null)
            {
                lootSpawner.Fill();
            }
        }
        
        [HookMethod(nameof(API_ForceClearLootContainers))]
        public void API_ForceClearLootContainers(string spawnGroupAlias)
        {
            LootSpawnerComponent lootSpawner = GetSpawnerByAlias(spawnGroupAlias);
            if (lootSpawner != null)
            {
                lootSpawner.Clear();
            }
        }
        
        [HookMethod(nameof(API_IsLootContainerCustomSpawned))]
        public bool API_IsLootContainerCustomSpawned(LootContainer lootContainer)
        {
            return GetSpawnerForLootContainer(lootContainer) != null;
        }

        [HookMethod(nameof(API_SpawnGroupExists))]
        public bool API_SpawnGroupExists(string spawnGroupAlias)
        {
            if (string.IsNullOrEmpty(spawnGroupAlias))
                return false;

            string filePath = DataFileUtil.GetFilePath(spawnGroupAlias);

            return DataFileUtil.Exists(filePath);
        }

        #endregion API

        #region Exposed Hooks

        private static bool OnCustomLootContainerSpawn(string spawnGroupAlias, string prefab, Vector3 position, Quaternion rotation)
        {
            object hookResult = Interface.CallHook("OnCustomLootContainerSpawn", spawnGroupAlias, prefab, position, rotation);
            return hookResult is bool && (bool)hookResult == false;
        }

        #endregion Exposed Hooks

        #region Loot Spawner Retrieval

        private LootSpawnerComponent GetSpawnerByAlias(string spawnGroupAlias)
        {
            if (string.IsNullOrEmpty(spawnGroupAlias))
                return null;
            
            foreach (LootSpawnerComponent lootSpawner in _lootSpawners)
            {
                if (lootSpawner.Data.Alias.Equals(spawnGroupAlias, StringComparison.OrdinalIgnoreCase))
                    return lootSpawner;
            }

            return null;
        }
        
        private LootSpawnerComponent GetSpawnerForLootContainer(LootContainer lootContainer)
        {
            foreach (LootSpawnerComponent lootSpawner in _lootSpawners)
            {
                if (lootSpawner.SpawnedEntities.Contains(lootContainer))
                    return lootSpawner;
            }
            return null;
        }

        #endregion Loot Spawner Retrieval

        #region Helper Functions

        private static void Shuffle<T>(List<T> list)
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

        private static string FindPrefabPath(string shortPrefabName)
        {
            foreach (string prefabPath in GameManifest.Current.entities)
            {
                string prefabFileName = prefabPath.Substring(prefabPath.LastIndexOf("/") + 1).Replace(".prefab", "");

                if (string.Equals(prefabFileName, shortPrefabName, StringComparison.OrdinalIgnoreCase))
                    return prefabPath;
            }

            return null;
        }

        private static string GetShortPrefabName(string fullPrefabPath)
        {
            if (string.IsNullOrEmpty(fullPrefabPath))
                return string.Empty;

            int lastSlashIndex = fullPrefabPath.LastIndexOf("/");
            if (lastSlashIndex == -1)
                return fullPrefabPath.Replace(".prefab", "");

            return fullPrefabPath.Substring(lastSlashIndex + 1).Replace(".prefab", "");
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
            /// - active
            /// - initialspawn
            /// - maxpop
            /// - minspawn
            /// - maxspawn
            /// - mindelay
            /// - maxdelay
            /// - randrot
            /// - replacelooted
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
            {
                MessagePlayer(player, Lang.NoPermission);
                return;
            }

            string[] args = conArgs.Args;
            if (args == null || args.Length == 0)
            {
                MessagePlayer(player, Lang.SpawnGroupUsage);
                return;
            }

            string subCommand = args[0];
            switch (subCommand)
            {
                case "create":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, Lang.SpawnGroupCreateUsage);
                            return;
                        }

                        string alias = args[1];
                        string filePath = DataFileUtil.GetFilePath(alias);
                        if (DataFileUtil.Exists(filePath))
                        {
                            MessagePlayer(player, Lang.SpawnGroupCreateAlreadyExists, alias);
                            return;
                        }

                        SpawnGroupData newGroup = new SpawnGroupData
                        {
                            Alias = alias,
                            Id = Guid.NewGuid(),
                            Active = true,
                            MaximumPopulation = 5,
                            MinimumNumberToSpawnPerTick = 1,
                            MaximumNumberToSpawnPerTick = 2,
                            InitialSpawn = true,
                            MinimumRespawnDelaySeconds = 30f,
                            MaximumRespawnDelaySeconds = 60f,
                            RandomizeYRotation = true,
                            ReplaceLootedContainers = true
                        };

                        DataFileUtil.Save(filePath, newGroup);
                        _spawnGroupBeingEdited = newGroup;

                        MessagePlayer(player, Lang.SpawnGroupCreated, alias);
                        break;
                    }
                case "edit":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, Lang.SpawnGroupEditUsage);
                            return;
                        }

                        string alias = args[1];
                        string filePath = DataFileUtil.GetFilePath(alias);
                        SpawnGroupData group = DataFileUtil.LoadIfExists<SpawnGroupData>(filePath);
                        if (group == null)
                        {
                            MessagePlayer(player, Lang.SpawnGroupNotFound, alias);
                            return;
                        }

                        LootSpawnerComponent lootSpawner = GetSpawnerByAlias(alias);
                        if (lootSpawner != null)
                            lootSpawner.Destroy();

                        _spawnGroupBeingEdited = group;
                        MessagePlayer(player, Lang.SpawnGroupEdited, alias);
                        VisualizeSpawnGroup(player);
                        break;
                    }
                case "remove":
                    {
                        if (_spawnGroupBeingEdited == null)
                        {
                            MessagePlayer(player, Lang.SpawnGroupNoGroupBeingEdited);
                            return;
                        }

                        string alias = _spawnGroupBeingEdited.Alias;
                        string filePath = DataFileUtil.GetFilePath(alias);
                        DataFileUtil.Delete(filePath);
                        _spawnGroupBeingEdited = null;

                        MessagePlayer(player, Lang.SpawnGroupRemoved, alias);
                        break;
                    }
                case "set":
                    {
                        if (_spawnGroupBeingEdited == null)
                        {
                            MessagePlayer(player, Lang.SpawnGroupSetNoGroupBeingEdited);
                            return;
                        }

                        if (args.Length < 3)
                        {
                            string editableProperties = string.Join("\n", new[]
                            {
                                "- active",
                                "- initialspawn",
                                "- maxpop",
                                "- minspawn",
                                "- maxspawn",
                                "- mindelay",
                                "- maxdelay",
                                "- randrot",
                                "- replacelooted"
                            });
                            MessagePlayer(player, Lang.SpawnGroupSetUsage, editableProperties);
                            return;
                        }

                        string property = args[1].ToLower();
                        string value = args[2];
                        bool isValueValid = true;

                        switch (property)
                        {
                            case "active":
                                if (bool.TryParse(value, out bool active))
                                {
                                    _spawnGroupBeingEdited.Active = active;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidActive);
                                    isValueValid = false;
                                }
                                break;

                            case "initialspawn":
                                if (bool.TryParse(value, out bool initialSpawn))
                                {
                                    _spawnGroupBeingEdited.InitialSpawn = initialSpawn;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidInitialSpawn);
                                    isValueValid = false;
                                }
                                break;

                            case "maxpop":
                                if (int.TryParse(value, out int maxPopulation) && maxPopulation >= 0)
                                {
                                    _spawnGroupBeingEdited.MaximumPopulation = maxPopulation;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidMaximumPop);
                                    isValueValid = false;
                                }
                                break;

                            case "minspawn":
                                if (int.TryParse(value, out int minSpawn) && minSpawn >= 0)
                                {
                                    _spawnGroupBeingEdited.MinimumNumberToSpawnPerTick = minSpawn;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidMinimumSpawn);
                                    isValueValid = false;
                                }
                                break;

                            case "maxspawn":
                                if (int.TryParse(value, out int maxSpawn) && maxSpawn >= _spawnGroupBeingEdited.MinimumNumberToSpawnPerTick)
                                {
                                    _spawnGroupBeingEdited.MaximumNumberToSpawnPerTick = maxSpawn;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidMaximumSpawn);
                                    isValueValid = false;
                                }
                                break;

                            case "mindelay":
                                if (float.TryParse(value, out float minDelay) && minDelay > 0)
                                {
                                    _spawnGroupBeingEdited.MinimumRespawnDelaySeconds = minDelay;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidMinimumDelay);
                                    isValueValid = false;
                                }
                                break;

                            case "maxdelay":
                                if (float.TryParse(value, out float maxDelay) && maxDelay >= _spawnGroupBeingEdited.MinimumRespawnDelaySeconds)
                                {
                                    _spawnGroupBeingEdited.MaximumRespawnDelaySeconds = maxDelay;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidMaximumDelay);
                                    isValueValid = false;
                                }
                                break;

                            case "randrot":
                                if (bool.TryParse(value, out bool randomizeYRotation))
                                {
                                    _spawnGroupBeingEdited.RandomizeYRotation = randomizeYRotation;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidRandomYRotation);
                                    isValueValid = false;
                                }
                                break;

                            case "replacelooted":
                                if (bool.TryParse(value, out bool replaceLootContainers))
                                {
                                    _spawnGroupBeingEdited.ReplaceLootedContainers = replaceLootContainers;
                                }
                                else
                                {
                                    MessagePlayer(player, Lang.SpawnGroupSetInvalidReplaceLootedContainers);
                                    isValueValid = false;
                                }
                                break;

                            default:
                                string editableProperties = string.Join("\n", new[]
                                          {
                                            "- active",
                                            "- initialspawn",
                                            "- maxpop",
                                            "- minspawn",
                                            "- maxspawn",
                                            "- mindelay",
                                            "- maxdelay",
                                            "- randrot",
                                            "- replacelooted"
                                        });
                                MessagePlayer(player, Lang.SpawnGroupSetUnknownProperty, property, editableProperties);
                                return;
                        }

                        if (isValueValid)
                        {
                            DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);
                            MessagePlayer(player, Lang.SpawnGroupSetPropertyUpdated, property, value, _spawnGroupBeingEdited.Alias);
                            VisualizeSpawnGroup(player);
                        }
                        break;
                    }
                case "done":
                    {
                        if (_spawnGroupBeingEdited == null)
                        {
                            MessagePlayer(player, Lang.SpawnGroupDoneNoGroupBeingEdited);
                            return;
                        }

                        string alias = _spawnGroupBeingEdited.Alias;
                        string filePath = DataFileUtil.GetFilePath(alias);

                        DataFileUtil.Save(filePath, _spawnGroupBeingEdited);

                        LootSpawnerComponent.Create(_spawnGroupBeingEdited);

                        _spawnGroupBeingEdited = null;
                        MessagePlayer(player, Lang.SpawnGroupDoneFinishedEditing, alias);
                        break;
                    }
                default:
                    MessagePlayer(player, Lang.SpawnGroupUnknownCommand, subCommand);
                    break;
            }
        }

        [ConsoleCommand(Cmd.SPAWN_POINT)]
        private void cmdSpawnPoint(ConsoleSystem.Arg conArgs)
        {
            if (conArgs == null)
                return;

            BasePlayer player = conArgs.Player();
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
            {
                MessagePlayer(player, Lang.NoPermission);
                return;
            }

            string[] args = conArgs.Args;
            if (args == null || args.Length == 0)
            {
                MessagePlayer(player, Lang.SpawnPointUsage);
                return;
            }

            if (_spawnGroupBeingEdited == null)
            {
                MessagePlayer(player, Lang.SpawnPointNoGroupBeingEdited);
                return;
            }

            string subCommand = args[0];
            switch (subCommand)
            {
                case "add":
                    {
                        if (args.Length < 2)
                        {
                            MessagePlayer(player, Lang.SpawnPointAddUsage);
                            return;
                        }

                        Vector3 position = Vector3.zero;

                        if (conArgs.GetString(1).ToLower() == "here" && player != null)
                            position = player.transform.position;
                        else
                            position = conArgs.GetVector3(1, Vector3.zero);

                        if (!TerrainUtil.GetGroundInfo(position, out RaycastHit hitInfo, 2f, LAYER_GROUND | LAYER_BUILDINGS))
                        {
                            MessagePlayer(player, Lang.SpawnPointAddInvalidPosition);
                            return;
                        }

                        position = hitInfo.point;

                        float radius = 0f;
                        if (args.Length > 2 && !float.TryParse(args[2], out radius))
                        {
                            MessagePlayer(player, Lang.SpawnPointAddInvalidRadius);
                            return;
                        }

                        if (radius < 0)
                        {
                            MessagePlayer(player, Lang.SpawnPointAddRadiusNegative);
                            return;
                        }

                        string spawnPointId = Guid.NewGuid().ToString();
                        SpawnPointData newSpawnPoint = new SpawnPointData
                        {
                            Id = spawnPointId,
                            Position = position,
                            Radius = radius
                        };

                        _spawnGroupBeingEdited.SpawnPoints.Add(newSpawnPoint);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, Lang.SpawnPointAddSuccess, spawnPointId, radius, position);
                        break;
                    }

                case "remove":
                    {
                        if (args.Length > 1)
                        {
                            string spawnPointId = args[1];
                            var spawnPointToRemove = _spawnGroupBeingEdited.SpawnPoints
                                .FirstOrDefault(sp => sp.Id.Equals(spawnPointId, StringComparison.OrdinalIgnoreCase));

                            if (spawnPointToRemove == null)
                            {
                                MessagePlayer(player, Lang.SpawnPointRemoveNotFound, spawnPointId);
                                return;
                            }

                            _spawnGroupBeingEdited.SpawnPoints.Remove(spawnPointToRemove);
                            DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);

                            VisualizeSpawnGroup(player);
                            MessagePlayer(player, Lang.SpawnPointRemoveSuccess, spawnPointId, spawnPointToRemove.Position);
                            return;
                        }

                        Vector3 playerPosition = player.transform.position;
                        float threshold = 1f;

                        var closestSpawnPoint = _spawnGroupBeingEdited.SpawnPoints
                            .OrderBy(sp => Vector3.Distance(sp.Position, playerPosition))
                            .FirstOrDefault(sp => Vector3.Distance(sp.Position, playerPosition) <= threshold);

                        if (closestSpawnPoint == null)
                        {
                            MessagePlayer(player, Lang.SpawnPointRemoveNoInRange);
                            return;
                        }

                        string removedId = closestSpawnPoint.Id;

                        _spawnGroupBeingEdited.SpawnPoints.Remove(closestSpawnPoint);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);

                        VisualizeSpawnGroup(player);
                        MessagePlayer(player, Lang.SpawnPointRemoveSuccess, removedId, closestSpawnPoint.Position);
                        break;
                    }

                default:
                    MessagePlayer(player, Lang.SpawnPointUnknownSubcommand, subCommand);
                    break;
            }
        }

        [ConsoleCommand(Cmd.PREFAB)]
        private void cmdPrefab(ConsoleSystem.Arg conArgs)
        {
            if (conArgs == null)
                return;

            BasePlayer player = conArgs.Player();
            if (player == null)
                return;

            if (!PermissionUtil.HasPermission(player, PermissionUtil.ADMIN))
            {
                MessagePlayer(player, Lang.NoPermission);
                return;
            }

            string[] args = conArgs.Args;
            if (args == null || args.Length < 2)
            {
                MessagePlayer(player, Lang.PrefabUsage);
                return;
            }

            if (_spawnGroupBeingEdited == null)
            {
                MessagePlayer(player, Lang.PrefabNoGroupBeingEdited);
                return;
            }

            string subCommand = args[0].ToLower();
            switch (subCommand)
            {
                case "add":
                    {
                        List<string> addedPrefabs = new List<string>();

                        for (int i = 1; i < args.Length; i += 2)
                        {
                            string shortPrefabName = args[i];

                            if (!_lootContainerPrefabs.ContainsKey(shortPrefabName))
                            {
                                string predefinedNames = string.Join("\n", _lootContainerPrefabs.Keys.Select(name => $"- {name}"));
                                MessagePlayer(player, Lang.PrefabAddUnrecognizedPrefab, shortPrefabName, predefinedNames);
                                continue;
                            }

                            if (_spawnGroupBeingEdited.Prefabs.Any(p => p.Prefab.Equals(shortPrefabName, StringComparison.OrdinalIgnoreCase)))
                            {
                                MessagePlayer(player, Lang.PrefabAddAlreadyExists, shortPrefabName);
                                continue;
                            }

                            int weight = 1;
                            if (i + 1 < args.Length && (!int.TryParse(args[i + 1], out weight) || weight <= 0))
                            {
                                MessagePlayer(player, Lang.PrefabAddInvalidWeight, shortPrefabName);
                                weight = 1;
                            }

                            PrefabData newPrefab = new PrefabData
                            {
                                Prefab = shortPrefabName,
                                Weight = weight
                            };

                            _spawnGroupBeingEdited.Prefabs.Add(newPrefab);
                            addedPrefabs.Add($"{shortPrefabName} (weight {weight})");
                        }

                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);
                        VisualizeSpawnGroup(player);

                        if (addedPrefabs.Count > 0)
                        {
                            MessagePlayer(player, Lang.PrefabAddSuccess, string.Join("\n- ", addedPrefabs));
                        }
                        else
                        {
                            MessagePlayer(player, Lang.PrefabAddNoneAdded);
                        }

                        break;
                    }

                case "remove":
                    {
                        List<string> removedPrefabs = new List<string>();

                        for (int i = 1; i < args.Length; i++)
                        {
                            string shortPrefabName = args[i];

                            var prefabToRemove = _spawnGroupBeingEdited.Prefabs
                                .FirstOrDefault(p => p.Prefab.Equals(shortPrefabName, StringComparison.OrdinalIgnoreCase));

                            if (prefabToRemove == null)
                            {
                                MessagePlayer(player, Lang.PrefabRemoveNotFound, shortPrefabName);
                                continue;
                            }

                            _spawnGroupBeingEdited.Prefabs.Remove(prefabToRemove);
                            removedPrefabs.Add(shortPrefabName);
                        }

                        DataFileUtil.Save(DataFileUtil.GetFilePath(_spawnGroupBeingEdited.Alias), _spawnGroupBeingEdited);
                        VisualizeSpawnGroup(player);

                        if (removedPrefabs.Count > 0)
                        {
                            MessagePlayer(player, Lang.PrefabRemoveSuccess, string.Join("\n- ", removedPrefabs));
                        }
                        else
                        {
                            MessagePlayer(player, Lang.PrefabRemoveNoneRemoved);
                        }

                        break;
                    }

                default:
                    MessagePlayer(player, Lang.PrefabUnknownSubcommand, subCommand);
                    break;
            }
        }

        #endregion Commands

        #region Command Helpers

        private void VisualizeSpawnGroup(BasePlayer player)
        {
            if (_spawnGroupBeingEdited == null || _spawnGroupBeingEdited.SpawnPoints.Count == 0)
                return;

            Vector3 center = Vector3.zero;
            foreach (SpawnPointData spawnPoint in _spawnGroupBeingEdited.SpawnPoints)
            {
                center += spawnPoint.Position;
            }
            center /= _spawnGroupBeingEdited.SpawnPoints.Count;

            if (TerrainUtil.GetGroundInfo(center, out RaycastHit groundHit, 50f, LAYER_GROUND | LAYER_BUILDINGS))
            {
                center = groundHit.point + new Vector3(0, 50f, 0);
            }

            string spawnGroupInfo = $"<size=30>Spawn Group: {_spawnGroupBeingEdited.Alias}</size>\n" +
                                    $"<size=25>Active: {_spawnGroupBeingEdited.Active}</size>\n" +
                                    $"<size=25>Maximum Population: {_spawnGroupBeingEdited.MaximumPopulation}</size>\n" +
                                    $"<size=25>Minimum Number To Spawn Per Tick: {_spawnGroupBeingEdited.MinimumNumberToSpawnPerTick}</size>\n" +
                                    $"<size=25>Maximum Number To Spawn Per Tick: {_spawnGroupBeingEdited.MaximumNumberToSpawnPerTick}</size>\n" +
                                    $"<size=25>Minimum Respawn Delay Seconds: {_spawnGroupBeingEdited.MinimumRespawnDelaySeconds}</size>\n" +
                                    $"<size=25>Maximum Respawn Delay Seconds: {_spawnGroupBeingEdited.MaximumRespawnDelaySeconds}</size>\n" +
                                    $"<size=25>Randomize Y Rotation: {_spawnGroupBeingEdited.RandomizeYRotation}</size>\n" +
                                    $"<size=25>Replace Looted Containers: {_spawnGroupBeingEdited.ReplaceLootedContainers}</size>\n" +
                                    $"<size=25>Total Spawn Points: {_spawnGroupBeingEdited.SpawnPoints.Count}</size>\n" +
                                    $"<size=25>Total Prefabs: {_spawnGroupBeingEdited.Prefabs.Count}</size>\n\n" +
                                    $"<size=30>Prefabs:</size>";

            foreach (PrefabData prefab in _spawnGroupBeingEdited.Prefabs)
            {
                spawnGroupInfo += $"\n<size=25>{prefab.Prefab} (Weight: {prefab.Weight})</size>";
            }

            DrawUtil.Text(player, 15f, Color.white, center, spawnGroupInfo);

            foreach (SpawnPointData spawnPoint in _spawnGroupBeingEdited.SpawnPoints)
            {
                DrawUtil.Sphere(player, 15f, Color.green, spawnPoint.Position, spawnPoint.Radius);
                DrawUtil.Arrow(player, 15f, Color.black, center, spawnPoint.Position, 0.5f);

                string spawnPointInfo = $"<size=30>Spawn Point</size>";
                DrawUtil.Text(player, 15f, Color.white, spawnPoint.Position, spawnPointInfo);
            }
        }

        #endregion Command Helpers

        #region Localization

        private class Lang
        {
            public const string NoPermission = "NoPermission";

            public const string SpawnGroupUsage = "SpawnGroup.Usage";
            public const string SpawnGroupCreateUsage = "SpawnGroup.CreateUsage";
            public const string SpawnGroupCreateAlreadyExists = "SpawnGroup.CreateAlreadyExists";
            public const string SpawnGroupCreated = "SpawnGroup.Created";
            public const string SpawnGroupEditUsage = "SpawnGroup.EditUsage";
            public const string SpawnGroupNotFound = "SpawnGroup.NotFound";
            public const string SpawnGroupEdited = "SpawnGroup.Edited";
            public const string SpawnGroupNoGroupBeingEdited = "SpawnGroup.NoGroupBeingEdited";
            public const string SpawnGroupRemoved = "SpawnGroup.Removed";
            public const string SpawnGroupUnknownCommand = "SpawnGroup.UnknownCommand";
             
            public const string SpawnGroupSetNoGroupBeingEdited = "SpawnGroup.Set.NoGroupBeingEdited";
            public const string SpawnGroupSetUsage = "SpawnGroup.Set.Usage";
            public const string SpawnGroupSetInvalidActive = "SpawnGroup.Set.InvalidActive";
            public const string SpawnGroupSetInvalidInitialSpawn = "SpawnGroup.Set.InvalidInitialSpawn";
            public const string SpawnGroupSetInvalidMaximumPop = "SpawnGroup.Set.InvalidMaximumPop";
            public const string SpawnGroupSetInvalidMinimumSpawn = "SpawnGroup.Set.InvalidMinimumSpawn";
            public const string SpawnGroupSetInvalidMaximumSpawn = "SpawnGroup.Set.InvalidMaximumSpawn";
            public const string SpawnGroupSetInvalidMinimumDelay = "SpawnGroup.Set.InvalidMinimumDelay";
            public const string SpawnGroupSetInvalidMaximumDelay = "SpawnGroup.Set.InvalidMaximumDelay";
            public const string SpawnGroupSetInvalidRandomYRotation = "SpawnGroup.Set.InvalidRandomRotation";
            public const string SpawnGroupSetInvalidReplaceLootedContainers = "SpawnGroup.Set.InvalidReplaceLootedContainers";
            public const string SpawnGroupSetUnknownProperty = "SpawnGroup.Set.UnknownProperty";
            public const string SpawnGroupSetPropertyUpdated = "SpawnGroup.Set.PropertyUpdated";

            public const string SpawnGroupDoneNoGroupBeingEdited = "SpawnGroup.Done.NoGroupBeingEdited";
            public const string SpawnGroupDoneFinishedEditing = "SpawnGroup.Done.FinishedEditing";
            
            public const string SpawnPointUsage = "SpawnPoint.Usage";
            public const string SpawnPointNoGroupBeingEdited = "SpawnPoint.NoGroupBeingEdited";
            public const string SpawnPointAddUsage = "SpawnPoint.Add.Usage";
            public const string SpawnPointAddInvalidPosition = "SpawnPoint.Add.InvalidPosition";
            public const string SpawnPointAddInvalidRadius = "SpawnPoint.Add.InvalidRadius";
            public const string SpawnPointAddRadiusNegative = "SpawnPoint.Add.RadiusNegative";
            public const string SpawnPointAddSuccess = "SpawnPoint.Add.Success";
            public const string SpawnPointRemoveNotFound = "SpawnPoint.Remove.NotFound";
            public const string SpawnPointRemoveNoInRange = "SpawnPoint.Remove.NoInRange";
            public const string SpawnPointRemoveSuccess = "SpawnPoint.Remove.Success";
            public const string SpawnPointUnknownSubcommand = "SpawnPoint.UnknownSubcommand";

            public const string PrefabUsage = "Prefab.Usage";
            public const string PrefabNoGroupBeingEdited = "Prefab.NoGroupBeingEdited";
            public const string PrefabAddUnrecognizedPrefab = "Prefab.Add.UnrecognizedPrefab";
            public const string PrefabAddAlreadyExists = "Prefab.Add.AlreadyExists";
            public const string PrefabAddInvalidWeight = "Prefab.Add.InvalidWeight";
            public const string PrefabAddSuccess = "Prefab.Add.Success";
            public const string PrefabAddNoneAdded = "Prefab.Add.NoneAdded";
            public const string PrefabRemoveNotFound = "Prefab.Remove.NotFound";
            public const string PrefabRemoveSuccess = "Prefab.Remove.Success";
            public const string PrefabRemoveNoneRemoved = "Prefab.Remove.NoneRemoved";
            public const string PrefabUnknownSubcommand = "Prefab.UnknownSubcommand";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You don't have permission to use this command.",

                [Lang.SpawnGroupUsage] = "Usage: cls.spawngroup <create/edit/remove> or cls.spawngroup set <property> <value>",
                [Lang.SpawnGroupCreateUsage] = "Usage: cls.spawngroup create <alias>",
                [Lang.SpawnGroupCreateAlreadyExists] = "Spawn group with alias '{0}' already exists.",
                [Lang.SpawnGroupCreated] = "Spawn group '{0}' created and selected for editing.",
                [Lang.SpawnGroupEditUsage] = "Usage: cls.spawngroup edit <alias>",
                [Lang.SpawnGroupNotFound] = "Spawn group '{0}' not found.",
                [Lang.SpawnGroupEdited] = "Spawn group '{0}' selected for editing.",
                [Lang.SpawnGroupNoGroupBeingEdited] = "You must edit a spawn group before removing it.",
                [Lang.SpawnGroupRemoved] = "Spawn group '{0}' removed successfully.",
                [Lang.SpawnGroupUnknownCommand] = "Unknown command '{0}'.",

                [Lang.SpawnGroupSetNoGroupBeingEdited] = "You must edit a spawn group before setting its properties.",
                [Lang.SpawnGroupSetUsage] = "Usage: cls.spawngroup set <property> <value>\n\nAvailable properties:\n{0}",
                [Lang.SpawnGroupSetInvalidActive] = "Invalid value for 'Active'. Please enter 'true' or 'false'.",
                [Lang.SpawnGroupSetInvalidInitialSpawn] = "Invalid value for 'InitialSpawn'. Please enter 'true' or 'false'.",
                [Lang.SpawnGroupSetInvalidMaximumPop] = "Invalid value for 'MaximumPopulation'. Please enter a non-negative integer.",
                [Lang.SpawnGroupSetInvalidMinimumSpawn] = "Invalid value for 'MinimumNumberToSpawnPerTick'. Please enter a non-negative integer.",
                [Lang.SpawnGroupSetInvalidMaximumSpawn] = "Invalid value for 'MaximumNumberToSpawnPerTick'. It must be an integer greater than or equal to 'MinimumNumberToSpawnPerTick'.",
                [Lang.SpawnGroupSetInvalidMinimumDelay] = "Invalid value for 'MinimumRespawnDelaySeconds'. Please enter a positive number.",
                [Lang.SpawnGroupSetInvalidMaximumDelay] = "Invalid value for 'MaximumRespawnDelaySeconds'. It must be a number greater than or equal to 'MinimumRespawnDelaySeconds'.",
                [Lang.SpawnGroupSetInvalidRandomYRotation] = "Invalid value for 'RandomizeYRotation'. Please enter 'true' or 'false'.",
                [Lang.SpawnGroupSetInvalidReplaceLootedContainers] = "Invalid value for 'ReplaceLootedContainers'. Please enter 'true' or 'false'.",
                [Lang.SpawnGroupSetUnknownProperty] = "Unknown property '{0}'.\n\nAvailable properties:\n{1}",
                [Lang.SpawnGroupSetPropertyUpdated] = "Property '{0}' set to '{1}' for spawn group '{2}'.",

                [Lang.SpawnGroupDoneNoGroupBeingEdited] = "You must edit a spawn group before finishing editing.",
                [Lang.SpawnGroupDoneFinishedEditing] = "Finished editing spawn group '{0}' and applied changes.",

                [Lang.SpawnPointUsage] = "Usage: cls.spawnpoint <add/remove> <position> [radius]",
                [Lang.SpawnPointNoGroupBeingEdited] = "You must edit a spawn group before managing spawn points.",
                [Lang.SpawnPointAddUsage] = "Usage: cls.spawnpoint add <position> [radius]",
                [Lang.SpawnPointAddInvalidPosition] = "The selected position is invalid. Please choose a position on a valid surface.",
                [Lang.SpawnPointAddInvalidRadius] = "Invalid radius. Please specify a valid number.",
                [Lang.SpawnPointAddRadiusNegative] = "Radius cannot be negative.",
                [Lang.SpawnPointAddSuccess] = "Spawn point added with id '{0}', radius {1} at position {2}.",
                [Lang.SpawnPointRemoveNotFound] = "No spawn point found with id '{0}'.",
                [Lang.SpawnPointRemoveNoInRange] = "No spawn point found within range.",
                [Lang.SpawnPointRemoveSuccess] = "Spawn point with id '{0}' at position {1} removed.",
                [Lang.SpawnPointUnknownSubcommand] = "Unknown subcommand '{0}'. Valid options are 'add' or 'remove'.",

                [Lang.PrefabUsage] = "Usage: cls.prefab <add/remove> <shortPrefabName1> [weight1] <shortPrefabName2> [weight2] ...",
                [Lang.PrefabNoGroupBeingEdited] = "You must edit a spawn group before managing prefabs.",
                [Lang.PrefabAddUnrecognizedPrefab] = "The prefab name '{0}' is not recognized. Please use one of the following valid names:\n{1}",
                [Lang.PrefabAddAlreadyExists] = "Prefab '{0}' is already in the spawn group.",
                [Lang.PrefabAddInvalidWeight] = "Invalid weight for prefab '{0}'. Defaulting to weight 1.",
                [Lang.PrefabAddSuccess] = "Added prefabs:\n- {0}",
                [Lang.PrefabAddNoneAdded] = "No prefabs were added.",
                [Lang.PrefabRemoveNotFound] = "Prefab '{0}' not found in the spawn group.",
                [Lang.PrefabRemoveSuccess] = "Removed prefabs:\n- {0}",
                [Lang.PrefabRemoveNoneRemoved] = "No prefabs were removed.",
                [Lang.PrefabUnknownSubcommand] = "Unknown subcommand '{0}'. Valid options are 'add' or 'remove'."
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