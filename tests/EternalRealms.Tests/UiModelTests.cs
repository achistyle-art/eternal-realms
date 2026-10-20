using EternalRealms.Server.Net;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 28–30 und 59: Tastenbelegung, Aktionsleisten, Einstellungen, Karten – ohne Unity testbar.</summary>
    public class UiModelTests
    {
        [Fact]
        public void DefaultKeybindingsFollowChapter29AndHaveNoDuplicates()
        {
            var k = Keybindings.CreateDefault();
            Assert.Equal("move_forward", k.ActionFor("W"));
            Assert.Equal("strafe_left", k.ActionFor("q"));
            Assert.Equal("all_bags", k.ActionFor("Shift+B"));
            Assert.Equal("action_1_1", k.ActionFor("Alpha1"));
            Assert.Equal("action_2_1", k.ActionFor("shift+Alpha1"));
            var all = k.Bindings.Values.SelectMany(v => v).Where(v => v.Length > 0).ToList();
            Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(Keybindings.Defaults.Count, k.Bindings.Count);
        }

        [Fact]
        public void RebindingStealsTheKeyFromTheOtherAction()
        {
            var k = Keybindings.CreateDefault();
            var previous = k.Bind("world_map", "W");
            Assert.Equal("move_forward", previous);
            Assert.Equal("world_map", k.ActionFor("W"));
            Assert.Equal("", k.KeyFor("move_forward"));
            Assert.Null(k.Bind("move_forward", "Up", 1));
            Assert.Equal("move_forward", k.ActionFor("Up"));
            k.Unbind("move_forward", 1);
            Assert.Null(k.ActionFor("Up"));
            Assert.Throws<ArgumentException>(() => k.Bind("gibt_es_nicht", "K"));
            k.ResetToDefaults();
            Assert.Equal("move_forward", k.ActionFor("W"));
        }

        [Theory]
        [InlineData("strg+umschalt+F", "Ctrl+Shift+F")]
        [InlineData("Shift+Alt+X", "Alt+Shift+X")]
        [InlineData("  ", "")]
        [InlineData("F1", "F1")]
        public void KeyNamesAreNormalized(string input, string expected) => Assert.Equal(expected, Keybindings.Normalize(input));

        [Fact]
        public void SettingsSurviveJsonRoundTripAndAreClamped()
        {
            var s = new GameSettings { UiScale = 1.4f, FontSize = 18, ShowDamageNumbers = false, Colorblind = ColorblindMode.Deuteranopia };
            s.Keys.Bind("world_map", "K");
            s.ActionBars.Set(1, 3, ActionKind.Item, "item_minor_healing_potion");
            s.Window("bags", 10, 20, 300, 400).Visible = true;
            var back = GameSettings.FromJson(s.ToJson());
            Assert.Equal(1.4f, back.UiScale);
            Assert.Equal(18, back.FontSize);
            Assert.False(back.ShowDamageNumbers);
            Assert.Equal(ColorblindMode.Deuteranopia, back.Colorblind);
            Assert.Equal("world_map", back.Keys.ActionFor("K"));
            Assert.Equal("item_minor_healing_potion", back.ActionBars.Get(1, 3).Id);
            Assert.True(back.Windows["bags"].Visible);
            Assert.Equal(300, back.Windows["bags"].Width);

            var wild = GameSettings.FromJson("{\"UiScale\":9,\"FontSize\":2,\"VolumeMusic\":-1,\"CameraMaxDistance\":500,\"Windows\":{\"x\":{\"Scale\":0}}}");
            Assert.Equal(2f, wild.UiScale);
            Assert.Equal(9, wild.FontSize);
            Assert.Equal(0f, wild.VolumeMusic);
            Assert.Equal(40f, wild.CameraMaxDistance);
            Assert.Equal(0.5f, wild.Windows["x"].Scale);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("kein json")]
        [InlineData("{\"Keys\":null,\"ActionBars\":null,\"ChatTabs\":[]}")]
        [InlineData("{\"Keys\":{\"Bindings\":{\"move_forward\":[\"W\",null],\"veraltet\":[\"X\",\"\"]}}}")]
        public void BrokenSettingsFallBackToDefaults(string? json)
        {
            var s = GameSettings.FromJson(json);
            Assert.Equal(Keybindings.Defaults.Count, s.Keys.Bindings.Count);
            Assert.Equal("move_forward", s.Keys.ActionFor("W"));
            Assert.Equal(ActionBarSet.BarCount, s.ActionBars.Bars.Count);
            Assert.All(s.ActionBars.Bars, b => Assert.Equal(ActionBar.Slots, b.Buttons.Count));
            Assert.NotEmpty(s.ChatTabs);
            Assert.True(s.ToJson().Length < GameSettings.MaxJsonLength);
        }

        [Fact]
        public void OlderSettingsGainNewActionsWithoutLosingOwnBindings()
        {
            var s = GameSettings.FromJson("{\"Keys\":{\"Bindings\":{\"world_map\":[\"W\",\"\"]}}}");
            Assert.Equal("world_map", s.Keys.ActionFor("W"));
            // W ist schon belegt: Vorwärts bekommt keine doppelte Taste, die übrigen Aktionen ihre Standards
            Assert.Equal("", s.Keys.KeyFor("move_forward"));
            Assert.Equal("all_bags", s.Keys.ActionFor("Shift+B"));
        }

        [Fact]
        public void ActionBarsSwapOnMoveAndAutoPlaceNewAbilities()
        {
            var bars = new ActionBarSet();
            bars.AutoPlace(new[] { "a", "b", "c" });
            Assert.Equal("a", bars.Get(0, 0).Id);
            Assert.Equal("c", bars.Get(0, 2).Id);
            bars.AutoPlace(new[] { "b", "d" }); // schon vorhandene nicht doppelt
            Assert.Equal("d", bars.Get(0, 3).Id);
            Assert.True(bars.Get(0, 4).IsEmpty);
            bars.Move(0, 0, 1, 5);
            Assert.True(bars.Get(0, 0).IsEmpty);
            Assert.Equal("a", bars.Get(1, 5).Id);
            bars.Move(0, 1, 0, 2);
            Assert.Equal("c", bars.Get(0, 1).Id);
            Assert.Equal("b", bars.Get(0, 2).Id);
            bars.Clear(0, 1);
            Assert.True(bars.Get(0, 1).IsEmpty);
            // volle Leisten: kein Absturz, nichts überschrieben
            var full = new ActionBarSet();
            full.AutoPlace(Enumerable.Range(0, 30).Select(i => "x" + i));
            Assert.Equal("x23", full.Get(1, 11).Id);
            Assert.True(full.Get(2, 0).IsEmpty);
        }

        [Fact]
        public void SlotStateReflectsSpellBookCooldownsAndItemCounts()
        {
            var db = TestContent.Db;
            var baseId = db.Abilities.Values.First(a => db.GetAbilityRank(a.Id, 1) != null).Id;
            var rank = db.GetAbilityRank(baseId, 1)!;
            var client = new GameClient();
            var slot = new ActionSlot { Kind = ActionKind.Ability, Id = baseId };
            Assert.True(ActionBarSet.Evaluate(slot, db, client).Unknown);

            client.Receive(new S_SpellBook { AbilityIds = { baseId }, Ranks = { 1 } });
            var st = ActionBarSet.Evaluate(slot, db, client);
            Assert.False(st.Unknown);
            Assert.Equal(rank.Name, st.Label);
            Assert.True(st.Usable);

            client.Receive(new S_Cooldown { AbilityId = rank.Id, Seconds = 30 });
            st = ActionBarSet.Evaluate(slot, db, client);
            Assert.InRange(st.CooldownLeft, 25f, 30f);
            Assert.False(st.Usable);

            var item = new ActionSlot { Kind = ActionKind.Item, Id = "item_minor_healing_potion" };
            Assert.Equal(0, ActionBarSet.Evaluate(item, db, client).ItemCount);
            Assert.False(ActionBarSet.Evaluate(item, db, client).Usable);
            Assert.True(ActionBarSet.Evaluate(new ActionSlot(), db, client).Usable);
        }

        [Fact]
        public void ClientAppliesSettingsAndExplorationMessages()
        {
            var client = new GameClient();
            Assert.False(client.SettingsLoaded);
            client.Receive(new S_Settings { Json = "{\"UiScale\":1.5}" });
            Assert.True(client.SettingsLoaded);
            Assert.Equal(1.5f, client.Settings.UiScale);
            client.Receive(new S_Exploration { Full = true, Areas = { "area_mt_lindenruh" } });
            client.Receive(new S_Exploration { Areas = { "area_mt_grauklippe" } });
            Assert.Equal(2, client.DiscoveredAreas.Count);
            client.Receive(new S_Exploration { Full = true, Areas = { "area_mt_silberquell" } });
            Assert.Single(client.DiscoveredAreas);
        }

        [Fact]
        public void EveryOpenWorldZoneIsPlacedOnAContinentWithoutOverlap()
        {
            var db = TestContent.Db;
            var map = new MapModel(db);
            Assert.Contains("Aurelion", map.Continents);
            Assert.Contains("Kharvath", map.Continents);
            foreach (var zone in db.Zones.Values.Where(z => !z.IsInstance && z.Continent != "Unterwelt" && z.Continent.Length > 0))
            {
                Assert.True(zone.MapRect.Count == 4, zone.Id + " ohne mapRect");
                Assert.InRange(zone.MapRect[0], 0, 1000);
                Assert.InRange(zone.MapRect[0] + zone.MapRect[2], 0, 1000);
                Assert.InRange(zone.MapRect[1] + zone.MapRect[3], 0, 1000);
            }
            foreach (var continent in map.Continents)
            {
                var zones = map.Zones(continent, new HashSet<string>(), "");
                for (int i = 0; i < zones.Count; i++)
                    for (int j = i + 1; j < zones.Count; j++)
                    {
                        var (a, b) = (zones[i], zones[j]);
                        bool overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                        Assert.False(overlap, $"{a.Zone.Id} überlappt {b.Zone.Id}");
                    }
            }
        }

        [Fact]
        public void WorldMapRevealsZonesThroughDiscoveredAreas()
        {
            var db = TestContent.Db;
            var map = new MapModel(db);
            var zone = db.Zones["zone_morgentauwald"];
            var hidden = map.Zones(zone.Continent, new HashSet<string>(), "").Single(z => z.Zone.Id == zone.Id);
            Assert.False(hidden.Discovered);
            Assert.True(map.Zones(zone.Continent, new HashSet<string>(), zone.Id).Single(z => z.Zone.Id == zone.Id).Discovered);
            Assert.True(map.Zones(zone.Continent, new HashSet<string> { zone.Areas[0].Id }, "").Single(z => z.Zone.Id == zone.Id).Discovered);

            // Ecken der Region landen auf den Ecken ihres Kartenrechtecks (Norden oben)
            var (x0, y0) = MapModel.ZoneToContinent(zone, 0, zone.Size);
            var (x1, y1) = MapModel.ZoneToContinent(zone, zone.Size, 0);
            Assert.Equal(zone.MapRect[0], x0, 3);
            Assert.Equal(zone.MapRect[1], y0, 3);
            Assert.Equal(zone.MapRect[0] + zone.MapRect[2], x1, 3);
            Assert.Equal(zone.MapRect[1] + zone.MapRect[3], y1, 3);

            var markers = map.ZoneMarkers(zone, new HashSet<string>(), new HashSet<string>(), Array.Empty<string>());
            Assert.Contains(markers, m => m.Kind == MapMarkerKind.Graveyard);
            Assert.Contains(markers, m => m.Kind == MapMarkerKind.FlightMaster && m.Label.EndsWith("(unbekannt)"));
            Assert.Contains(markers, m => m.Kind == MapMarkerKind.DungeonEntrance);
        }

        [Fact]
        public void MinimapProjectionPutsNorthUpAndRotatesWithTheCamera()
        {
            var c = new Vec3(100, 0, 100);
            var (x, y) = MapModel.ToMinimap(100, 150, c, 50);
            Assert.Equal(0f, x, 3);
            Assert.Equal(-1f, y, 3); // Norden (+Z) ist oben (negatives y auf dem Bildschirm)
            var (ex, _) = MapModel.ToMinimap(150, 100, c, 50);
            Assert.Equal(1f, ex, 3);
            var (rx, ry) = MapModel.ToMinimap(100, 150, c, 50, (float)Math.PI / 2);
            Assert.Equal(1f, Math.Abs(rx) + Math.Abs(ry), 3);
        }

        [Fact]
        public void ServerPersistsSettingsAndReportsExploration()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior);
            var conn = t.Conn(p);
            Assert.Single(conn.Of<S_Settings>());
            var full = conn.Of<S_Exploration>().Single();
            Assert.True(full.Full);

            var area = t.World.Zone("zone_morgentauwald")!.Def.Areas.First(a => !p.Data.DiscoveredAreas.Contains(a.Id));
            conn.Clear();
            t.World.Progression.DiscoverArea(p, area);
            t.World.Progression.DiscoverArea(p, area);
            var delta = conn.Of<S_Exploration>().Single();
            Assert.False(delta.Full);
            Assert.Equal(new[] { area.Id }, delta.Areas);

            p.Data.ClientSettings = new GameSettings { FontSize = 20 }.ToJson();
            t.World.Save(p);
            t.World.Persistence?.Flush(TimeSpan.FromSeconds(5));
            var stored = t.Store.GetAsync(p.Data.Id).Result!;
            Assert.Equal(20, GameSettings.FromJson(stored.ClientSettings).FontSize);
        }

        [Fact]
        public void TooltipComparesAgainstTheWeakerMatchingSlot()
        {
            var db = TestContent.Db;
            var ring = db.Items.Values.First(i => i.InventoryType == InventoryType.Finger && i.Modifiers.Count > 0);
            var inv = new S_Inventory();
            for (int i = 0; i <= (int)EquipSlot.Ranged; i++) inv.Equipment.Add(new ItemStackData());
            // Beide Ringplätze leer: Vergleich zeigt den vollen Wert
            var diff = ItemTooltip.Compare(ring, inv, db);
            Assert.NotEmpty(diff);
            Assert.All(diff, d => Assert.True(d.delta > 0));
            // Ein Platz belegt mit genau diesem Ring, der andere leer: verglichen wird gegen den leeren
            inv.Equipment[(int)EquipSlot.Finger1] = new ItemStackData { ItemId = ring.Id, Count = 1 };
            Assert.Equal(diff, ItemTooltip.Compare(ring, inv, db));
            inv.Equipment[(int)EquipSlot.Finger2] = new ItemStackData { ItemId = ring.Id, Count = 1 };
            Assert.Empty(ItemTooltip.Compare(ring, inv, db));
            var potion = db.Items["item_minor_healing_potion"];
            Assert.Empty(ItemTooltip.Compare(potion, inv, db));

            var weapon = db.Items.Values.First(i => i.IsWeapon && i.MaxDurability > 0 && i.RequiredLevel > 1);
            var lines = ItemTooltip.Lines(weapon, db, new ItemStackData { ItemId = weapon.Id, Durability = weapon.MaxDurability / 2 }, playerLevel: 1);
            Assert.Contains(lines, l => l.Contains("DPS"));
            Assert.Contains(lines, l => l.StartsWith("Haltbarkeit"));
            Assert.Contains(lines, l => l.StartsWith("(!) Benötigt Stufe"));
            Assert.InRange(ItemTooltip.DurabilityPercent(weapon, weapon.MaxDurability / 2), 49, 51);
            Assert.Equal(100, ItemTooltip.DurabilityPercent(potion, 0));
        }

        [Fact]
        public void ChatTabsFilterChannelsAndCombatLinesReadNaturally()
        {
            var party = new ChatTab { Channels = { (int)ChatChannel.Party } };
            Assert.True(ChatFormat.Accepts(party, ChatChannel.Party));
            Assert.False(ChatFormat.Accepts(party, ChatChannel.Say));
            Assert.True(ChatFormat.Accepts(party, ChatChannel.System));
            Assert.True(ChatFormat.Accepts(new ChatTab(), ChatChannel.Whisper));

            string Name(int id) => id == 1 ? "Ihr" : "Wolf";
            var hit = new S_CombatEvent { SourceId = 2, TargetId = 1, Result = CombatResult.Hit, Amount = 12, School = School.Physical };
            Assert.Equal("Wolf trifft Ihr für 12 Körperlich.", ChatFormat.CombatLine(hit, Name, a => a));
            var heal = new S_CombatEvent { SourceId = 1, TargetId = 1, Result = CombatResult.CritHeal, Amount = 80, AbilityId = "x" };
            Assert.Equal("Ihr heilt Ihr (Heilung) kritisch um 80.", ChatFormat.CombatLine(heal, Name, a => "Heilung"));
            Assert.Contains("weicht", ChatFormat.CombatLine(new S_CombatEvent { Result = CombatResult.Dodge }, Name, a => a));
        }

        [Fact]
        public void ReputationStandingsAndProgress()
        {
            Assert.Equal("Neutral", Reputation.Standing(0));
            Assert.Equal("Freundlich", Reputation.Standing(3000));
            Assert.Equal("Ehrfürchtig", Reputation.Standing(42999));
            Assert.Equal("Hasserfüllt", Reputation.Standing(-42000));
            Assert.Equal((500, 3000), Reputation.WithinStanding(500));
            Assert.Equal((0, 6000), Reputation.WithinStanding(3000));
        }

        [Fact]
        public void PlayerStateCarriesCharacterWindowValues()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10);
            p.Data.Reputation["rep_test"] = 4500;
            var st = p.BuildPlayerState();
            Assert.Equal(7, st.Resist.Count);
            Assert.True(st.Parry > 0); // Krieger können parieren
            Assert.True(st.MoveSpeed > 0);
            Assert.Equal(4500, st.ReputationValues[st.ReputationIds.IndexOf("rep_test")]);
            var back = (S_PlayerState)Packets.Deserialize(Packets.Serialize(st));
            Assert.Equal(st.Parry, back.Parry);
            Assert.Equal(st.ReputationIds, back.ReputationIds);
        }

        [Fact]
        public void ExplorationIncludesKnownFlightNodes()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior);
            var full = t.Conn(p).Of<S_Exploration>().Single();
            Assert.Equal(p.Data.KnownTravelNodes.OrderBy(n => n), full.FlightNodes);
            var client = new GameClient();
            client.Receive(full);
            client.Receive(new S_Exploration { FlightNodes = { "node_neu" } });
            Assert.Contains("node_neu", client.KnownFlightNodes);
            Assert.Equal(full.FlightNodes.Count + 1, client.KnownFlightNodes.Count);
        }
    }
}
