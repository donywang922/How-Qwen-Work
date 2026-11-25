using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenObservationPlatform : UdonSharpBehaviour
    {
        public Transform deck, recoveryPoint;
        public Renderer deckRenderer;
        public Collider deckCollider;
        public Slider heightSlider;
        public Text heightLabel;
        public QwenLocalization localization;
        public float radius = 11.2f, maximumHeight = 5.5f, movementSpeed = 1.5f;
        public float hiddenHeight = -.04f;
        private float targetHeight = -.04f, currentHeight = -.04f, nextLabel, hintUntil;
        private bool settingSlider, hasRidden, jumping, initialized;
        private float jumpBlockUntil;

        public void Start()
        {
            if (initialized) return;
            initialized = true;
            ResetToLowest();
        }

        public void HeightChanged()
        {
            if (settingSlider || heightSlider == null) return;
            var player = Networking.LocalPlayer;
            float requested = Mathf.Clamp(heightSlider.value, 0, maximumHeight);
            if (requested > .02f && Utilities.IsValid(player) && !Inside(player.GetPosition(), radius - .15f))
            {
                ResetToLowest();
                hintUntil = Time.unscaledTime + 2;
                RefreshLocalizedLabel();
                return;
            }
            targetHeight = requested <= .02f ? hiddenHeight : Mathf.Max(.06f, requested);
            RefreshLabel();
        }

        public void ResetToLowest()
        {
            targetHeight = currentHeight = hiddenHeight;
            hasRidden = jumping = false;
            jumpBlockUntil=0;
            settingSlider = true;
            if (heightSlider != null) heightSlider.value = 0;
            settingSlider = false;
            ApplyDeck();
            RefreshLabel();
        }

        private bool Inside(Vector3 position, float distance)
        {
            if (deck == null) return false;
            Vector3 delta = position - deck.position;
            return delta.x * delta.x + delta.z * delta.z < distance * distance;
        }

        private void ApplyDeck()
        {
            if (deck != null)
            {
                Vector3 position = deck.position;
                position.y = currentHeight;
                deck.position = position;
            }
            bool visible = currentHeight > .01f;
            if (deckRenderer != null) deckRenderer.enabled = visible;
            if (deckCollider != null) deckCollider.enabled = visible;
        }

        private void RefreshLabel()
        {
            if (heightLabel == null) return;
            if (localization == null) return;
            heightLabel.text = Time.unscaledTime < hintUntil ? localization.T(QwenLocalization.STEP_ONTO)
                : targetHeight <= 0 ? localization.T(QwenLocalization.FLOOR_HIDDEN)
                : localization.F(QwenLocalization.FLOOR_HEIGHT,Mathf.Max(0,currentHeight).ToString("F1"),targetHeight.ToString("F1"),"","");
        }

        public void RefreshLocalizedLabel() { RefreshLabel(); }

        public void Update()
        {
            if (!initialized) Start();
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player) || deck == null) return;
            Vector3 feet = player.GetPosition();
            if (feet.y < -2)
            {
                ResetToLowest();
                if (recoveryPoint != null)
                {
                    player.SetVelocity(Vector3.zero);
                    player.TeleportTo(recoveryPoint.position, recoveryPoint.rotation);
                }
                return;
            }

            bool inside = Inside(feet, radius);
            if (currentHeight > .01f && hasRidden
                && (feet.y < currentHeight - .35f || (!inside && feet.y < .08f && player.IsPlayerGrounded())))
            {
                // Let the player land naturally on the lower ground; reset their empty deck.
                ResetToLowest();
                return;
            }

            bool onSurface = inside && Mathf.Abs(feet.y - Mathf.Max(0, currentHeight)) < .18f;
            if (jumping && Time.unscaledTime>=jumpBlockUntil && player.IsPlayerGrounded() && player.GetVelocity().y <= 0) jumping = false;
            bool carry = onSurface && !jumping && player.GetVelocity().y < .5f;
            if (carry && currentHeight > .01f) hasRidden = true;
            float before = currentHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, movementSpeed * Time.deltaTime);
            ApplyDeck();
            float delta = Mathf.Max(0, currentHeight) - Mathf.Max(0, before);
            if (carry && Mathf.Abs(delta) > .00001f)
            {
                hasRidden = currentHeight > .01f;
                // Only carry during a height change. Walking and jumping remain available.
                player.TeleportTo(feet + Vector3.up * delta, player.GetRotation(),
                    VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, true);
            }
            if (Time.unscaledTime >= nextLabel)
            {
                nextLabel = Time.unscaledTime + .1f;
                if (Time.unscaledTime >= hintUntil) RefreshLabel();
            }
        }

        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            if (value)
            {
                jumping = true;
                // Input precedes the physics jump impulse: do not immediately clear
                // this latch just because the previous frame's vertical speed was zero.
                jumpBlockUntil=Time.unscaledTime+.20f;
            }
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal) ResetToLowest();
        }
    }
}
