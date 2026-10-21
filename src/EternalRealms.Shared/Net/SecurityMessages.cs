using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    public enum AccountAction : byte
    {
        /// <summary>Nur den aktuellen Stand abfragen.</summary>
        Info,
        /// <summary>Zwei-Faktor-Anmeldung einrichten: Server erzeugt ein Geheimnis, aktiv erst nach Bestätigung.</summary>
        BeginTwoFactor,
        /// <summary>Einrichtung mit einem Code aus der App bestätigen.</summary>
        ConfirmTwoFactor,
        /// <summary>Abschalten – verlangt Passwort und aktuellen Code.</summary>
        DisableTwoFactor,
        ChangePassword,
    }

    /// <summary>Kontoschutz (Kapitel 40): Zwei-Faktor-Anmeldung und Passwortänderung – auf der Charakterauswahl und im Spiel.</summary>
    public sealed class C_AccountSecurity : Message
    {
        public override Opcode Op => Opcode.C_AccountSecurity;
        public AccountAction Action;
        public string Password = "", NewPassword = "", Code = "";
        public override void Write(NetDataWriter w) { w.Put((byte)Action); w.Put(Password); w.Put(NewPassword); w.Put(Code); }
        public override void Read(NetDataReader r) { Action = (AccountAction)r.GetByte(); Password = r.GetString(); NewPassword = r.GetString(); Code = r.GetString(); }
    }

    public sealed class S_AccountSecurity : Message
    {
        public override Opcode Op => Opcode.S_AccountSecurity;
        public bool TwoFactorEnabled;
        /// <summary>Während der Einrichtung: Geheimnis (Base32) und otpauth-Adresse für die Authenticator-App.</summary>
        public string PendingSecret = "", PendingUri = "";
        public bool Success;
        public string Message = "";
        /// <summary>Nach einer Passwortänderung: neues Wiederverbindungstoken (alle alten sind ungültig).</summary>
        public string Token = "";
        public override void Write(NetDataWriter w) { w.Put(TwoFactorEnabled); w.Put(PendingSecret); w.Put(PendingUri); w.Put(Success); w.Put(Message); w.Put(Token); }
        public override void Read(NetDataReader r) { TwoFactorEnabled = r.GetBool(); PendingSecret = r.GetString(); PendingUri = r.GetString(); Success = r.GetBool(); Message = r.GetString(); Token = r.GetString(); }
    }

    /// <summary>Warteschlange beim Weltbetritt, wenn der Realm voll ist (Kapitel 54).</summary>
    public sealed class S_QueueStatus : Message
    {
        public override Opcode Op => Opcode.S_QueueStatus;
        /// <summary>Platz in der Schlange (1 = als Nächstes); 0 = nicht mehr in der Schlange.</summary>
        public int Position, Length;
        public string Message = "";
        public override void Write(NetDataWriter w) { w.Put(Position); w.Put(Length); w.Put(Message); }
        public override void Read(NetDataReader r) { Position = r.GetInt(); Length = r.GetInt(); Message = r.GetString(); }
    }

    /// <summary>Vom Server beendete Verbindung: Grund und ob der Client es automatisch erneut versuchen soll.</summary>
    public sealed class S_Goodbye : Message
    {
        public override Opcode Op => Opcode.S_Goodbye;
        public string Reason = "";
        public bool MayReconnect;
        public override void Write(NetDataWriter w) { w.Put(Reason); w.Put(MayReconnect); }
        public override void Read(NetDataReader r) { Reason = r.GetString(); MayReconnect = r.GetBool(); }
    }
}
