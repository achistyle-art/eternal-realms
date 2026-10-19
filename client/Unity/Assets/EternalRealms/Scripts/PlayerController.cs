using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Steuerung: WASD laufen (relativ zur Kamera), Q/E seitwärts, Leertaste springen, Num-Lock Autolauf,
    /// Linksklick Ziel wählen, Rechtsklick angreifen/ansprechen, Tab nächstes Ziel, 1–0 Aktionsleiste, F interagieren.
    /// Der Client meldet Bewegungen; der Server prüft Tempo und Kontrolleffekte und korrigiert notfalls.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        public float RunSpeed = 7f;
        private float _sendTimer;
        private bool _moved, _autoRun;
        private float _verticalVelocity, _height;
        private Vector3 _lastSent;

        private GameClient Client => NetworkManager.Instance.Client;

        private void Update()
        {
            var me = Client.Me;
            if (me == null || !Client.InWorld) return;
            HandleTargeting(me);
            if (GameUI.TypingInChat) return;
            HandleHotkeys(me);
            HandleMovement(me);
        }

        private void HandleMovement(ClientEntity me)
        {
            bool canMove = (me.Flags & (UnitFlags.Stunned | UnitFlags.Rooted | UnitFlags.Feared | UnitFlags.Dead)) == 0;
            var cam = Camera.main!.GetComponent<ThirdPersonCamera>();
            float yaw = cam.Yaw;
            if (Input.GetKeyDown(KeyCode.Numlock)) _autoRun = !_autoRun;
            float fwd = Input.GetAxisRaw("Vertical") + (_autoRun ? 1 : 0) + (Input.GetMouseButton(0) && Input.GetMouseButton(1) ? 1 : 0);
            float side = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            float turn = Input.GetAxisRaw("Horizontal");
            if (cam.RotatingCharacter) side += turn; else cam.Yaw += turn * 120f * Time.deltaTime;
            if (Mathf.Abs(fwd) > 0.01f) _autoRun = _autoRun && fwd > 0;
            var dir = Quaternion.Euler(0, yaw, 0) * new Vector3(Mathf.Clamp(side, -1, 1), 0, Mathf.Clamp(fwd, -1, 1));
            float speed = RunSpeed * ((me.Flags & UnitFlags.Ghost) != 0 ? 1.25f : 1f) * (fwd < 0 ? 0.6f : 1f);
            var pos = me.Position.ToUnity();
            if (canMove && dir.sqrMagnitude > 0.01f)
            {
                pos += dir.normalized * speed * Time.deltaTime;
                _moved = true;
            }
            // Sprung ist rein optisch (Höhe wird nur dargestellt).
            if (Input.GetKeyDown(KeyCode.Space) && _height <= 0.01f && canMove) _verticalVelocity = 6f;
            _verticalVelocity -= 18f * Time.deltaTime;
            _height = Mathf.Max(0, _height + _verticalVelocity * Time.deltaTime);
            pos.y = _height;
            float facing = Conversions.YawToFacing(yaw);
            if (canMove) me.Position = pos.ToShared();
            var view = WorldRenderer.Instance.ViewOf(me.Id);
            if (view != null) view.transform.rotation = Quaternion.Euler(0, yaw, 0);
            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0 && (_moved || (pos - _lastSent).sqrMagnitude > 0.0001f))
            {
                _sendTimer = 0.1f;
                Client.Move(pos.ToShared(), facing, _height > 0);
                _lastSent = pos;
                _moved = false;
            }
        }

        private void HandleTargeting(ClientEntity me)
        {
            if (GameUI.PointerOverUi) return;
            bool left = Input.GetMouseButtonUp(0), right = Input.GetMouseButtonUp(1);
            if (!left && !right) return;
            if (Camera.main == null) return;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 200f)) return;
            var view = hit.collider.GetComponentInParent<EntityView>();
            if (view == null) return;
            var e = Client.State.Get(view.EntityId);
            if (e == null) return;
            Client.Target(e.Id);
            me.TargetId = e.Id;
            if (right) Activate(e);
        }

        /// <summary>Rechtsklick: Gegner angreifen, NPC/Objekt/Leiche benutzen.</summary>
        public void Activate(ClientEntity e)
        {
            bool hostile = e.Faction == Faction.Hostile || e.Faction == Faction.Neutral || (e.Kind == EntityKind.Player && e.Faction != Client.Me!.Faction);
            if (e.IsDead || e.Kind == EntityKind.GameObject || !hostile) Client.Send(new C_Interact { EntityId = e.Id });
            else Client.Send(new C_AttackStart { TargetId = e.Id });
        }

        private void HandleHotkeys(ClientEntity me)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var next = Client.NextEnemyTarget(me.TargetId);
                if (next != null) { Client.Target(next.Id); me.TargetId = next.Id; }
            }
            if (Input.GetKeyDown(KeyCode.Escape) && !GameUI.AnyWindowOpen) { Client.Target(0); me.TargetId = 0; }
            if (Input.GetKeyDown(KeyCode.F) && Client.State.Get(me.TargetId) is ClientEntity t) Activate(t);
            for (int i = 0; i < 10; i++)
            {
                var key = i == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + i;
                if (Input.GetKeyDown(key)) GameUI.Instance.UseActionSlot(i);
            }
        }
    }
}
