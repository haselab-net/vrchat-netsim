using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// Correct toggle: the state is a synced variable (so late joiners get it) and only the owner
    /// changes it; other players send the owner a request.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SyncedToggle : UdonSharpBehaviour
    {
        public GameObject lamp;
        [UdonSynced] public bool isOn;

        void Start() { Apply(); }

        public override void Interact()
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Flip));
        }

        public void Flip()
        {
            isOn = !isOn;
            RequestSerialization();
            Apply();
        }

        public override void OnDeserialization() { Apply(); }

        void Apply() { if (lamp != null) lamp.SetActive(isOn); }
    }
}
