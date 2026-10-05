using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// BUG (on purpose, subtle): the common "take ownership, then write" pattern.
    /// It works for one player at a time, but when two players click at nearly the same time both
    /// take ownership and write; only one write survives, and the other player keeps showing its own
    /// value until the next change.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RacyCounter : UdonSharpBehaviour
    {
        public Text label;
        [UdonSynced] public int count;

        void Start() { Refresh(); }

        public override void Interact()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            count++;
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization() { Refresh(); }

        void Refresh() { if (label != null) label.text = $"Racy counter (bug)\n{count}"; }
    }
}
