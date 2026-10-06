using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// Counts balls carried into the goal. Only the owner of the goal counts, so every goal is counted once.
    /// Picking the ball up inside the goal fires OnTriggerEnter again, so a ball counts only once until it has
    /// left the goal area.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GoalCounter : UdonSharpBehaviour
    {
        public Text label;
        public string ballName = "Ball";
        public float rearmDistance = 2.5f;
        [UdonSynced] public int goals;

        Transform counted;   // ball counted last; counts again after it has left the goal area

        void Start() { Refresh(); }

        void Update()
        {
            if (counted != null && Vector3.Distance(counted.position, transform.position) > rearmDistance) counted = null;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other == null || other.gameObject.name != ballName) return;
            if (!Networking.IsOwner(gameObject)) return;
            if (counted == other.transform) return;
            counted = other.transform;
            goals++;
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization() { Refresh(); }

        void Refresh() { if (label != null) label.text = $"Goals\n{goals}"; }
    }
}
