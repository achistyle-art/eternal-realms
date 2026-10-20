using EternalRealms.Shared.Client.Anim;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Figur aus Grundkörpern mit Gelenken (Kapitel 33), solange keine Modelle eingebunden sind: Mensch (Kopf, Rumpf,
    /// Arme, Beine), Vierbeiner oder Klumpen. Die Posen kommen aus <see cref="AnimPose"/>; Schritte melden Fußtritte.
    /// </summary>
    public sealed class ProceduralRig : MonoBehaviour
    {
        public enum Plan { Humanoid, Quadruped, Blob }

        public Plan BodyPlan { get; private set; }
        private Transform _hips = null!, _torso = null!, _head = null!, _armL = null!, _armR = null!, _legL = null!, _legR = null!;
        private Renderer[] _renderers = System.Array.Empty<Renderer>();
        private float _lastHalf;
        /// <summary>Ein Fuß hat in diesem Bild den Boden berührt.</summary>
        public bool StepThisFrame { get; private set; }

        public static Plan PlanFor(string family) => family switch
        {
            "Wolf" or "Hyena" or "Boar" or "Lizard" or "Spider" or "Beetle" or "Wyrm" => Plan.Quadruped,
            "Fungus" => Plan.Blob,
            _ => Plan.Humanoid,
        };

        public static ProceduralRig Build(GameObject root, Plan plan)
        {
            var rig = root.AddComponent<ProceduralRig>();
            rig.BodyPlan = plan;
            rig._hips = Node("Hüfte", root.transform, Vector3.zero);
            switch (plan)
            {
                case Plan.Quadruped:
                    rig._torso = Part("Rumpf", PrimitiveType.Capsule, rig._hips, new Vector3(0, 0.1f, 0), new Vector3(0.6f, 0.55f, 0.6f), Quaternion.Euler(90, 0, 0));
                    rig._head = Part("Kopf", PrimitiveType.Sphere, rig._hips, new Vector3(0, 0.3f, 0.75f), Vector3.one * 0.45f, Quaternion.identity);
                    rig._armL = Limb("Vorderbein L", rig._hips, new Vector3(-0.22f, -0.05f, 0.4f), 0.5f, 0.14f);
                    rig._armR = Limb("Vorderbein R", rig._hips, new Vector3(0.22f, -0.05f, 0.4f), 0.5f, 0.14f);
                    rig._legL = Limb("Hinterbein L", rig._hips, new Vector3(-0.22f, -0.05f, -0.4f), 0.5f, 0.15f);
                    rig._legR = Limb("Hinterbein R", rig._hips, new Vector3(0.22f, -0.05f, -0.4f), 0.5f, 0.15f);
                    break;
                case Plan.Blob:
                    rig._torso = Part("Körper", PrimitiveType.Sphere, rig._hips, new Vector3(0, -0.3f, 0), new Vector3(1.1f, 0.9f, 1.1f), Quaternion.identity);
                    rig._head = Part("Hut", PrimitiveType.Sphere, rig._hips, new Vector3(0, 0.25f, 0), new Vector3(1.4f, 0.5f, 1.4f), Quaternion.identity);
                    rig._armL = Node("L", rig._hips, Vector3.zero); rig._armR = Node("R", rig._hips, Vector3.zero);
                    rig._legL = Node("FL", rig._hips, Vector3.zero); rig._legR = Node("FR", rig._hips, Vector3.zero);
                    break;
                default:
                    rig._torso = Part("Rumpf", PrimitiveType.Capsule, rig._hips, new Vector3(0, 0.15f, 0), new Vector3(0.55f, 0.5f, 0.35f), Quaternion.identity);
                    rig._head = Part("Kopf", PrimitiveType.Sphere, rig._hips, new Vector3(0, 0.8f, 0), Vector3.one * 0.36f, Quaternion.identity);
                    rig._armL = Limb("Arm L", rig._hips, new Vector3(-0.36f, 0.55f, 0), 0.65f, 0.13f);
                    rig._armR = Limb("Arm R", rig._hips, new Vector3(0.36f, 0.55f, 0), 0.65f, 0.13f);
                    rig._legL = Limb("Bein L", rig._hips, new Vector3(-0.14f, -0.3f, 0), 0.7f, 0.16f);
                    rig._legR = Limb("Bein R", rig._hips, new Vector3(0.14f, -0.3f, 0), 0.7f, 0.16f);
                    break;
            }
            rig._renderers = root.GetComponentsInChildren<Renderer>();
            return rig;
        }

        private static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        private static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            return go.transform;
        }

        /// <summary>Gelenk mit nach unten hängendem Glied – gedreht wird das Gelenk, nicht das Glied.</summary>
        private static Transform Limb(string name, Transform parent, Vector3 joint, float length, float thickness)
        {
            var pivot = Node(name, parent, joint);
            Part(name + " Glied", PrimitiveType.Capsule, pivot, new Vector3(0, -length / 2, 0), new Vector3(thickness, length / 2, thickness), Quaternion.identity);
            return pivot;
        }

        public void SetColor(Color body, Color accent)
        {
            foreach (var r in _renderers)
            {
                bool isAccent = r.transform == _head || r.transform.parent == _armL || r.transform.parent == _armR;
                r.material.color = isAccent ? accent : body;
            }
        }

        public void SetVisible(bool visible)
        {
            foreach (var r in _renderers) if (r != null) r.enabled = visible;
        }

        /// <summary>Pose anwenden. Winkel in Grad: X = vor/zurück, Z = seitlich.</summary>
        public void Apply(AnimPose pose)
        {
            float p = pose.BasePhase * Mathf.PI * 2;
            float swing = 0, bob = 0, lean = 0, armL = 0, armR = 0, armSideL = 0, armSideR = 0, headNod = 0, headTurn = 0, legL = 0, legR = 0, hipsY = 0, roll = 0;
            switch (pose.Base)
            {
                case AnimClip.Walk: swing = 28; bob = 0.04f; break;
                case AnimClip.Run: swing = 50; bob = 0.08f; lean = 12; break;
                case AnimClip.Backpedal: swing = -22; bob = 0.03f; break;
                case AnimClip.CombatIdle: lean = 8; armL = -35; armR = -40; bob = 0.015f; break;
                case AnimClip.Mounted: legL = legR = -80; armL = armR = -30; hipsY = 0.05f; break;
                case AnimClip.Stun: headNod = 20; lean = -5; armSideL = armSideR = 5; break;
                case AnimClip.Death: roll = 90 * pose.BasePhase; hipsY = -0.6f * pose.BasePhase; break;
                case AnimClip.Dead: roll = 90; hipsY = -0.6f; break;
                case AnimClip.Ghost: bob = 0.1f; armSideL = 20; armSideR = -20; break;
                case AnimClip.Sit: legL = legR = -85 * pose.BasePhase; hipsY = -0.5f * pose.BasePhase; break;
                case AnimClip.Sleep: roll = 90 * pose.BasePhase; hipsY = -0.6f * pose.BasePhase; break;
                case AnimClip.Kneel: legL = -90 * pose.BasePhase; hipsY = -0.35f * pose.BasePhase; lean = 10; break;
                case AnimClip.Dance:
                    armL = -150 + 30 * Mathf.Sin(p * 2); armR = -150 - 30 * Mathf.Sin(p * 2); hipsY = 0.06f * Mathf.Abs(Mathf.Sin(p * 2));
                    roll = 8 * Mathf.Sin(p); break;
                default: bob = 0.01f; break;
            }
            float s = Mathf.Sin(p);
            if (swing != 0)
            {
                legL += swing * s; legR -= swing * s;
                armL -= swing * 0.8f * s; armR += swing * 0.8f * s;
            }
            if (pose.Overlay is AnimClip o)
            {
                float w = pose.OverlayWeight, t = Mathf.Clamp01(pose.OverlayProgress);
                float up = Mathf.Sin(t * Mathf.PI);
                switch (o)
                {
                    case AnimClip.AttackMelee: armR = Mathf.Lerp(armR, t < 0.4f ? -150 * (t / 0.4f) : -150 + 210 * ((t - 0.4f) / 0.6f), w); lean += 10 * up * w; break;
                    case AnimClip.AttackUnarmed: armR = Mathf.Lerp(armR, -90 * up, w); lean += 8 * up * w; break;
                    case AnimClip.Shoot: armL = Mathf.Lerp(armL, -90, w); armR = Mathf.Lerp(armR, -90 + 20 * up, w); break;
                    case AnimClip.CastLoop: armL = Mathf.Lerp(armL, -60 + 8 * Mathf.Sin(t * 12), w); armR = Mathf.Lerp(armR, -60 - 8 * Mathf.Sin(t * 12), w); break;
                    case AnimClip.Channel: armL = Mathf.Lerp(armL, -100, w); armR = Mathf.Lerp(armR, -100, w); break;
                    case AnimClip.CastRelease: armL = Mathf.Lerp(armL, -120 * up, w); armR = Mathf.Lerp(armR, -120 * up, w); lean -= 6 * up * w; break;
                    case AnimClip.Hit: lean -= 15 * up * w; headNod -= 10 * up * w; break;
                    case AnimClip.Dodge: roll += 15 * up * w; hipsY -= 0.1f * up * w; break;
                    case AnimClip.Block: armL = Mathf.Lerp(armL, -80, w * up); break;
                    case AnimClip.Gather: lean += 40 * up * w; armL = Mathf.Lerp(armL, -60 * up, w); armR = Mathf.Lerp(armR, -60 * up, w); break;
                    case AnimClip.Jump: hipsY += 0.3f * up * w; legL = Mathf.Lerp(legL, -30 * up, w); legR = Mathf.Lerp(legR, -30 * up, w); break;
                    case AnimClip.Wave: armR = Mathf.Lerp(armR, -160, w); armSideR = 25 * Mathf.Sin(t * Mathf.PI * 8) * w; break;
                    case AnimClip.Bow: lean += 45 * up * w; break;
                    case AnimClip.Cheer: armL = Mathf.Lerp(armL, -170, w); armR = Mathf.Lerp(armR, -170, w); hipsY += 0.08f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4)) * w; break;
                    case AnimClip.Laugh: lean -= 10 * up * w; headNod -= 15 * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 6)) * w; break;
                    case AnimClip.Cry: headNod += 30 * w; armL = Mathf.Lerp(armL, -120, w * 0.8f); armR = Mathf.Lerp(armR, -120, w * 0.8f); break;
                    case AnimClip.Point: armR = Mathf.Lerp(armR, -90, w); break;
                    case AnimClip.Salute: armR = Mathf.Lerp(armR, -150, w); armSideR = -40 * w; break;
                    case AnimClip.Flex: armSideL = 80 * w; armSideR = -80 * w; armL = Mathf.Lerp(armL, -60, w); armR = Mathf.Lerp(armR, -60, w); break;
                    case AnimClip.Shrug: hipsY += 0.03f * up * w; armSideL = 20 * up * w; armSideR = -20 * up * w; break;
                    case AnimClip.Applaud: armL = Mathf.Lerp(armL, -80, w); armR = Mathf.Lerp(armR, -80, w); armSideL = 15 * Mathf.Sin(t * Mathf.PI * 10) * w; armSideR = -armSideL; break;
                    case AnimClip.Nod: headNod += 25 * Mathf.Sin(t * Mathf.PI * 4) * w; break;
                    case AnimClip.HeadShake: headTurn = 30 * Mathf.Sin(t * Mathf.PI * 6) * w; break;
                }
            }
            if (BodyPlan == Plan.Blob)
            {
                float squash = 1 + 0.08f * Mathf.Sin(p * 2);
                _torso.localScale = new Vector3(1.1f * squash, 0.9f / squash, 1.1f * squash);
                _hips.localRotation = Quaternion.Euler(0, 0, roll);
                _hips.localPosition = new Vector3(0, hipsY, 0);
            }
            else
            {
                _hips.localPosition = new Vector3(0, (BodyPlan == Plan.Quadruped ? -0.45f : 0) + hipsY + bob * Mathf.Abs(Mathf.Cos(p)), 0);
                _hips.localRotation = Quaternion.Euler(lean, 0, roll);
                _legL.localRotation = Quaternion.Euler(legL, 0, 0);
                _legR.localRotation = Quaternion.Euler(legR, 0, 0);
                if (BodyPlan == Plan.Quadruped)
                {
                    // Vierbeiner: Vorder- und Hinterbeine diagonal gegengleich
                    _armL.localRotation = Quaternion.Euler(-legL, 0, 0);
                    _armR.localRotation = Quaternion.Euler(-legR, 0, 0);
                }
                else
                {
                    _armL.localRotation = Quaternion.Euler(armL, 0, armSideL);
                    _armR.localRotation = Quaternion.Euler(armR, 0, armSideR);
                }
                _head.localRotation = Quaternion.Euler(headNod, headTurn, 0);
            }
            // Fußtritt, wenn der Schrittzyklus eine Halbphase vollendet
            bool walking = pose.Base is AnimClip.Walk or AnimClip.Run or AnimClip.Backpedal;
            float half = pose.BasePhase * 2 % 1f;
            StepThisFrame = walking && half < _lastHalf;
            _lastHalf = walking ? half : 0;
        }
    }
}
