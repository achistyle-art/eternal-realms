using System.Text.Json;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using Npgsql;
using NpgsqlTypes;

namespace EternalRealms.Server.Persistence
{
    /// <summary>
    /// PostgreSQL-Speicher. Die Kerndaten eines Charakters liegen in Spalten, Inventar, Fähigkeiten, Talente,
    /// Quests und Ruf in eigenen Tabellen (abfragbar, z. B. für Auktionshaus und Auswertungen).
    /// Ein Speichervorgang ist eine Transaktion: Entweder alles oder nichts wird geschrieben.
    /// </summary>
    public sealed class PostgresStore : IAccountRepository, ICharacterRepository, IEconomyRepository, ISocialRepository, IDisposable
    {
        private readonly NpgsqlDataSource _ds;
        private static readonly JsonSerializerOptions Json = ContentDatabase.CreateOptions();

        public PostgresStore(string connectionString)
        {
            _ds = NpgsqlDataSource.Create(connectionString);
        }

        public void Dispose() => _ds.Dispose();

        // ------------------------------------------------------------ Migrationen

        public async Task MigrateAsync(string migrationsDir, Action<string>? log = null)
        {
            await using var conn = await _ds.OpenConnectionAsync();
            await using (var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())", conn))
                await cmd.ExecuteNonQueryAsync();
            var applied = new HashSet<string>();
            await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) applied.Add(r.GetString(0));
            foreach (var file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            {
                var version = Path.GetFileNameWithoutExtension(file);
                if (applied.Contains(version)) continue;
                await using var tx = await conn.BeginTransactionAsync();
                await using (var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(file), conn, tx)) await cmd.ExecuteNonQueryAsync();
                await using (var cmd = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@v)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("v", version);
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                log?.Invoke("Datenbankmigration angewendet: " + version);
            }
        }

        // ------------------------------------------------------------ Konten

        public async Task<Account?> FindByNameAsync(string username)
        {
            await using var cmd = _ds.CreateCommand("SELECT id, username, password_hash, is_banned, created_at FROM accounts WHERE lower(username) = lower(@u)");
            cmd.Parameters.AddWithValue("u", username);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new Account { Id = r.GetInt64(0), Username = r.GetString(1), PasswordHash = r.GetString(2), IsBanned = r.GetBoolean(3), CreatedAt = r.GetDateTime(4) };
        }

        public async Task<Account> CreateAsync(string username, string passwordHash)
        {
            await using var cmd = _ds.CreateCommand("INSERT INTO accounts (username, password_hash) VALUES (@u, @p) RETURNING id, created_at");
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.AddWithValue("p", passwordHash);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            return new Account { Id = r.GetInt64(0), Username = username, PasswordHash = passwordHash, CreatedAt = r.GetDateTime(1) };
        }

        public async Task TouchLoginAsync(long accountId)
        {
            await using var cmd = _ds.CreateCommand("UPDATE accounts SET last_login = now() WHERE id = @id");
            cmd.Parameters.AddWithValue("id", accountId);
            await cmd.ExecuteNonQueryAsync();
        }

        // ------------------------------------------------------------ Charaktere

        private sealed class WorldStateJson
        {
            public string CorpseZone { get; set; } = "";
            public Vec3 CorpsePosition { get; set; }
            public string BindZone { get; set; } = "";
            public Vec3 BindPosition { get; set; }
            public string? ReturnZone { get; set; }
            public Vec3 ReturnPosition { get; set; }
            public int TalentResets { get; set; }
            public DateTime LastTalentReset { get; set; }
            public int RidingSkill { get; set; }
            public HashSet<string> Mounts { get; set; } = new();
            public bool ShareLocation { get; set; } = true;
            public Dictionary<string, InstanceBind> InstanceBinds { get; set; } = new();
            public long CurrentInstanceId { get; set; }
            public List<DateTime> RecentInstances { get; set; } = new();
            public Dictionary<string, ProfessionProgress> Professions { get; set; } = new();
        }

        private const string CharColumns = "id, account_id, name, race, class, gender, appearance, level, xp, rested_xp, money, zone_id, pos_x, pos_y, pos_z, facing, " +
                                           "health, mana, death_state, is_bot, bot_data, world_state, discovered_areas, travel_nodes, completed_quests, played_seconds, " +
                                           "logged_out_in_inn, created_at, last_logout";

