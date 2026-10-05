using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// Counts balls carried into the goal. Only the owner of the goal counts, so every goal is counted once.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GoalCounter : UdonSharpBehaviour
    {
        public Text label;
        public string ballName = "Ball";
        [UdonSynced] public int goals;

        void Start() { Refresh(); }

        void OnTriggerEnter(Collider other)
        {
            if (other == null || other.gameObject.name != ballName) return;
            if (!Networking.IsOwner(gameObject)) return;
            goals++;
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization() { Refresh(); }

        void Refresh() { if (label != null) label.text = $"Goals\n{goals}"; }
    }
}
