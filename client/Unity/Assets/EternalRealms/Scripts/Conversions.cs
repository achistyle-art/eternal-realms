using EternalRealms.Shared;
using UnityEngine;

namespace EternalRealms.Client
{
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToShared(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        /// <summary>Server-Blickrichtung (Radiant, 0 = +Z) in Unity-Gradzahl um die Y-Achse.</summary>
        public static float FacingToYaw(float facing) => facing * Mathf.Rad2Deg;
        public static float YawToFacing(float yaw) => yaw * Mathf.Deg2Rad;

        public static Color ClassColor(CharClass c) => c switch
        {
            CharClass.Warrior => new Color(0.78f, 0.61f, 0.43f),
            CharClass.Paladin => new Color(0.96f, 0.55f, 0.73f),
            CharClass.Hunter => new Color(0.67f, 0.83f, 0.45f),
            CharClass.Rogue => new Color(1f, 0.96f, 0.41f),
            CharClass.Priest => Color.white,
            CharClass.Shaman => new Color(0f, 0.44f, 0.87f),
            CharClass.Mage => new Color(0.41f, 0.8f, 0.94f),
            CharClass.Warlock => new Color(0.58f, 0.51f, 0.79f),
            CharClass.Druid => new Color(1f, 0.49f, 0.04f),
            _ => Color.gray,
        };
    }
}
