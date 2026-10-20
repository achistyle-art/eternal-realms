using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Interaktives Tutorial (Kapitel 39) mit einer KI-Erklärerin: eine gekennzeichnete Begleiterin, die neuen Spielern
    /// folgt, jeden Schritt erklärt, auf das nächste Ziel zeigt (Questgeber, Gegner, Lehrer …), bei Stillstand nachhilft
    /// und auf Ereignisse wie den ersten Tod reagiert. Schritte schließen sich durch echte Handlungen ab
    /// (Fortschrittsereignisse) – Lesen allein reicht nicht. Jederzeit überspringbar und neu startbar.
    /// </summary>
    public sealed class TutorialSystem
    {
        public const string GuideTemplate = "npc_ki_erklaerer";
        public const float HintAfter = 45f, FollowDistance = 3.5f;

        private sealed class Session
        {
            public Creature? Guide;
            public float StepTime, FocusTimer;
            public bool HintGiven;
            public int FocusId;
            public double LastLevelRemark = -999;
        }

        private readonly GameWorld _w;
        private readonly Dictionary<Player, Session> _sessions = new();
        private List<TutorialStepDef> Steps => _w.Content.Tutorial;

        public TutorialSystem(GameWorld world) { _w = world; }

        public bool IsActive(Player p) => _sessions.ContainsKey(p);
        public Creature? GuideOf(Player p) => _sessions.TryGetValue(p, out var s) ? s.Guide : null;

        /// <summary>Beim Betreten der Welt: neue Charaktere beginnen, angefangene setzen fort.</summary>
        public void OnEnterWorld(Player p)
        {
            if (p.IsBot || Steps.Count == 0 || !_w.Config.Tutorial) return;
            var st = p.Data.Tutorial;
            if (st == null && p.Level <= 2 && p.Data.CompletedQuests.Count == 0) { Start(p); return; }
            if (st != null && !st.Done) { Resume(p); return; }
            p.Send(BuildState(p));
        }

        public void Start(Player p)
        {
            p.Data.Tutorial = new TutorialState();
            Resume(p);
        }

        private void Resume(Player p)
        {
            if (!_sessions.TryGetValue(p, out var s)) _sessions[p] = s = new Session();
            EnsureGuide(p, s);
            BeginStep(p, s);
        }

        public void Skip(Player p)
        {
            p.Data.Tutorial ??= new TutorialState();
            p.Data.Tutorial.Done = true;
            p.Data.Tutorial.Skipped = true;
            End(p, "Alles klar – Ihr kommt allein zurecht. Ruft mich jederzeit über die Optionen zurück!");
        }

        public void Restart(Player p)
        {
            if (!p.IsBot) Start(p);
        }

        public void OnLogout(Player p)
        {
            if (!_sessions.Remove(p, out var s)) return;
            if (s.Guide != null && !s.Guide.Removed) s.Guide.Zone?.Remove(s.Guide);
        }

        private TutorialStepDef? Current(Player p) =>
            p.Data.Tutorial is { Done: false } st && st.Step < Steps.Count ? Steps[st.Step] : null;

        /// <summary>Fortschrittsereignis (aus <see cref="GameWorld.ReportProgress"/>).</summary>
        public void Report(Player p, ProgressEvent ev, string target, int amount)
        {
            if (!_sessions.TryGetValue(p, out var s)) return;
            // Die Erklärerin reagiert auch abseits des aktuellen Schritts
            if (ev == ProgressEvent.Die) Say(p, s, "Keine Sorge! Lasst Euren Geist frei und lauft zu Eurem Körper zurück – oder sprecht mit dem Geisterheiler.");
            // Mehrere Aufstiege kurz hintereinander: nur eine Bemerkung
            if (ev == ProgressEvent.ReachLevel && amount > 1 && _w.Time - s.LastLevelRemark > 10)
            {
                s.LastLevelRemark = _w.Time;
                Say(p, s, $"Stufe {amount}! Beim Klassenlehrer gibt es jetzt neue Fähigkeiten.");
            }
            var step = Current(p);
            if (step?.Trigger != ev || (step.Target.Length > 0 && step.Target != target)) return;
            var st = p.Data.Tutorial!;
            st.Counter = ProgressEvents.IsValue(ev) ? Math.Max(st.Counter, amount) : st.Counter + amount;
            if (st.Counter >= step.Count) Advance(p, s);
            else p.Send(BuildState(p));
        }

        private void Advance(Player p, Session s)
        {
            var st = p.Data.Tutorial!;
            st.Step++;
            st.Counter = 0;
            if (st.Step >= Steps.Count)
            {
                st.Done = true;
                End(p, "Das war's – Ihr seid bereit für Eure eigenen Abenteuer. Viel Glück, und vergesst nicht: Ihr müsst nie allein spielen!");
                _w.ReportProgress(p, ProgressEvent.TutorialDone);
                return;
            }
            BeginStep(p, s);
        }

        private void BeginStep(Player p, Session s)
        {
            var step = Current(p);
            if (step == null) return;
            s.StepTime = 0;
            s.HintGiven = false;
            UpdateFocus(p, s, step);
            Say(p, s, step.Text);
            if (step.Emote.Length > 0 && s.Guide != null) _w.Emotes.PerformBy(s.Guide, step.Emote, s.FocusId != 0 ? p.Zone?.GetUnit(s.FocusId) : p);
            p.Send(BuildState(p));
        }

        private void End(Player p, string farewell)
        {
            if (_sessions.Remove(p, out var s))
            {
                Say(p, s, farewell);
                if (s.Guide != null && s.Guide.Zone != null)
                {
                    _w.Emotes.PerformBy(s.Guide, "wave", p);
                    s.Guide.DespawnAt = _w.Time + 6;
                }
            }
            p.Send(BuildState(p));
        }

        public void Update(float dt)
        {
            if (_sessions.Count == 0) return;
            foreach (var (p, s) in _sessions.ToList())
            {
                if (p.Removed) { OnLogout(p); continue; }
                EnsureGuide(p, s);
                var step = Current(p);
                if (step == null) continue;
                s.StepTime += dt;
                if (step.Trigger == null && step.AutoSeconds > 0 && s.StepTime >= step.AutoSeconds) { Advance(p, s); continue; }
                if (!s.HintGiven && step.Hint.Length > 0 && s.StepTime >= HintAfter && !p.InCombat)
                {
                    s.HintGiven = true;
                    UpdateFocus(p, s, step);
                    Say(p, s, step.Hint);
                    if (s.FocusId != 0 && s.Guide != null) _w.Emotes.PerformBy(s.Guide, "point", p.Zone?.GetUnit(s.FocusId));
                    p.Send(BuildState(p));
                }
                s.FocusTimer -= dt;
                if (s.FocusTimer <= 0 && step.Focus.Length > 0)
                {
                    int before = s.FocusId;
                    UpdateFocus(p, s, step);
                    if (before != s.FocusId) p.Send(BuildState(p));
                }
            }
        }

        // ------------------------------------------------------------ Erklärerin

        private void EnsureGuide(Player p, Session s)
        {
            if (p.Zone == null || !_w.Content.Creatures.ContainsKey(GuideTemplate)) return;
            if (s.Guide != null && !s.Guide.Removed && s.Guide.Zone == p.Zone) return;
            if (s.Guide != null && !s.Guide.Removed) s.Guide.Zone?.Remove(s.Guide);
            var g = p.Zone.SpawnCreature(GuideTemplate, p.Position + new Vec3(FollowDistance, 0, 0));
            g.GuideFor = p;
            s.Guide = g;
        }

        /// <summary>Die Erklärerin spricht nur mit ihrem Schützling (Flüstern) – mit dessen eigener Tastenbelegung.</summary>
        private void Say(Player p, Session s, string text)
        {
            if (text.Length == 0) return;
            var keys = GameSettings.FromJson(p.Data.ClientSettings).Keys;
            p.Send(new S_Chat
            {
                Channel = ChatChannel.Whisper, SenderId = s.Guide?.Id ?? 0, SenderIsBot = true,
                SenderName = (s.Guide?.Name ?? "Lysa") + " (KI-Erklärerin)", Text = TutorialText.Render(text, keys),
            });
        }

        private void UpdateFocus(Player p, Session s, TutorialStepDef step)
        {
            s.FocusTimer = 3;
            s.FocusId = 0;
            if (p.Zone == null || step.Focus.Length == 0) return;
            var near = p.Zone.UnitsInRange(p.Position, 300).OfType<Creature>().ToList();
            Creature? best = step.Focus switch
            {
                "QuestGiver" => near.Where(c => c.Template.Roles.HasFlag(NpcRole.QuestGiver) && _w.Quests.MarkerFor(p, c) is QuestMarker.Available or QuestMarker.ReadyToTurnIn)
                                    .OrderBy(c => c.DistanceTo(p)).FirstOrDefault(),
                "Hostile" => near.Where(c => c.IsAlive && c.Faction == Faction.Hostile && c.TappedBy == null && !c.Template.Elite && c.Level <= p.Level + 1)
                                 .OrderBy(c => c.DistanceTo(p)).FirstOrDefault(),
                "Trainer" => near.Where(c => c.Template.Roles.HasFlag(NpcRole.ClassTrainer) && c.Template.TrainerClass == p.Class).OrderBy(c => c.DistanceTo(p)).FirstOrDefault(),
                "Innkeeper" => near.Where(c => c.Template.Roles.HasFlag(NpcRole.Innkeeper)).OrderBy(c => c.DistanceTo(p)).FirstOrDefault(),
                "Corpse" => near.Where(c => c.IsDead && c.Lootable && c.TappedBy == p).OrderBy(c => c.DistanceTo(p)).FirstOrDefault(),
                _ => null,
            };
            s.FocusId = best?.Id ?? 0;
        }

        public S_Tutorial BuildState(Player p)
        {
            var st = p.Data.Tutorial;
            var step = Current(p);
            _sessions.TryGetValue(p, out var s);
            var keys = GameSettings.FromJson(p.Data.ClientSettings).Keys;
            return new S_Tutorial
            {
                Step = st?.Step ?? 0, Steps = Steps.Count, Done = step == null,
                Title = step?.Title ?? "", Text = step == null ? "" : TutorialText.Render(step.Text, keys),
                Hint = step == null || s == null || !s.HintGiven ? "" : TutorialText.Render(step.Hint, keys),
                FocusEntityId = s?.FocusId ?? 0, GuideId = s?.Guide?.Id ?? 0,
                Progress = st?.Counter ?? 0, Goal = step?.Count ?? 0,
            };
        }
    }
}
