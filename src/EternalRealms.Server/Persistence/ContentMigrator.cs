using EternalRealms.Shared.Content;

namespace EternalRealms.Server.Persistence
{
    /// <summary>
    /// Bringt gespeicherte Daten auf den aktuellen Content-Stand (Kapitel 56): wendet die Migrationen aus
    /// content/migrations.json einmal an (umbenennen, entfernen mit Erstattung) und verwahrt Gegenstände, deren Definition
    /// ohne Migration verschwunden ist, statt sie zu löschen. Kommt die Definition zurück (Rollback des Contents), werden
    /// sie beim nächsten Laden zurückgelegt. Nie wird etwas verdoppelt: jede Änderung verschiebt oder ersetzt nur.
    /// </summary>
    public static class ContentMigrator
    {
        public sealed class Report
        {
            public List<string> Changes { get; } = new();
            /// <summary>Erstattung in Kupfer für entfernte Gegenstände (noch nicht gutgeschrieben – der Aufrufer bucht sie).</summary>
            public long Refund { get; set; }
            public bool Changed => Changes.Count > 0 || Refund > 0;
        }

        public static Report Apply(CharacterData d, ContentDatabase db)
        {
            var r = new Report();
            foreach (var m in db.Migrations)
            {
                if (d.ContentMigrations.Contains(m.Id)) continue;
                ApplyItems(d, m, r);
                foreach (var (from, to) in m.RenameQuests)
                {
                    if (d.Quests.Remove(from, out var qp)) { qp.QuestId = to; d.Quests[to] = qp; r.Changes.Add($"Quest {from} → {to}"); }
                    if (d.CompletedQuests.Remove(from)) d.CompletedQuests.Add(to);
                }
                foreach (var q in m.RemoveQuests)
                    if (d.Quests.Remove(q) | d.CompletedQuests.Remove(q)) r.Changes.Add($"Quest {q} entfernt");
                foreach (var (from, to) in m.RenameAbilities)
                    if (d.Abilities.Remove(from, out var rank))
                    {
                        d.Abilities[to] = Math.Max(rank, d.Abilities.GetValueOrDefault(to));
                        r.Changes.Add($"Fähigkeit {from} → {to}");
                    }
                foreach (var a in m.RemoveAbilities)
                    if (d.Abilities.Remove(a)) r.Changes.Add($"Fähigkeit {a} entfernt");
                foreach (var (from, to) in m.RenameZones)
                {
                    if (d.ZoneId == from) { d.ZoneId = to; r.Changes.Add($"Region {from} → {to}"); }
                    if (d.BindZone == from) d.BindZone = to;
                    if (d.CorpseZone == from) d.CorpseZone = to;
                    if (d.ReturnZone == from) d.ReturnZone = to;
                }
                foreach (var (from, to) in m.RenameAchievements)
                    if (d.Achievements.Remove(from, out var ap)) { d.Achievements[to] = ap; r.Changes.Add($"Erfolg {from} → {to}"); }
                d.ContentMigrations.Add(m.Id);
            }
            Quarantine(d, db, r);
            // Ohne Definition gibt es weder laufende Quests noch Fähigkeiten – sie würden das Spiel stören, nicht bereichern.
            foreach (var q in d.Quests.Keys.Where(q => !db.Quests.ContainsKey(q)).ToList()) { d.Quests.Remove(q); r.Changes.Add($"unbekannte Quest {q} abgebrochen"); }
            foreach (var a in d.Abilities.Keys.Where(a => db.GetAbilityRank(a, 1) == null).ToList()) { d.Abilities.Remove(a); r.Changes.Add($"unbekannte Fähigkeit {a} entfernt"); }
            return r;
        }

        private static void ApplyItems(CharacterData d, ContentMigrationDef m, Report r)
        {
            if (m.RenameItems.Count == 0 && m.RemoveItems.Count == 0) return;
            void Slot(Func<ItemStack?> get, Action<ItemStack?> set, string where)
            {
                var s = get();
                if (s == null) return;
                if (m.RenameItems.TryGetValue(s.ItemId, out var to)) { r.Changes.Add($"{where}: {s.ItemId} → {to}"); s.ItemId = to; }
                else if (m.RemoveItems.TryGetValue(s.ItemId, out var price))
                {
                    r.Refund += price * s.Count;
                    r.Changes.Add($"{where}: {s.Count}× {s.ItemId} entfernt, {price * s.Count} Kupfer erstattet");
                    set(null);
                }
            }
            for (int i = 0; i < d.Bag.Count; i++) { int k = i; Slot(() => d.Bag[k], v => d.Bag[k] = v, "Tasche"); }
            for (int i = 0; i < d.Bank.Count; i++) { int k = i; Slot(() => d.Bank[k], v => d.Bank[k] = v, "Bank"); }
            foreach (var slot in d.Equipment.Keys.ToList()) Slot(() => d.Equipment[slot], v => { if (v == null) d.Equipment.Remove(slot); }, "Ausrüstung");
            foreach (var slot in d.BagContainers.Keys.ToList()) Slot(() => d.BagContainers[slot], v => { if (v == null) d.BagContainers.Remove(slot); }, "Taschenplatz");
            foreach (var s in d.Quarantine.ToList())
            {
                if (m.RenameItems.TryGetValue(s.ItemId, out var to)) s.ItemId = to;
                else if (m.RemoveItems.TryGetValue(s.ItemId, out var price)) { r.Refund += price * s.Count; d.Quarantine.Remove(s); r.Changes.Add($"Verwahrt: {s.Count}× {s.ItemId} erstattet"); }
            }
        }

