using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    public sealed class AchievementProgressData
    {
        public string Id = "";
        public List<int> Counts = new List<int>();
        /// <summary>Abschlusszeit (Unix-Sekunden, UTC); 0 = offen.</summary>
        public long CompletedAt;
        public void Write(NetDataWriter w) { w.Put(Id); w.PutList(Counts, (x, v) => x.Put(v)); w.Put(CompletedAt); }
        public static AchievementProgressData Read(NetDataReader r) => new AchievementProgressData { Id = r.GetString(), Counts = r.GetList(x => x.GetInt()), CompletedAt = r.GetLong() };
    }

    /// <summary>Alle Erfolge mit Fortschritt, freigeschaltete Titel und der gewählte Titel (Kapitel 38).</summary>
    public sealed class S_Achievements : Message
    {
        public override Opcode Op => Opcode.S_Achievements;
        public List<AchievementProgressData> Progress = new List<AchievementProgressData>();
        public List<string> Titles = new List<string>();
        public string ActiveTitle = "";
        public int Points;
        public override void Write(NetDataWriter w) { w.PutList(Progress, (x, p) => p.Write(x)); w.PutStrings(Titles); w.Put(ActiveTitle); w.Put(Points); }
        public override void Read(NetDataReader r) { Progress = r.GetList(AchievementProgressData.Read); Titles = r.GetStrings(); ActiveTitle = r.GetString(); Points = r.GetInt(); }
    }

    public sealed class S_AchievementEarned : Message
    {
        public override Opcode Op => Opcode.S_AchievementEarned;
        public string Id = "";
        public string Name = "";
        public int Points;
        public string Title = "";
        public override void Write(NetDataWriter w) { w.Put(Id); w.Put(Name); w.Put(Points); w.Put(Title); }
        public override void Read(NetDataReader r) { Id = r.GetString(); Name = r.GetString(); Points = r.GetInt(); Title = r.GetString(); }
    }

    /// <summary>Titel wählen; leer = keinen Titel tragen.</summary>
    public sealed class C_SetTitle : Message
    {
        public override Opcode Op => Opcode.C_SetTitle;
        public string Title = "";
        public override void Write(NetDataWriter w) => w.Put(Title);
        public override void Read(NetDataReader r) => Title = r.GetString();
    }

    /// <summary>Aktueller Tutorialschritt (Kapitel 39). Done = abgeschlossen oder übersprungen.</summary>
    public sealed class S_Tutorial : Message
    {
        public override Opcode Op => Opcode.S_Tutorial;
        public int Step, Steps;
        public string Title = "", Text = "", Hint = "";
        /// <summary>Worauf die Erklärerin zeigt (0 = nichts) und wer sie ist.</summary>
        public int FocusEntityId, GuideId;
        public int Progress, Goal;
        public bool Done;
        public override void Write(NetDataWriter w)
        {
            w.Put(Step); w.Put(Steps); w.Put(Title); w.Put(Text); w.Put(Hint); w.Put(FocusEntityId); w.Put(GuideId); w.Put(Progress); w.Put(Goal); w.Put(Done);
        }
        public override void Read(NetDataReader r)
        {
            Step = r.GetInt(); Steps = r.GetInt(); Title = r.GetString(); Text = r.GetString(); Hint = r.GetString(); FocusEntityId = r.GetInt(); GuideId = r.GetInt();
            Progress = r.GetInt(); Goal = r.GetInt(); Done = r.GetBool();
        }
    }

    public sealed class C_TutorialSkip : Message
    {
        public override Opcode Op => Opcode.C_TutorialSkip;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class C_TutorialRestart : Message
    {
        public override Opcode Op => Opcode.C_TutorialRestart;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }
}
