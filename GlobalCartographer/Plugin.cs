using BepInEx;
using UnityEngine;
using HarmonyLib;
using BepInEx.Configuration;
using System.Collections;
using System;
using System.Collections.Generic;

namespace GlobalCartographer
{
    [HarmonyPatch(typeof(CharMovement))]
    class HookCharMovement
    {
        const string CommandMessage = "forcedownloadworld";

        public static bool DownloadInitiatedForSession;

        [HarmonyPrefix]
        [HarmonyPatch("CmdSendChatMessage")]
        static bool CmdSendChatMessagePrefix(string newMessage)
        {
            string FlattenedString = string.Join("", newMessage.ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            if (FlattenedString == CommandMessage)
            {
                if (!NetworkMapSharer.Instance.isClientOnly)
                {
                    NotificationManager.manage.createChatNotification("Command does nothing if you are not a multiplayer client.");
                }
                else if (DownloadInitiatedForSession)
                {
                    NotificationManager.manage.createChatNotification("Command does nothing after the first time it is run, per session.");
                }
                else
                {
                    Plugin.Instance.InitiateDownload();

                    DownloadInitiatedForSession = true;
                }

                return (false);
            }

            return (true);
        }
    }

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        const int ModId = 154;

        const int MapSize = 1000, ChunkSize = 10, ChunkCount = MapSize / ChunkSize, TotalChunkCount = ChunkCount * ChunkCount;

        public static Plugin Instance;

        static bool Ready;

        static int ChunksPerFrame, SecondsPerMessage;

        void StoreModId()
        {
            ConfigEntry<int> ModIdEntry = Config.Bind<int>("Developer", "NexusID", ModId, "nexus mod id -- automatically generated -- cannot be changed");

            if (ModIdEntry.Value != ModId)
            {
                ModIdEntry.Value = ModId;

                Config.Save();
            }
        }

        void Start()
        {
            Instance = this;

            ChunksPerFrame = Config.Bind<int>("General", "ChunksPerFrame", 5, "how fast (and laggy) to run the forced update -- 1 slowest (little lag) -- 10 fastest (much lag)").Value;

            if (ChunksPerFrame < 1)
            {
                ChunksPerFrame = 1;
            }
            else if (ChunksPerFrame > 10)
            {
                ChunksPerFrame = 10;
            }

            SecondsPerMessage = Config.Bind<int>("General", "SecondsPerMessage", 3, "how often (in seconds) to display update progress -- minimum 1").Value;

            if (SecondsPerMessage < 1)
            {
                SecondsPerMessage = 1;
            }

            StoreModId();

            new Harmony(PluginInfo.PLUGIN_GUID).PatchAll();
        }

        void Update()
        {
            if (NetworkMapSharer.Instance.localChar == null)
            {
                if (Ready)
                {
                    HookCharMovement.DownloadInitiatedForSession = false;

                    Ready = false;
                }
            }
            else
            {
                if (!Ready)
                {
                    Ready = true;
                }
            }
        }

        public void InitiateDownload()
        {
            StartCoroutine(ForceRefreshAllChunks());
        }

        static void PurgeUnusedChunkInstances()
        {
            List<Chunk> NewChunksInUse = new List<Chunk>();

            foreach (Chunk CurrentChunk in WorldManager.Instance.chunksInUse)
            {
                if (CurrentChunk.isActiveAndEnabled)
                {
                    NewChunksInUse.Add(CurrentChunk);
                }
                else
                {
                    Destroy(CurrentChunk);
                }
            }

            WorldManager.Instance.chunksInUse = NewChunksInUse;
        }

        static void PurgeUnusedTileObjectInstances()
        {
            List<TileObject> NewFreeObjects = new List<TileObject>();

            foreach (TileObject CurrentTileObject in WorldManager.Instance.freeObjects)
            {
                if (CurrentTileObject.active)
                {
                    NewFreeObjects.Add(CurrentTileObject);
                }
                else
                {
                    Destroy(CurrentTileObject);
                }
            }

            WorldManager.Instance.freeObjects = NewFreeObjects;

            Traverse.Create(WorldManager.Instance).Field("freeObjectsCount").SetValue(NewFreeObjects.Count);
        }

        static IEnumerator ForceRefreshAllChunks()
        {
            int CompletedChunkCount = 0;

            float NextNotificationTime = Time.time;

            for (int ChunkX = 0; ChunkX < ChunkCount; ChunkX++)
            {
                for (int ChunkY = 0; ChunkY < ChunkCount; ChunkY++)
                {
                    int CoordX = ChunkX * ChunkSize, CoordY = ChunkY * ChunkSize;

                    if (WorldManager.Instance.doesPositionNeedsChunk(CoordX, CoordY))
                    {
                        WorldManager.Instance.getFreeChunkAndSetInPos(CoordX, CoordY);
                    }

                    CompletedChunkCount++;

                    if (CompletedChunkCount % ChunksPerFrame == 0)
                    {
                        NewChunkLoader.loader.resetChunksViewing();

                        yield return (null);

                        if (NetworkMapSharer.Instance.localChar == null)
                        {
                            yield break;
                        }
                    }

                    if (Time.time >= NextNotificationTime)
                    {
                        int Progress = (CompletedChunkCount * 100) / TotalChunkCount;

                        NotificationManager.manage.createChatNotification(Progress + "% complete.");

                        NextNotificationTime = Time.time + SecondsPerMessage;
                    }
                }
            }

            PurgeUnusedChunkInstances();

            PurgeUnusedTileObjectInstances();

            yield return (RenderMap.Instance.ScanTheMap());

            NotificationManager.manage.createChatNotification("100% complete.");
        }
    }
}