using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Client.Ui
{
    public enum ActionKind : byte { Empty = 0, Ability = 1, Item = 2 }

    public sealed class ActionSlot
    {
        public ActionKind Kind { get; set; }
        /// <summary>Fähigkeits-Basis-ID (der höchste bekannte Rang wird benutzt) oder Gegenstands-ID.</summary>
        public string Id { get; set; } = "";
        public bool IsEmpty => Kind == ActionKind.Empty || Id.Length == 0;
    }

    public sealed class ActionBar
    {
        public const int Slots = 12;
        public bool Visible { get; set; } = true;
        public List<ActionSlot> Buttons { get; set; } = Enumerable.Range(0, Slots).Select(_ => new ActionSlot()).ToList();
    }

    /// <summary>Was ein Aktionsknopf gerade anzeigen soll (Kapitel 28.2).</summary>
    public sealed class SlotState
    {
        public string Label = "";
        public string Icon = "";
        public float CooldownLeft;
        public bool NotEnoughPower, OutOfRange, Unknown, Active;
        public int ItemCount = -1;
        public bool Usable => !NotEnoughPower && !OutOfRange && !Unknown && CooldownLeft <= 0 && ItemCount != 0;
    }

    /// <summary>Mehrere frei belegbare Aktionsleisten mit Ziehen-und-Ablegen.</summary>
    public sealed class ActionBarSet
    {
        public const int BarCount = 4;
        public List<ActionBar> Bars { get; set; } = Enumerable.Range(0, BarCount).Select(i => new ActionBar { Visible = i < 2 }).ToList();

        public ActionSlot Get(int bar, int slot) => Bars[bar].Buttons[slot];

        public void Set(int bar, int slot, ActionKind kind, string id) { var s = Get(bar, slot); s.Kind = kind; s.Id = id ?? ""; }

        public void Clear(int bar, int slot) => Set(bar, slot, ActionKind.Empty, "");

        /// <summary>Knopf auf einen anderen Platz ziehen: Inhalte werden getauscht.</summary>
        public void Move(int fromBar, int fromSlot, int toBar, int toSlot)
        {
            var a = Get(fromBar, fromSlot); var b = Get(toBar, toSlot);
            var (k, id) = (a.Kind, a.Id);
            a.Kind = b.Kind; a.Id = b.Id;
            b.Kind = k; b.Id = id;
        }

        public void Normalize()
        {
            while (Bars.Count < BarCount) Bars.Add(new ActionBar { Visible = false });
            foreach (var b in Bars) while (b.Buttons.Count < ActionBar.Slots) b.Buttons.Add(new ActionSlot());
        }

        /// <summary>Neue Fähigkeiten automatisch auf freie Plätze der ersten Leiste legen (wie beim Lernen im Vorbild).</summary>
        public void AutoPlace(IEnumerable<string> abilityBaseIds)
        {
            foreach (var id in abilityBaseIds)
            {
                if (Bars.Any(b => b.Buttons.Any(s => s.Kind == ActionKind.Ability && s.Id == id))) continue;
                var free = Bars.Take(2).SelectMany(b => b.Buttons).FirstOrDefault(s => s.IsEmpty);
                if (free == null) return;
                free.Kind = ActionKind.Ability;
                free.Id = id;
            }
        }

        /// <summary>
        /// Anzeigezustand eines Knopfs aus dem Clientzustand: Abklingzeit, Ressource, Reichweite, aktive Haltung,
        /// Anzahl bei Gegenständen. Verbindlich prüft der Server – das hier ist nur die Rückmeldung für den Spieler.
        /// </summary>
        public static SlotState Evaluate(ActionSlot slot, ContentDatabase content, GameClient client)
        {
            var st = new SlotState();
            if (slot.IsEmpty) return st;
            var me = client.Me;
            if (slot.Kind == ActionKind.Item)
            {
                st.Label = content.Items.TryGetValue(slot.Id, out var item) ? item.Name : slot.Id;
                st.Icon = item?.Icon ?? "";
                st.ItemCount = client.State.Inventory?.Bag.Where(b => b.ItemId == slot.Id).Sum(b => b.Count) ?? 0;
                if (item?.UseAbility != null && client.Cooldowns.TryGetValue(item.UseAbility, out var until)) st.CooldownLeft = (float)(until - client.Now);
                return st;
            }
            if (!client.SpellBook.TryGetValue(slot.Id, out var rank)) { st.Unknown = true; st.Label = slot.Id; return st; }
            var ab = content.GetAbilityRank(slot.Id, rank);
            if (ab == null) { st.Unknown = true; return st; }
            st.Label = ab.Name;
            st.Icon = ab.Icon;
            if (client.Cooldowns.TryGetValue(ab.Id, out var cd)) st.CooldownLeft = (float)(cd - client.Now);
            if (me != null)
            {
                st.NotEnoughPower = ab.Cost > 0 && me.Power < ab.Cost;
                var target = client.State.Get(me.TargetId);
                if (target != null && target != me && ab.Target is TargetType.Enemy or TargetType.Friendly or TargetType.AreaAroundTarget)
                    st.OutOfRange = Vec3.DistanceXZ(me.Position, target.Position) > ab.MaxRange + 1.5f;
                st.Active = ab.Effects.Any(e => e.Type == EffectType.ApplyAura && e.Aura != null && me.Auras.Any(a => a.AuraId == e.Aura));
            }
            return st;
        }
    }
}
