using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// BUG (on purpose): changes the synced value without taking ownership.
    /// Only the owner's serialization is accepted, so clicks by other players are lost
    /// and those players keep showing a value nobody else sees.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LostWriteCounter : UdonSharpBehaviour
    {
        public Text label;
        [UdonSynced] public int count;

        void Start() { Refresh(); }

        public override void Interact()
        {
            count++;                 // missing: Networking.SetOwner(Networking.LocalPlayer, gameObject);
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization() { Refresh(); }

        void Refresh() { if (label != null) label.text = $"Lost-write counter (bug)\n{count}"; }
    }
}
