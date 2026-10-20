using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Frei drehbare Third-Person-Kamera: linke Maustaste dreht die Kamera, rechte auch die Figur, Mausrad zoomt.
    /// Empfindlichkeit, maximale Entfernung, Y-Umkehr und Kollision kommen aus den Einstellungen (Kapitel 29).
    /// </summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public float Distance = 9f, MinDistance = 2f;
        public float Yaw, Pitch = 20f;
        public bool RotatingCharacter { get; private set; }
        private float _shownDistance = 9f;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void LateUpdate()
        {
            var net = NetworkManager.Instance;
            var me = net.Client.Me;
            var view = me != null ? WorldRenderer.Instance.ViewOf(me.Id) : null;
            if (view == null) return;
            var s = net.Client.Settings;
            bool lmb = Input.GetMouseButton(0), rmb = Input.GetMouseButton(1);
            if ((lmb || rmb) && !GameUI.PointerOverUi)
            {
                Yaw += Input.GetAxis("Mouse X") * s.CameraSensitivity;
                float dy = Input.GetAxis("Mouse Y") * s.CameraSensitivity * (s.InvertMouseY ? -1 : 1);
                Pitch = Mathf.Clamp(Pitch - dy, -30f, 80f);
            }
            RotatingCharacter = rmb && !GameUI.PointerOverUi;
            if (!GameUI.PointerOverUi) Distance = Mathf.Clamp(Distance - Input.mouseScrollDelta.y * 1.5f, MinDistance, s.CameraMaxDistance);
            Distance = Mathf.Min(Distance, s.CameraMaxDistance);
            var pivot = view.transform.position + Vector3.up * 1.2f;
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            float wanted = Distance;
            if (s.CameraCollision) wanted = Mathf.Min(wanted, FreeDistance(pivot, rot * Vector3.back, Distance));
            // Heranrücken sofort, Zurückweichen sanft – so springt die Kamera nicht
            _shownDistance = wanted < _shownDistance ? wanted : Mathf.MoveTowards(_shownDistance, wanted, 12f * Time.deltaTime);
            var desired = pivot + rot * Vector3.back * _shownDistance;
            if (desired.y < 0.3f) desired.y = 0.3f;
            transform.position = desired;
            transform.LookAt(pivot);
        }

        /// <summary>Abstand bis zum ersten Hindernis hinter der Figur; Figuren und Kreaturen (EntityView) zählen nicht.</summary>
        private float FreeDistance(Vector3 pivot, Vector3 dir, float max)
        {
            int n = Physics.SphereCastNonAlloc(pivot, 0.3f, dir, _hits, max, ~0, QueryTriggerInteraction.Ignore);
            float best = max;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.GetComponentInParent<EntityView>() != null) continue;
                if (_hits[i].distance > 0 && _hits[i].distance < best) best = _hits[i].distance;
            }
            return Mathf.Max(MinDistance * 0.5f, best - 0.2f);
        }
    }
}