        /// <summary>Unbekannte Gegenstände verwahren; wieder bekannte zurücklegen (sofern ein Taschenplatz frei ist).</summary>
        private static void Quarantine(CharacterData d, ContentDatabase db, Report r)
        {
            bool Known(ItemStack s) => db.Items.ContainsKey(s.ItemId);
            for (int i = 0; i < d.Bag.Count; i++)
                if (d.Bag[i] is ItemStack s && !Known(s)) { d.Quarantine.Add(s); d.Bag[i] = null; r.Changes.Add($"Tasche: {s.ItemId} unbekannt – verwahrt"); }
            for (int i = 0; i < d.Bank.Count; i++)
                if (d.Bank[i] is ItemStack s && !Known(s)) { d.Quarantine.Add(s); d.Bank[i] = null; r.Changes.Add($"Bank: {s.ItemId} unbekannt – verwahrt"); }
            foreach (var (slot, s) in d.Equipment.ToList())
                if (!Known(s)) { d.Quarantine.Add(s); d.Equipment.Remove(slot); r.Changes.Add($"Ausrüstung: {s.ItemId} unbekannt – verwahrt"); }
            foreach (var (slot, s) in d.BagContainers.ToList())
                if (!Known(s)) { d.Quarantine.Add(s); d.BagContainers.Remove(slot); r.Changes.Add($"Taschenplatz: {s.ItemId} unbekannt – verwahrt"); }
            foreach (var s in d.Quarantine.Where(Known).ToList())
            {
                int free = d.Bag.FindIndex(x => x == null);
                if (free < 0) break;
                d.Bag[free] = s;
                d.Quarantine.Remove(s);
                r.Changes.Add($"Verwahrt: {s.Count}× {s.ItemId} zurück in die Tasche");
            }
        }

        /// <summary>Briefanhänge umbenennen; entfernte werden als Gold beigelegt. true = Brief geändert.</summary>
        public static bool Apply(MailData mail, ContentDatabase db)
        {
            bool changed = false;
            foreach (var m in db.Migrations)
                foreach (var s in mail.Items.ToList())
                {
                    if (m.RenameItems.TryGetValue(s.ItemId, out var to)) { s.ItemId = to; changed = true; }
                    else if (m.RemoveItems.TryGetValue(s.ItemId, out var price)) { mail.Items.Remove(s); mail.Money += price * s.Count; changed = true; }
                }
            return changed;
        }

        /// <summary>Auktion umbenennen. null = unverändert, true = geändert, false = Gegenstand entfernt (Auktion auflösen).</summary>
        public static bool? Apply(AuctionData a, ContentDatabase db)
        {
            bool? result = null;
            foreach (var m in db.Migrations)
            {
                if (m.RenameItems.TryGetValue(a.ItemId, out var to)) { a.ItemId = to; result = true; }
                else if (m.RemoveItems.ContainsKey(a.ItemId)) return false;
            }
            return result;
        }

        /// <summary>
        /// Konsistenzprüfung (Kapitel 53): was würde beim nächsten Laden angepasst? Läuft auf Kopien – ändert nichts.
        /// Liefert eine Zusammenfassung und je betroffenem Datensatz eine Zeile (höchstens <paramref name="maxLines"/>).
        /// </summary>
        public static async Task<(int Checked, int Affected, List<string> Lines)> InspectAsync(ICharacterRepository chars, ContentDatabase db, int maxLines = 50)
        {
            var lines = new List<string>();
            int checkedCount = 0, affected = 0;
            foreach (var id in await chars.ListAllIdsAsync())
            {
                var c = await chars.GetAsync(id);
                if (c == null) continue;
                checkedCount++;
                if (c.Level < 1 || c.Level > Shared.Rules.Experience.MaxLevel || c.Money < 0)
                {
                    affected++;
                    if (lines.Count < maxLines) lines.Add($"{c.Name} (#{c.Id}): ungültige Werte (Stufe {c.Level}, Geld {c.Money})");
                }
                if (!db.Zones.ContainsKey(c.ZoneId) && lines.Count < maxLines) lines.Add($"{c.Name} (#{c.Id}): unbekannte Region {c.ZoneId} – wird zum Rückkehrpunkt gebracht");
                var r = Apply(c, db);
                if (!r.Changed) continue;
                affected++;
                if (lines.Count < maxLines) lines.Add($"{c.Name} (#{c.Id}): {string.Join("; ", r.Changes.Take(5))}{(r.Changes.Count > 5 ? " …" : "")}{(r.Refund > 0 ? $", Erstattung {r.Refund} Kupfer" : "")}");
            }
            if (chars is IEconomyRepository eco)
            {
                foreach (var m in await eco.LoadMailAsync())
                {
                    var unknown = m.Items.Where(i => !db.Items.ContainsKey(i.ItemId)).Select(i => i.ItemId).ToList();
                    if (unknown.Count > 0 && lines.Count < maxLines) lines.Add($"Brief #{m.Id}: unbekannte Anhänge {string.Join(", ", unknown)}");
                }
                foreach (var a in await eco.LoadAuctionsAsync())
                    if (!db.Items.ContainsKey(a.ItemId) && lines.Count < maxLines) lines.Add($"Auktion #{a.Id}: unbekannter Gegenstand {a.ItemId}");
            }
            return (checkedCount, affected, lines);
        }

        public static long RefundFor(AuctionData a, ContentDatabase db)
        {
            foreach (var m in db.Migrations)
                if (m.RemoveItems.TryGetValue(a.ItemId, out var price)) return price * a.Count;
            return 0;
        }
    }
}
