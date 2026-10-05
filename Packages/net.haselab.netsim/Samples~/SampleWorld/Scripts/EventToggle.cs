using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Haselab.NetSim.SampleWorld
{
    /// <summary>
    /// BUG (on purpose): the state only lives in a network event, not in a synced variable.
    /// Players who join later never receive past events and see the lamp in its initial state.
    /// (Manual sync without synced variables: network events need a sync mode other than None.)
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EventToggle : UdonSharpBehaviour
    {
        public GameObject lamp;
        bool isOn;

        public override void Interact()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Toggle));
        }

        public void Toggle()
        {
            isOn = !isOn;
            if (lamp != null) lamp.SetActive(isOn);
        }
    }
}
