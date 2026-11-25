using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenBenchSeat : UdonSharpBehaviour
    {
        public VRC.SDK3.Components.VRCStation station;
        public override void Interact()
        {
            if(Utilities.IsValid(Networking.LocalPlayer)) station.UseStation(Networking.LocalPlayer);
        }
    }
}
