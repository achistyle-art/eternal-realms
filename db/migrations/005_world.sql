-- Block D: Wiederkehrzeiten von Weltbossen und anderen dauerhaften Spawns (übersteht Neustarts).
CREATE TABLE IF NOT EXISTS spawn_timers (
    key         TEXT PRIMARY KEY,          -- Zone:Spawnindex:Kreatur
    respawn_at  TIMESTAMPTZ NOT NULL
);
