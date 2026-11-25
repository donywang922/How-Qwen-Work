using UdonSharp;
using UnityEngine;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenLightbox : UdonSharpBehaviour
    {
        public Material pages;
        public float secondsPerPage = 24;
        public void Start() { pages.SetFloat("_ManualPage",-1); pages.SetFloat("_PageOffset",0); }
        public void NextPage()
        {
            float now=Time.timeSinceLevelLoad;
            float phase=Mathf.Repeat(now+pages.GetFloat("_PageOffset"),secondsPerPage*3);
            int next=(Mathf.FloorToInt(phase/secondsPerPage)+1)%3;
            pages.SetFloat("_PageOffset",next*secondsPerPage-now);
        }
    }
}