        public async Task<List<CharacterData>> ListForAccountAsync(long accountId)
        {
            var ids = new List<long>();
            await using (var cmd = _ds.CreateCommand("SELECT id FROM characters WHERE account_id = @a AND NOT is_bot ORDER BY id"))
            {
                cmd.Parameters.AddWithValue("a", accountId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(r.GetInt64(0));
            }
            var list = new List<CharacterData>();
            foreach (var id in ids) { var c = await GetAsync(id); if (c != null) list.Add(c); }
            return list;
        }

        public async Task<List<CharacterData>> ListBotsAsync()
        {
            var ids = new List<long>();
            await using (var cmd = _ds.CreateCommand("SELECT id FROM characters WHERE is_bot ORDER BY id"))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) ids.Add(r.GetInt64(0));
            var list = new List<CharacterData>();
            foreach (var id in ids) { var c = await GetAsync(id); if (c != null) list.Add(c); }
            return list;
        }

        public async Task<bool> NameExistsAsync(string name)
        {
            await using var cmd = _ds.CreateCommand("SELECT 1 FROM characters WHERE lower(name) = lower(@n)");
            cmd.Parameters.AddWithValue("n", name);
            return await cmd.ExecuteScalarAsync() != null;
        }

        public async Task<CharacterData?> GetAsync(long id)
        {
            await using var conn = await _ds.OpenConnectionAsync();
            CharacterData c;
            await using (var cmd = new NpgsqlCommand($"SELECT {CharColumns} FROM characters WHERE id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return null;
                var ws = JsonSerializer.Deserialize<WorldStateJson>(r.GetString(21), Json) ?? new WorldStateJson();
                c = new CharacterData
                {
                    Id = r.GetInt64(0), AccountId = r.IsDBNull(1) ? null : r.GetInt64(1), Name = r.GetString(2), Race = (Race)r.GetInt16(3),
                    Class = (CharClass)r.GetInt16(4), Gender = (Gender)r.GetInt16(5),
                    Appearance = JsonSerializer.Deserialize<AppearanceDto>(r.GetString(6), Json) ?? new AppearanceDto(),
                    Level = r.GetInt32(7), Xp = r.GetInt32(8), RestedXp = r.GetInt32(9), Money = r.GetInt64(10), ZoneId = r.GetString(11),
                    Position = new Vec3(r.GetFloat(12), r.GetFloat(13), r.GetFloat(14)), Facing = r.GetFloat(15), Health = r.GetInt32(16), Mana = r.GetInt32(17),
                    DeathState = (DeathState)r.GetInt16(18), IsBot = r.GetBoolean(19),
                    Bot = r.IsDBNull(20) ? null : JsonSerializer.Deserialize<BotData>(r.GetString(20), Json),
                    CorpseZone = ws.CorpseZone, CorpsePosition = ws.CorpsePosition, BindZone = ws.BindZone, BindPosition = ws.BindPosition,
                    ReturnZone = ws.ReturnZone, ReturnPosition = ws.ReturnPosition, TalentResets = ws.TalentResets, LastTalentReset = ws.LastTalentReset, RidingSkill = ws.RidingSkill, Mounts = ws.Mounts, ShareLocation = ws.ShareLocation, InstanceBinds = ws.InstanceBinds, CurrentInstanceId = ws.CurrentInstanceId, RecentInstances = ws.RecentInstances, Professions = ws.Professions,
                    DiscoveredAreas = new HashSet<string>(r.GetFieldValue<string[]>(22)), KnownTravelNodes = new HashSet<string>(r.GetFieldValue<string[]>(23)),
                    CompletedQuests = new HashSet<string>(r.GetFieldValue<string[]>(24)), PlayedSeconds = r.GetInt64(25), LoggedOutInInn = r.GetBoolean(26),
                    CreatedAt = r.GetDateTime(27), LastLogout = r.GetDateTime(28),
                };
            }
            await using (var cmd = new NpgsqlCommand("SELECT ability_id, rank FROM character_abilities WHERE character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) c.Abilities[r.GetString(0)] = r.GetInt32(1);
            }
            await using (var cmd = new NpgsqlCommand("SELECT f.friend_id, c.name, f.note FROM friends f JOIN characters c ON c.id = f.friend_id WHERE f.character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) c.Friends.Add(new SocialEntry { CharacterId = r.GetInt64(0), Name = r.GetString(1), Note = r.GetString(2) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT i.ignored_id, c.name FROM character_ignores i JOIN characters c ON c.id = i.ignored_id WHERE i.character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) c.Ignored.Add(new SocialEntry { CharacterId = r.GetInt64(0), Name = r.GetString(1) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT talent_id, rank FROM character_talents WHERE character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) c.Talents[r.GetString(0)] = r.GetInt32(1);
            }
            await using (var cmd = new NpgsqlCommand("SELECT container, slot, item_id, count, durability, enchant FROM character_items WHERE character_id = @id ORDER BY container, slot", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var stack = new ItemStack { ItemId = r.GetString(2), Count = r.GetInt32(3), Durability = r.GetInt32(4), Enchant = r.GetString(5) };
                    int slot = r.GetInt32(1);
                    short container = r.GetInt16(0);
                    if (container == 1) c.Equipment[(EquipSlot)slot] = stack;
                    else if (container == 2)
                    {
                        while (c.Bank.Count <= slot) c.Bank.Add(null);
                        c.Bank[slot] = stack;
                    }
                    else if (container == 3) c.BagContainers[slot] = stack;
                    else
                    {
                        while (c.Bag.Count <= slot) c.Bag.Add(null);
                        c.Bag[slot] = stack;
                    }
                }
            }
            await using (var cmd = new NpgsqlCommand("SELECT quest_id, state, counts FROM character_quests WHERE character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    c.Quests[r.GetString(0)] = new QuestProgress { QuestId = r.GetString(0), State = (QuestState)r.GetInt16(1), Counts = r.GetFieldValue<int[]>(2) };
            }
            await using (var cmd = new NpgsqlCommand("SELECT faction_id, value FROM character_reputation WHERE character_id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) c.Reputation[r.GetString(0)] = r.GetInt32(1);
            }
            return c;
        }

        public async Task<long> CreateAsync(CharacterData data)
        {
            await using var conn = await _ds.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO characters (account_id, name, race, class, gender, appearance, zone_id, pos_x, pos_y, pos_z) " +
                "VALUES (@a, @n, @r, @c, @g, @ap::jsonb, @z, 0, 0, 0) RETURNING id", conn, tx))
            {
                cmd.Parameters.AddWithValue("a", (object?)data.AccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("n", data.Name);
                cmd.Parameters.AddWithValue("r", (short)data.Race);
                cmd.Parameters.AddWithValue("c", (short)data.Class);
                cmd.Parameters.AddWithValue("g", (short)data.Gender);
                cmd.Parameters.AddWithValue("ap", JsonSerializer.Serialize(data.Appearance, Json));
                cmd.Parameters.AddWithValue("z", data.ZoneId);
                data.Id = (long)(await cmd.ExecuteScalarAsync())!;
            }
            await WriteAsync(conn, tx, data);
            await tx.CommitAsync();
            return data.Id;
        }

        public async Task SaveAsync(CharacterData data)
        {
            if (data.Id <= 0) return;
            await using var conn = await _ds.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await WriteAsync(conn, tx, data);
            await tx.CommitAsync();
        }

        private static async Task WriteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CharacterData d)
        {
            var ws = new WorldStateJson
            {
                CorpseZone = d.CorpseZone, CorpsePosition = d.CorpsePosition, BindZone = d.BindZone, BindPosition = d.BindPosition,
                ReturnZone = d.ReturnZone, ReturnPosition = d.ReturnPosition, TalentResets = d.TalentResets, LastTalentReset = d.LastTalentReset, RidingSkill = d.RidingSkill, Mounts = d.Mounts, ShareLocation = d.ShareLocation, InstanceBinds = d.InstanceBinds, CurrentInstanceId = d.CurrentInstanceId, RecentInstances = d.RecentInstances, Professions = d.Professions,
            };
            await using (var cmd = new NpgsqlCommand(
                "UPDATE characters SET level=@lv, xp=@xp, rested_xp=@rx, money=@m, zone_id=@z, pos_x=@x, pos_y=@y, pos_z=@zz, facing=@f, health=@h, mana=@mn, " +
                "death_state=@ds, is_bot=@b, bot_data=@bd::jsonb, appearance=@ap::jsonb, world_state=@ws::jsonb, discovered_areas=@da, travel_nodes=@tn, " +
                "completed_quests=@cq, played_seconds=@ps, logged_out_in_inn=@inn, last_logout=@ll WHERE id=@id", conn, tx))
            {
                var p = cmd.Parameters;
                p.AddWithValue("lv", d.Level); p.AddWithValue("xp", d.Xp); p.AddWithValue("rx", d.RestedXp); p.AddWithValue("m", d.Money);
                p.AddWithValue("z", d.ZoneId); p.AddWithValue("x", d.Position.X); p.AddWithValue("y", d.Position.Y); p.AddWithValue("zz", d.Position.Z);
                p.AddWithValue("f", d.Facing); p.AddWithValue("h", d.Health); p.AddWithValue("mn", d.Mana); p.AddWithValue("ds", (short)d.DeathState);
                p.AddWithValue("b", d.IsBot); p.AddWithValue("bd", d.Bot == null ? DBNull.Value : JsonSerializer.Serialize(d.Bot, Json));
                p.AddWithValue("ap", JsonSerializer.Serialize(d.Appearance, Json)); p.AddWithValue("ws", JsonSerializer.Serialize(ws, Json));
                p.AddWithValue("da", d.DiscoveredAreas.ToArray()); p.AddWithValue("tn", d.KnownTravelNodes.ToArray()); p.AddWithValue("cq", d.CompletedQuests.ToArray());
                p.AddWithValue("ps", d.PlayedSeconds); p.AddWithValue("inn", d.LoggedOutInInn);
                p.AddWithValue("ll", DateTime.SpecifyKind(d.LastLogout, DateTimeKind.Utc)); p.AddWithValue("id", d.Id);
                await cmd.ExecuteNonQueryAsync();
            }
            foreach (var table in new[] { "character_abilities", "character_talents", "character_items", "character_quests", "character_reputation", "friends", "character_ignores" })
                await using (var del = new NpgsqlCommand($"DELETE FROM {table} WHERE character_id = @id", conn, tx))
                {
                    del.Parameters.AddWithValue("id", d.Id);
                    await del.ExecuteNonQueryAsync();
                }
            // Mehrere Zeilen je Tabelle in einem Befehl (unnest über Arrays) statt einzelner INSERTs.
            if (d.Friends.Count > 0)
                await using (var cmd = new NpgsqlCommand("INSERT INTO friends (character_id, friend_id, note) SELECT @id, f, n FROM unnest(@f, @n) AS t(f, n) " +
                                                         "WHERE EXISTS (SELECT 1 FROM characters WHERE id = f) ON CONFLICT DO NOTHING", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", d.Id);
                    cmd.Parameters.AddWithValue("f", d.Friends.Select(f => f.CharacterId).ToArray());
                    cmd.Parameters.AddWithValue("n", d.Friends.Select(f => f.Note).ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            if (d.Ignored.Count > 0)
                await using (var cmd = new NpgsqlCommand("INSERT INTO character_ignores (character_id, ignored_id) SELECT @id, i FROM unnest(@i) AS t(i) " +
                                                         "WHERE EXISTS (SELECT 1 FROM characters WHERE id = i) ON CONFLICT DO NOTHING", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", d.Id);
                    cmd.Parameters.AddWithValue("i", d.Ignored.Select(f => f.CharacterId).ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            await InsertKeyValues(conn, tx, "character_abilities", "ability_id", "rank", d.Id, d.Abilities);
            await InsertKeyValues(conn, tx, "character_talents", "talent_id", "rank", d.Id, d.Talents);
            await InsertKeyValues(conn, tx, "character_reputation", "faction_id", "value", d.Id, d.Reputation);
            var containers = new List<short>(); var slots = new List<int>(); var items = new List<string>(); var counts = new List<int>(); var durs = new List<int>(); var enchs = new List<string>();
            for (int i = 0; i < d.Bag.Count; i++)
                if (d.Bag[i] is { } s) { containers.Add(0); slots.Add(i); items.Add(s.ItemId); counts.Add(s.Count); durs.Add(s.Durability); enchs.Add(s.Enchant); }
            foreach (var kv in d.Equipment) { containers.Add(1); slots.Add((int)kv.Key); items.Add(kv.Value.ItemId); counts.Add(kv.Value.Count); durs.Add(kv.Value.Durability); enchs.Add(kv.Value.Enchant); }
            for (int i = 0; i < d.Bank.Count; i++)
                if (d.Bank[i] is { } b) { containers.Add(2); slots.Add(i); items.Add(b.ItemId); counts.Add(b.Count); durs.Add(b.Durability); enchs.Add(b.Enchant); }
            foreach (var kv in d.BagContainers) { containers.Add(3); slots.Add(kv.Key); items.Add(kv.Value.ItemId); counts.Add(kv.Value.Count); durs.Add(kv.Value.Durability); enchs.Add(kv.Value.Enchant); }
            if (items.Count > 0)
                await using (var cmd = new NpgsqlCommand("INSERT INTO character_items (character_id, container, slot, item_id, count, durability, enchant) " +
                                                         "SELECT @id, unnest(@c), unnest(@s), unnest(@i), unnest(@n), unnest(@d), unnest(@e)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", d.Id);
                    cmd.Parameters.AddWithValue("c", containers.ToArray()); cmd.Parameters.AddWithValue("s", slots.ToArray());
                    cmd.Parameters.AddWithValue("i", items.ToArray()); cmd.Parameters.AddWithValue("n", counts.ToArray()); cmd.Parameters.AddWithValue("d", durs.ToArray());
                    cmd.Parameters.AddWithValue("e", enchs.ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            foreach (var q in d.Quests.Values)
                await using (var cmd = new NpgsqlCommand("INSERT INTO character_quests (character_id, quest_id, state, counts) VALUES (@id, @q, @s, @c)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", d.Id); cmd.Parameters.AddWithValue("q", q.QuestId);
                    cmd.Parameters.AddWithValue("s", (short)q.State); cmd.Parameters.AddWithValue("c", q.Counts);
                    await cmd.ExecuteNonQueryAsync();
                }
        }

        private static async Task InsertKeyValues(NpgsqlConnection conn, NpgsqlTransaction tx, string table, string keyCol, string valCol, long id, Dictionary<string, int> values)
        {
            if (values.Count == 0) return;
            await using var cmd = new NpgsqlCommand($"INSERT INTO {table} (character_id, {keyCol}, {valCol}) SELECT @id, unnest(@k), unnest(@v)", conn, tx);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("k", values.Keys.ToArray());
            cmd.Parameters.AddWithValue("v", values.Values.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }

        // ------------------------------------------------------------ Wirtschaft

        /// <summary>Charaktere, Briefe, Auktionen und Geldprotokoll in genau einer Transaktion.</summary>
        public async Task CommitAsync(EconomyCommit c)
        {
            await using var conn = await _ds.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            foreach (var d in c.Characters) if (d.Id > 0) await WriteAsync(conn, tx, d);
            foreach (var m in c.UpsertMail)
                await using (var cmd = new NpgsqlCommand(
                    "INSERT INTO mail (id, recipient_id, sender_id, sender_name, subject, body, money, cod, items, deliver_at, expires_at, is_read, returned, created_at) " +
                    "VALUES (@id, @r, @s, @sn, @sub, @b, @m, @cod, @it::jsonb, @da, @ea, @rd, @ret, @ca) ON CONFLICT (id) DO UPDATE SET " +
                    "recipient_id=EXCLUDED.recipient_id, sender_id=EXCLUDED.sender_id, sender_name=EXCLUDED.sender_name, subject=EXCLUDED.subject, money=EXCLUDED.money, " +
                    "cod=EXCLUDED.cod, items=EXCLUDED.items, deliver_at=EXCLUDED.deliver_at, expires_at=EXCLUDED.expires_at, is_read=EXCLUDED.is_read, returned=EXCLUDED.returned", conn, tx))
                {
                    var p = cmd.Parameters;
                    p.AddWithValue("id", m.Id); p.AddWithValue("r", m.RecipientId); p.AddWithValue("s", (object?)m.SenderId ?? DBNull.Value);
                    p.AddWithValue("sn", m.SenderName); p.AddWithValue("sub", m.Subject); p.AddWithValue("b", m.Body); p.AddWithValue("m", m.Money); p.AddWithValue("cod", m.Cod);
                    p.AddWithValue("it", JsonSerializer.Serialize(m.Items, Json)); p.AddWithValue("da", m.DeliverAt); p.AddWithValue("ea", m.ExpiresAt);
                    p.AddWithValue("rd", m.Read); p.AddWithValue("ret", m.Returned); p.AddWithValue("ca", m.CreatedAt);
                    await cmd.ExecuteNonQueryAsync();
                }
            if (c.DeleteMail.Count > 0)
                await using (var cmd = new NpgsqlCommand("DELETE FROM mail WHERE id = ANY(@ids)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("ids", c.DeleteMail.ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            foreach (var a in c.UpsertAuctions)
                await using (var cmd = new NpgsqlCommand(
                    "INSERT INTO auctions (id, house, seller_id, seller_name, seller_is_bot, item_id, count, durability, start_bid, buyout, current_bid, bidder_id, bidder_name, " +
                    "deposit, duration_hours, created_at, expires_at, enchant) VALUES (@id, @h, @s, @sn, @sb, @i, @n, @d, @sbid, @bo, @cb, @bid, @bn, @dep, @dh, @ca, @ea, @en) " +
                    "ON CONFLICT (id) DO UPDATE SET current_bid=EXCLUDED.current_bid, bidder_id=EXCLUDED.bidder_id, bidder_name=EXCLUDED.bidder_name, expires_at=EXCLUDED.expires_at", conn, tx))
                {
                    var p = cmd.Parameters;
                    p.AddWithValue("id", a.Id); p.AddWithValue("h", (short)a.House); p.AddWithValue("s", a.SellerId); p.AddWithValue("sn", a.SellerName); p.AddWithValue("sb", a.SellerIsBot);
                    p.AddWithValue("i", a.ItemId); p.AddWithValue("n", a.Count); p.AddWithValue("d", a.Durability); p.AddWithValue("sbid", a.StartBid); p.AddWithValue("bo", a.Buyout);
                    p.AddWithValue("cb", a.CurrentBid); p.AddWithValue("bid", (object?)a.BidderId ?? DBNull.Value); p.AddWithValue("bn", a.BidderName); p.AddWithValue("dep", a.Deposit);
                    p.AddWithValue("dh", (short)a.DurationHours); p.AddWithValue("ca", a.CreatedAt); p.AddWithValue("ea", a.ExpiresAt); p.AddWithValue("en", a.Enchant);
                    await cmd.ExecuteNonQueryAsync();
                }
            if (c.DeleteAuctions.Count > 0)
                await using (var cmd = new NpgsqlCommand("DELETE FROM auctions WHERE id = ANY(@ids)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("ids", c.DeleteAuctions.ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            if (c.Ledger.Count > 0)
                await using (var cmd = new NpgsqlCommand(
                    "INSERT INTO economy_log (at, character_id, character_name, delta, balance, reason, reference) " +
                    "SELECT unnest(@at), NULLIF(unnest(@cid), 0), unnest(@cn), unnest(@d), unnest(@b), unnest(@r), unnest(@ref)", conn, tx))
                {
                    var p = cmd.Parameters;
                    p.AddWithValue("at", c.Ledger.Select(l => l.At).ToArray()); p.AddWithValue("cid", c.Ledger.Select(l => l.CharacterId).ToArray());
                    p.AddWithValue("cn", c.Ledger.Select(l => l.CharacterName).ToArray()); p.AddWithValue("d", c.Ledger.Select(l => l.Delta).ToArray());
                    p.AddWithValue("b", c.Ledger.Select(l => l.Balance).ToArray()); p.AddWithValue("r", c.Ledger.Select(l => l.Reason).ToArray());
                    p.AddWithValue("ref", c.Ledger.Select(l => l.Reference).ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            await tx.CommitAsync();
        }

        public async Task<List<MailData>> LoadMailAsync()
        {
            var list = new List<MailData>();
            await using var cmd = _ds.CreateCommand("SELECT id, recipient_id, sender_id, sender_name, subject, body, money, cod, items, deliver_at, expires_at, is_read, returned, created_at FROM mail");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new MailData
                {
                    Id = r.GetInt64(0), RecipientId = r.GetInt64(1), SenderId = r.IsDBNull(2) ? null : r.GetInt64(2), SenderName = r.GetString(3), Subject = r.GetString(4),
                    Body = r.GetString(5), Money = r.GetInt64(6), Cod = r.GetInt64(7), Items = JsonSerializer.Deserialize<List<ItemStack>>(r.GetString(8), Json) ?? new(),
                    DeliverAt = r.GetDateTime(9), ExpiresAt = r.GetDateTime(10), Read = r.GetBoolean(11), Returned = r.GetBoolean(12), CreatedAt = r.GetDateTime(13),
                });
            return list;
        }

        public async Task<List<AuctionData>> LoadAuctionsAsync()
        {
            var list = new List<AuctionData>();
            await using var cmd = _ds.CreateCommand("SELECT id, house, seller_id, seller_name, seller_is_bot, item_id, count, durability, start_bid, buyout, current_bid, bidder_id, " +
                                                    "bidder_name, deposit, duration_hours, created_at, expires_at, enchant FROM auctions");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new AuctionData
                {
                    Id = r.GetInt64(0), House = (byte)r.GetInt16(1), SellerId = r.GetInt64(2), SellerName = r.GetString(3), SellerIsBot = r.GetBoolean(4), ItemId = r.GetString(5),
                    Count = r.GetInt32(6), Durability = r.GetInt32(7), StartBid = r.GetInt64(8), Buyout = r.GetInt64(9), CurrentBid = r.GetInt64(10),
                    BidderId = r.IsDBNull(11) ? null : r.GetInt64(11), BidderName = r.GetString(12), Deposit = r.GetInt64(13), DurationHours = r.GetInt16(14),
                    CreatedAt = r.GetDateTime(15), ExpiresAt = r.GetDateTime(16), Enchant = r.GetString(17),
                });
            return list;
        }

        public async Task<CharacterRef?> FindCharacterAsync(string name)
        {
            await using var cmd = _ds.CreateCommand("SELECT id, account_id, name, race FROM characters WHERE lower(name) = lower(@n)");
            cmd.Parameters.AddWithValue("n", name);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new CharacterRef { Id = r.GetInt64(0), AccountId = r.IsDBNull(1) ? null : r.GetInt64(1), Name = r.GetString(2), Race = (Race)r.GetInt16(3) };
        }

        public async Task<List<LedgerEntry>> LedgerForAsync(long characterId, int limit)
        {
            var list = new List<LedgerEntry>();
            await using var cmd = _ds.CreateCommand("SELECT at, character_id, character_name, delta, balance, reason, reference FROM economy_log WHERE character_id = @c ORDER BY id DESC LIMIT @l");
            cmd.Parameters.AddWithValue("c", characterId);
            cmd.Parameters.AddWithValue("l", limit);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new LedgerEntry { At = r.GetDateTime(0), CharacterId = r.GetInt64(1), CharacterName = r.GetString(2), Delta = r.GetInt64(3), Balance = r.GetInt64(4), Reason = r.GetString(5), Reference = r.GetString(6) });
            return list;
        }

        // ------------------------------------------------------------ Soziales

        public async Task<List<GuildData>> LoadGuildsAsync()
        {
            var guilds = new Dictionary<long, GuildData>();
            await using var conn = await _ds.OpenConnectionAsync();
            await using (var cmd = new NpgsqlCommand("SELECT id, name, faction, COALESCE(leader_id, 0), motd, info, rank_names, created_at FROM guilds", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    guilds[r.GetInt64(0)] = new GuildData
                    {
                        Id = r.GetInt64(0), Name = r.GetString(1), Faction = (Faction)r.GetInt16(2), LeaderId = r.GetInt64(3), Motd = r.GetString(4), Info = r.GetString(5),
                        RankNames = ((string[])r.GetValue(6)).ToList(), CreatedAt = r.GetDateTime(7),
                    };
            await using (var cmd = new NpgsqlCommand("SELECT m.guild_id, m.character_id, c.name, m.rank, m.note, c.level, c.class, c.is_bot, m.joined_at " +
                                                     "FROM guild_members m JOIN characters c ON c.id = m.character_id", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    if (guilds.TryGetValue(r.GetInt64(0), out var g))
                        g.Members.Add(new GuildMemberData
                        {
                            CharacterId = r.GetInt64(1), Name = r.GetString(2), Rank = r.GetInt16(3), Note = r.GetString(4), Level = r.GetInt32(5),
                            Class = (CharClass)r.GetInt16(6), IsBot = r.GetBoolean(7), JoinedAt = r.GetDateTime(8),
                        });
            return guilds.Values.ToList();
        }

        public async Task SaveGuildAsync(GuildData g)
        {
            await using var conn = await _ds.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var cmd = new NpgsqlCommand("INSERT INTO guilds (id, name, faction, leader_id, motd, info, rank_names, created_at) VALUES (@id, @n, @f, @l, @m, @i, @r, @c) " +
                                                     "ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name, leader_id=EXCLUDED.leader_id, motd=EXCLUDED.motd, info=EXCLUDED.info, rank_names=EXCLUDED.rank_names", conn, tx))
            {
                var p = cmd.Parameters;
                p.AddWithValue("id", g.Id); p.AddWithValue("n", g.Name); p.AddWithValue("f", (short)g.Faction); p.AddWithValue("l", g.LeaderId);
                p.AddWithValue("m", g.Motd); p.AddWithValue("i", g.Info); p.AddWithValue("r", g.RankNames.ToArray()); p.AddWithValue("c", g.CreatedAt);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand("DELETE FROM guild_members WHERE guild_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", g.Id);
                await cmd.ExecuteNonQueryAsync();
            }
            if (g.Members.Count > 0)
                await using (var cmd = new NpgsqlCommand("INSERT INTO guild_members (guild_id, character_id, rank, note, joined_at) SELECT @g, c, r, n, j FROM unnest(@c, @r, @n, @j) AS t(c, r, n, j) " +
                                                         "WHERE EXISTS (SELECT 1 FROM characters WHERE id = c)", conn, tx))
                {
                    var p = cmd.Parameters;
                    p.AddWithValue("g", g.Id);
                    p.AddWithValue("c", g.Members.Select(m => m.CharacterId).ToArray());
                    p.AddWithValue("r", g.Members.Select(m => (short)m.Rank).ToArray());
                    p.AddWithValue("n", g.Members.Select(m => m.Note).ToArray());
                    p.AddWithValue("j", g.Members.Select(m => m.JoinedAt).ToArray());
                    await cmd.ExecuteNonQueryAsync();
                }
            await tx.CommitAsync();
        }

        public async Task DeleteGuildAsync(long guildId)
        {
            await using var cmd = _ds.CreateCommand("DELETE FROM guilds WHERE id = @id");
            cmd.Parameters.AddWithValue("id", guildId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SaveReportAsync(PlayerReport rep)
        {
            await using var cmd = _ds.CreateCommand("INSERT INTO player_reports (at, reporter_id, reporter_name, target_id, target_name, reason, text, evidence) " +
                                                    "VALUES (@at, @rid, @rn, @tid, @tn, @r, @t, @e::jsonb)");
            var p = cmd.Parameters;
            p.AddWithValue("at", rep.At); p.AddWithValue("rid", rep.ReporterId); p.AddWithValue("rn", rep.ReporterName); p.AddWithValue("tid", (object?)rep.TargetId ?? DBNull.Value);
            p.AddWithValue("tn", rep.TargetName); p.AddWithValue("r", rep.Reason); p.AddWithValue("t", rep.Text); p.AddWithValue("e", JsonSerializer.Serialize(rep.Evidence, Json));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(long id)
        {
            await using var cmd = _ds.CreateCommand("DELETE FROM characters WHERE id = @id");
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
