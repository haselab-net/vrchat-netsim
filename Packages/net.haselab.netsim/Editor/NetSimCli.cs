// Command line entry point: open a scene, run scenarios unattended and quit with an exit code.
//
//   Unity.exe -projectPath <project> -executeMethod Haselab.NetSim.NetSimCli.Run
//             -netsimScene Assets/MyWorld.unity
//             -netsimRun random-play -netsimRun "late-join|slow|0.2|0.6|0.05|0.05|0.05|2|4"
//             [-netsimList] [-logFile <file>]
//
// - The project must not be open in another Unity Editor. Do not pass -quit (the editor quits by itself when done).
// - -netsimRun takes a scenario name or a NetSimSuite step string; it can be repeated. Without -netsimRun, the batch
//   list saved in the NetSim window is run. Settings not given in a step come from ProjectSettings/NetSimSettings.json.
// - -netsimList only writes the known scenarios to Logs/NetSim/scenarios.txt and quits.
// - Works with -batchmode as well (play mode runs without a window).
// - Results: Logs/NetSim/suite.log, one report per scenario in Logs/NetSim/, and Logs/NetSim/cli-result.txt
//   ("<verdict>\t<step>\t<report>" per step).
// - Exit code: 0 = every scenario passed (or made no checks), 2 = a check failed, 1 = error (unknown scenario, compile
//   error, exception, scenario never started).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Haselab.NetSim
{
    [InitializeOnLoad]
    public static class NetSimCli
    {
        const string KeyActive = "Haselab.NetSim.Cli.Active";
        const string ResultPath = "Logs/NetSim/cli-result.txt";

        static NetSimCli()
        {
            // the suite outlives domain reloads; re-attach after each one while a command line run is in progress
            if (SessionState.GetBool(KeyActive, false)) NetSimSuite.Finished += OnFinished;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var steps = new List<string>();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-netsimRun") steps.Add(args[i + 1]);
            Directory.CreateDirectory("Logs/NetSim");

            if (args.Contains("-netsimList"))
            {
                File.WriteAllLines("Logs/NetSim/scenarios.txt", NetSimScenarioRunner.ScenarioNames.OrderBy(x => x)
                    .Select(n => n + "\t" + NetSimScenarioRunner.DescriptionOf(n)));
                Exit(0, "scenarios written to Logs/NetSim/scenarios.txt");
                return;
            }
            if (EditorUtility.scriptCompilationFailed) { Exit(1, "scripts do not compile"); return; }

            var scene = Arg("-netsimScene");
            if (!string.IsNullOrEmpty(scene))
            {
                if (!File.Exists(scene)) { Exit(1, "scene not found: " + scene); return; }
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            }
            if (steps.Count == 0) steps.AddRange(NetSimSettings.Instance.batch);
            if (steps.Count == 0) { Exit(1, "nothing to run: pass -netsimRun <scenario> or save a batch list in the NetSim window"); return; }
            var known = new HashSet<string>(NetSimScenarioRunner.ScenarioNames);
            var unknown = steps.Where(s => !s.StartsWith("swap:")).Select(s => s.Split('|')[0].Trim()).Where(n => !known.Contains(n)).Distinct().ToList();
            if (unknown.Count > 0) { Exit(1, "unknown scenario(s): " + string.Join(", ", unknown) + " (known: " + string.Join(", ", known.OrderBy(x => x)) + ")"); return; }

            Debug.Log("[NetSimCli] running " + steps.Count + " step(s) on " + EditorSceneManager.GetActiveScene().path);
            SessionState.SetBool(KeyActive, true);
            NetSimSuite.Finished -= OnFinished; NetSimSuite.Finished += OnFinished;
            NetSimSuite.Start(steps.ToArray());
        }

        static void OnFinished(string[] results)
        {
            NetSimSuite.Finished -= OnFinished;
            SessionState.SetBool(KeyActive, false);
            var verdicts = results.Select(r => r.Split('\t')[0]).ToList();
            int code = verdicts.Count == 0 || verdicts.Contains("ERROR") ? 1 : verdicts.Contains("FAIL") ? 2 : 0;
            File.WriteAllLines(ResultPath, results);
            Exit(code, string.Join("\n", results));
        }

        static void Exit(int code, string message)
        {
            Debug.Log($"[NetSimCli] exit {code}: {message}");
            try { File.AppendAllText("Logs/NetSim/suite.log", $"[{DateTime.Now:HH:mm:ss}] cli exit {code}\n"); } catch { }
            // leave play mode first; exiting from inside play mode can hang the editor
            EditorApplication.CallbackFunction quit = null;
            quit = () => {
                if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
                EditorApplication.update -= quit;
                EditorApplication.Exit(code);
            };
            EditorApplication.update += quit;
        }
    }
}
#endif
