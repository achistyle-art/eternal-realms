using System.Collections.Generic;
using LiteNetLib.Utils;

namespace EternalRealms.Shared.Net
{
    public sealed class C_LearnProfession : Message
    {
        public override Opcode Op => Opcode.C_LearnProfession;
        public int NpcId;
        public override void Write(NetDataWriter w) => w.Put(NpcId);
        public override void Read(NetDataReader r) => NpcId = r.GetInt();
    }

    public sealed class C_TrainProfessionRank : Message
    {
        public override Opcode Op => Opcode.C_TrainProfessionRank;
        public int NpcId;
        public override void Write(NetDataWriter w) => w.Put(NpcId);
        public override void Read(NetDataReader r) => NpcId = r.GetInt();
    }

    public sealed class C_LearnRecipe : Message
    {
        public override Opcode Op => Opcode.C_LearnRecipe;
        public int NpcId;
        public string RecipeId = "";
        public override void Write(NetDataWriter w) { w.Put(NpcId); w.Put(RecipeId); }
        public override void Read(NetDataReader r) { NpcId = r.GetInt(); RecipeId = r.GetString(); }
    }

    public sealed class C_UnlearnProfession : Message
    {
        public override Opcode Op => Opcode.C_UnlearnProfession;
        public string Profession = "";
        public override void Write(NetDataWriter w) => w.Put(Profession);
        public override void Read(NetDataReader r) => Profession = r.GetString();
    }

    public sealed class C_Craft : Message
    {
        public override Opcode Op => Opcode.C_Craft;
        public string RecipeId = "";
        public int Count = 1;
        public override void Write(NetDataWriter w) { w.Put(RecipeId); w.Put(Count); }
        public override void Read(NetDataReader r) { RecipeId = r.GetString(); Count = r.GetInt(); }
    }

    public sealed class C_Skin : Message
    {
        public override Opcode Op => Opcode.C_Skin;
        public int CorpseId;
        public override void Write(NetDataWriter w) => w.Put(CorpseId);
        public override void Read(NetDataReader r) => CorpseId = r.GetInt();
    }

    public sealed class C_Disenchant : Message
    {
        public override Opcode Op => Opcode.C_Disenchant;
        public int BagIndex;
        public override void Write(NetDataWriter w) => w.Put(BagIndex);
        public override void Read(NetDataReader r) => BagIndex = r.GetInt();
    }

    public sealed class C_ProfessionInfo : Message
    {
        public override Opcode Op => Opcode.C_ProfessionInfo;
        public override void Write(NetDataWriter w) { }
        public override void Read(NetDataReader r) { }
    }

    public sealed class ProfessionData
    {
        public string Id = "";
        public int Skill, MaxSkill;
        public List<string> Recipes = new List<string>();
        public void Write(NetDataWriter w) { w.Put(Id); w.Put(Skill); w.Put(MaxSkill); w.PutStrings(Recipes); }
        public static ProfessionData Read(NetDataReader r) => new ProfessionData { Id = r.GetString(), Skill = r.GetInt(), MaxSkill = r.GetInt(), Recipes = r.GetStrings() };
    }

    public sealed class S_Professions : Message
    {
        public override Opcode Op => Opcode.S_Professions;
        public List<ProfessionData> Professions = new List<ProfessionData>();
        public override void Write(NetDataWriter w) => w.PutList(Professions, (x, p) => p.Write(x));
        public override void Read(NetDataReader r) => Professions = r.GetList(ProfessionData.Read);
    }

    /// <summary>Angebot eines Berufslehrers: Beruf erlernen, nächster Rang, lernbare Rezepte (mit Preis).</summary>
    public sealed class S_ProfessionTrainer : Message
    {
        public override Opcode Op => Opcode.S_ProfessionTrainer;
        public int NpcId;
        public string Profession = "";
        public bool Known;
        public long LearnCost;
        public string NextRank = "";
        public long NextRankCost;
        public List<string> Recipes = new List<string>();
        public List<long> RecipeCosts = new List<long>();
        public override void Write(NetDataWriter w)
        {
            w.Put(NpcId); w.Put(Profession); w.Put(Known); w.Put(LearnCost); w.Put(NextRank); w.Put(NextRankCost); w.PutStrings(Recipes); w.PutList(RecipeCosts, (x, c) => x.Put(c));
        }
        public override void Read(NetDataReader r)
        {
            NpcId = r.GetInt(); Profession = r.GetString(); Known = r.GetBool(); LearnCost = r.GetLong(); NextRank = r.GetString(); NextRankCost = r.GetLong();
            Recipes = r.GetStrings(); RecipeCosts = r.GetList(x => x.GetLong());
        }
    }
}
