using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    /// <summary>Speichert die Clienteinstellungen am Charakter (Kapitel 28/29/59), damit sie auf jedem Rechner gleich sind.</summary>
    public sealed class C_SaveSettings : Message
    {
        /// <summary>Obergrenze für das JSON; größere Anfragen lehnt der Server ab.</summary>
        public const int MaxLength = 32 * 1024;
        public override Opcode Op => Opcode.C_SaveSettings;
        public string Json = "";
        public override void Write(NetDataWriter w) => w.Put(Json);
        public override void Read(NetDataReader r) => Json = r.GetString(MaxLength + 1);
    }

    /// <summary>Gespeicherte Einstellungen beim Betreten der Welt; leer = Standard.</summary>
    public sealed class S_Settings : Message
    {
        public override Opcode Op => Opcode.S_Settings;
        public string Json = "";
        public override void Write(NetDataWriter w) => w.Put(Json);
        public override void Read(NetDataReader r) => Json = r.GetString();
    }

    /// <summary>Entdeckte Gebiete für Weltkarte und Nebel des Krieges. Full = vollständige Liste, sonst nur neue Gebiete.</summary>
    public sealed class S_Exploration : Message
    {
        public override Opcode Op => Opcode.S_Exploration;
        public bool Full;
        public List<string> Areas = new List<string>();
        public override void Write(NetDataWriter w) { w.Put(Full); w.PutStrings(Areas); }
        public override void Read(NetDataReader r) { Full = r.GetBool(); Areas = r.GetStrings(); }
    }
}
