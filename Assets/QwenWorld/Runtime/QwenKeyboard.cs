using UdonSharp;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenKeyboard : UdonSharpBehaviour
    {
        public QwenSocialUI social;
        public override void Interact()
        {
            if (social != null)
                social.KeyboardInteract();
        }
    }
}
