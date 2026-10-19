using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Frei drehbare Third-Person-Kamera: linke Maustaste dreht die Kamera, rechte auch die Figur, Mausrad zoomt.</summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public float Distance = 9f, MinDistance = 2f, MaxDistance = 30f;
        public float Yaw, Pitch = 20f;
        public float Sensitivity = 4f;
        public bool RotatingCharacter { get; private set; }

        private void LateUpdate()
        {
            var me = NetworkManager.Instance.Client.Me;
            var view = me != null ? WorldRenderer.Instance.ViewOf(me.Id) : null;
            if (view == null) return;
            bool lmb = Input.GetMouseButton(0), rmb = Input.GetMouseButton(1);
            if ((lmb || rmb) && !GameUI.PointerOverUi)
            {
                Yaw += Input.GetAxis("Mouse X") * Sensitivity;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxis("Mouse Y") * Sensitivity, -30f, 80f);
            }
            RotatingCharacter = rmb && !GameUI.PointerOverUi;
            if (!GameUI.PointerOverUi) Distance = Mathf.Clamp(Distance - Input.mouseScrollDelta.y * 1.5f, MinDistance, MaxDistance);
            var pivot = view.transform.position + Vector3.up * 1.2f;
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            var desired = pivot - rot * Vector3.forward * Distance;
            if (desired.y < 0.3f) desired.y = 0.3f;
            transform.position = desired;
            transform.LookAt(pivot);
        }
    }
}
