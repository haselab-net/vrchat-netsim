// Settings shared by the NetSim window, scenario runs started from code and the command line (NetSimCli).
// Stored as JSON in ProjectSettings/NetSimSettings.json so they can be committed, edited by hand or by a script.
// The file is re-read when it changes on disk.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Haselab.NetSim
{
    [Serializable]
    public class NetSimSettings
    {
        /// <summary>Network conditions used when a scenario or a manual session starts without an explicit NetSimConfig.</summary>
        public NetSimConfig config = new NetSimConfig();
        /// <summary>Editor speed while a scenario runs (simulated time per real time). Manual sessions run at 1.</summary>
        public float timeScale = 4f;
        /// <summary>Clients in total, including the local ClientSim player C0 (used by the built-in scenarios).</summary>
        public int clients = 3;
        /// <summary>How long the built-in scenarios let the random bot play (simulated seconds).</summary>
        public float playSeconds = 120f;
        /// <summary>Chance per client per simulated second that the random bot presses a button / interacts.</summary>
        public float actionProbability = 0.3f;
        /// <summary>Scenario selected in the window.</summary>
        public string scenario = "random-play";
        /// <summary>Steps of the window's batch list (NetSimSuite step strings).</summary>
        public string[] batch = new string[0];

        public const string FilePath = "ProjectSettings/NetSimSettings.json";
        static NetSimSettings instance;
        static DateTime loadedStamp;

        public static NetSimSettings Instance
        {
            get
            {
                var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (instance == null || stamp != loadedStamp) Load(stamp);
                return instance;
            }
        }

        static void Load(DateTime stamp)
        {
            loadedStamp = stamp;
            try { instance = File.Exists(FilePath) ? JsonUtility.FromJson<NetSimSettings>(File.ReadAllText(FilePath)) : null; }
            catch (Exception e) { Debug.LogWarning("[NetSim] cannot read " + FilePath + ": " + e.Message); instance = null; }
            if (instance == null) instance = new NetSimSettings();
            if (instance.config == null) instance.config = new NetSimConfig();
            if (instance.batch == null) instance.batch = new string[0];
        }

        public static void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(Instance, true));
            loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        }

        public NetSimConfig CloneConfig() => JsonUtility.FromJson<NetSimConfig>(JsonUtility.ToJson(config));

        // ------------------------------------------------------------------ presets of network conditions
        public struct Preset
        {
            public string name; public float latMin, latMax, drop;
            public Preset(string n, float a, float b, float d) { name = n; latMin = a; latMax = b; drop = d; }
        }

        /// <summary>Latency is one way per hop (client -> server or server -> client); drop applies to events, synced variables and object sync.</summary>
        public static readonly Preset[] Presets =
        {
            new Preset("No delay (logic check)", 0f, 0f, 0f),
            new Preset("Good (20-50 ms)", 0.02f, 0.05f, 0f),
            new Preset("Typical (50-150 ms, default)", 0.05f, 0.15f, 0f),
            new Preset("Far away (150-400 ms)", 0.15f, 0.4f, 0f),
            new Preset("Bad Wi-Fi (100-600 ms, 5% loss)", 0.1f, 0.6f, 0.05f),
            new Preset("Very lossy (50-300 ms, 20% loss)", 0.05f, 0.3f, 0.2f),
        };

        public void Apply(Preset p)
        {
            config.latencyMin = p.latMin; config.latencyMax = p.latMax;
            config.eventDropRate = config.serializationDropRate = config.objectSyncDropRate = p.drop;
        }

        public int MatchingPreset()
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                if (Mathf.Approximately(p.latMin, config.latencyMin) && Mathf.Approximately(p.latMax, config.latencyMax)
                    && Mathf.Approximately(p.drop, config.eventDropRate) && Mathf.Approximately(p.drop, config.serializationDropRate)
                    && Mathf.Approximately(p.drop, config.objectSyncDropRate)) return i;
            }
            return -1;
        }
    }
}
#endif
