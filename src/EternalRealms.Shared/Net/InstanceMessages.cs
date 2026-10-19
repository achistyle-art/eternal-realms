using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    public sealed class C_ResetInstances : Message
    {
        public override Opcode Op => Opcode.C_ResetInstances;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_InstanceInfo : Message
    {
        public override Opcode Op => Opcode.C_InstanceInfo;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class InstanceBindData
    {
        public string ZoneId = "", Name = "";
        public long InstanceId;
        public int BossesKilled, BossesTotal;
        /// <summary>Stunden bis zum Reset; −1 = Dungeonbindung ohne Sperre.</summary>
        public float HoursLeft;
        public void Write(NetDataWriter w) { w.Put(ZoneId); w.Put(Name); w.Put(InstanceId); w.Put(BossesKilled); w.Put(BossesTotal); w.Put(HoursLeft); }
        public static InstanceBindData Read(NetDataReader r) => new InstanceBindData
        {
            ZoneId = r.GetString(), Name = r.GetString(), InstanceId = r.GetLong(), BossesKilled = r.GetInt(), BossesTotal = r.GetInt(), HoursLeft = r.GetFloat(),
        };
    }

    public sealed class S_InstanceInfo : Message
    {
        public override Opcode Op => Opcode.S_InstanceInfo;
        public List<InstanceBindData> Binds = new List<InstanceBindData>();
        public override void Write(NetDataWriter w) => w.PutList(Binds, (x, b) => b.Write(x));
        public override void Read(NetDataReader r) => Binds = r.GetList(InstanceBindData.Read);
    }
}
