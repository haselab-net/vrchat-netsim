using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// Correct counter: players ask the owner to change the value, and only the owner writes it.
    /// Simultaneous clicks are applied one after another by the owner, so no click is lost
    /// (unless the network event itself is lost) and every player ends with the owner's value.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GoodCounter : UdonSharpBehaviour
    {
        public Text label;
        [UdonSynced] public int count;

        void Start() { Refresh(); }

        public override void Interact()
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Increment));
        }

        public void Increment()
        {
            count++;
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization() { Refresh(); }

        void Refresh() { if (label != null) label.text = $"Good counter\n{count}"; }
    }
}
