using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QwenExhibitState : UdonSharpBehaviour
    {
        public QwenWorldController controller;
        public QwenLocalMenu localMenu;
        public QwenSocialUI social;
        public Transform center;
        [UdonSynced]
        public bool texturesShown;
        [UdonSynced]
        public int keyboardHolder = -1;
        public float keyboardAcquireDistance = 3f, keyboardReleaseDistance = 3.6f;
        private int[] candidateIds = new int[80];
        private int candidateCount;
        [UdonSynced]
        public int layerMask;
        [UdonSynced]
        public bool computing, computationPaused;
        [UdonSynced] public int progressPromptDone, progressPromptTotal, progressOutputTokens;
        [UdonSynced] public int progressOperator, progressOperatorTotal = 849, progressLayer;
        [UdonSynced]
        private int wishSequence;
        private int playedWish;
        private float lastWish = -100, nextLeaseCheck, nextSync;
        private bool dirty;
        private bool OwnsState()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        private VRCPlayerApi Sender()
        {
            return NetworkCalling.InNetworkCall ? NetworkCalling.CallingPlayer : Networking.LocalPlayer;
        }

        private bool Near(VRCPlayerApi p)
        {
            return Utilities.IsValid(p) && center != null && (p.GetPosition() - center.position).sqrMagnitude < 42.25f;
        }

        public bool IsLocalHolder()
        {
            return Utilities.IsValid(Networking.LocalPlayer) && keyboardHolder == Networking.LocalPlayer.playerId;
        }

        public void Start()
        {
            Apply();
        }

        public void SetVisibilityLocal(bool value)
        {
            if (OwnsState())
                _SetVisibility(value);
            else
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_SetVisibility), value);
        }

        [NetworkCallable(maxEventsPerSecond: 5)]
        public void _SetVisibility(bool value)
        {
            if (!OwnsState() || !Utilities.IsValid(Sender()))
                return;
            texturesShown = value;
            if (!value) ClearDetailedProgress();
            Changed();
        }

        public void WishLocal()
        {
            if (OwnsState())
                _Wish();
            else
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_Wish));
        }

        [NetworkCallable(maxEventsPerSecond: 3)]
        public void _Wish()
        {
            var sender = Sender();
            if (!OwnsState() || texturesShown || !Near(sender) || Time.unscaledTime - lastWish < 5)
                return;
            lastWish = Time.unscaledTime;
            texturesShown = true;
            wishSequence++;
            Changed();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_PlayWish), wishSequence, sender.GetPosition());
        }

        [NetworkCallable(maxEventsPerSecond: 3)]
        public void _PlayWish(int sequence, Vector3 feet)
        {
            var sender = Sender();
            if (!Utilities.IsValid(sender) || sender != Networking.GetOwner(gameObject) || sequence <= playedWish)
                return;
            playedWish = sequence;
            if (localMenu != null)
            {
                localMenu.SetSharedVisibility(true);
                localMenu.PlayWishBurst(feet);
            }
        }

        public void ClaimKeyboardLocal()
        {
            if (controller == null || !controller.supportsInference)
                return;
            if (OwnsState())
                _ClaimKeyboard(true);
            else
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_ClaimKeyboard), true);
        }

        [NetworkCallable(maxEventsPerSecond: 5)]
        public void _ClaimKeyboard(bool inferenceCapable)
        {
            var sender = Sender();
            if (!OwnsState() || !inferenceCapable || !KeyboardNear(sender, keyboardAcquireDistance))
                return;
            bool registered = false;
            for (int i = 0; i < candidateCount; i++)
                if (candidateIds[i] == sender.playerId) registered = true;
            if (!registered && candidateCount < candidateIds.Length)
                candidateIds[candidateCount++] = sender.playerId;
            RefreshKeyboardAssignment();
        }

        private bool KeyboardNear(VRCPlayerApi player, float distance)
        {
            return Utilities.IsValid(player) && center != null
                && (player.GetPosition() - center.position).sqrMagnitude <= distance * distance;
        }

        public void RefreshKeyboardAssignment()
        {
            if (!OwnsState()) return;
            // Retain a nearby holder indefinitely, even if another candidate is closer.
            if (KeyboardNear(VRCPlayerApi.GetPlayerById(keyboardHolder), keyboardReleaseDistance)) return;
            int nearest = -1;
            float best = keyboardAcquireDistance * keyboardAcquireDistance;
            for (int i = candidateCount - 1; i >= 0; i--)
            {
                var player = VRCPlayerApi.GetPlayerById(candidateIds[i]);
                if (!Utilities.IsValid(player))
                {
                    candidateIds[i] = candidateIds[--candidateCount];
                    continue;
                }
                float distance = (player.GetPosition() - center.position).sqrMagnitude;
                if (distance < best || (Mathf.Approximately(distance, best) && (nearest < 0 || player.playerId < nearest)))
                {
                    best = distance;
                    nearest = player.playerId;
                }
            }
            if (keyboardHolder != nearest)
            {
                keyboardHolder = nearest;
                Changed();
            }
        }

        public void PublishCoreLocal(int mask, bool busy, bool paused)
        {
            if (!texturesShown) mask = 0;
            if (OwnsState())
                _Core(mask, busy, paused);
            else
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_Core), mask, busy, paused);
        }

        [NetworkCallable(maxEventsPerSecond: 12)]
        public void _Core(int mask, bool busy, bool paused)
        {
            var sender = Sender();
            if (!OwnsState() || controller == null || !Utilities.IsValid(sender) || sender != Networking.GetOwner(controller.gameObject))
                return;
            mask = texturesShown ? mask & 16777215 : 0;
            if (layerMask == mask && computing == busy && computationPaused == paused)
                return;
            layerMask = mask;
            computing = busy;
            computationPaused = paused;
            Changed();
        }

        public void PublishProgressLocal(int done, int total, int tokens, int op, int opTotal, int layer)
        {
            if (!texturesShown) return;
            if (OwnsState()) _Progress(done,total,tokens,op,opTotal,layer);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner,nameof(_Progress),done,total,tokens,op,opTotal,layer);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void _Progress(int done, int total, int tokens, int op, int opTotal, int layer)
        {
            var sender = Sender();
            if (!texturesShown || !OwnsState() || controller == null || !Utilities.IsValid(sender)
                || sender != Networking.GetOwner(controller.gameObject)) return;
            total = Mathf.Clamp(total,0,8192);
            done = Mathf.Clamp(done,0,total);
            tokens = Mathf.Clamp(tokens,0,8192);
            opTotal = Mathf.Clamp(opTotal,1,4096);
            op = Mathf.Clamp(op,0,opTotal);
            layer = Mathf.Clamp(layer,0,24);
            if (progressPromptDone == done && progressPromptTotal == total && progressOutputTokens == tokens
                && progressOperator == op && progressOperatorTotal == opTotal && progressLayer == layer) return;
            progressPromptDone = done; progressPromptTotal = total; progressOutputTokens = tokens;
            progressOperator = op; progressOperatorTotal = opTotal; progressLayer = layer;
            Changed();
        }

        private void ClearDetailedProgress()
        {
            layerMask = 0;
            progressPromptDone = 0; progressPromptTotal = 0; progressOutputTokens = 0;
            progressOperator = 0; progressOperatorTotal = 849; progressLayer = 0;
        }

        public void Update()
        {
            if (!OwnsState())
                return;
            if (Time.unscaledTime >= nextLeaseCheck)
            {
                nextLeaseCheck = Time.unscaledTime + .25f;
                RefreshKeyboardAssignment();
            }

            if (dirty && Time.unscaledTime >= nextSync && !Networking.IsClogged)
            {
                dirty = false;
                nextSync = Time.unscaledTime + .1f;
                RequestSerialization();
            }
        }

        private void Changed()
        {
            dirty = true;
            Apply();
        }

        public void Apply()
        {
            if (localMenu != null)
                localMenu.SetSharedVisibility(texturesShown);
            if (social != null)
                social.PermissionChanged();
        }

        public override void OnDeserialization()
        {
            Apply();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!result.success)
                dirty = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!OwnsState())
                return;
            for (int i = candidateCount - 1; i >= 0; i--)
                if (candidateIds[i] == player.playerId) candidateIds[i] = candidateIds[--candidateCount];
            if (player.playerId == keyboardHolder) keyboardHolder = -1;
            RefreshKeyboardAssignment();
            Changed();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (OwnsState())
            {
                nextLeaseCheck = 0;
                dirty = true;
            }

            Apply();
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return Utilities.IsValid(requestedOwner) && requestedOwner.isMaster;
        }
    }
}
