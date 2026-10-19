-- Gilden, Freunde, Ignorieren und Meldungen (Kapitel 22, 31, 32).
ALTER TABLE guilds ADD COLUMN IF NOT EXISTS info TEXT NOT NULL DEFAULT '';
ALTER TABLE guilds ADD COLUMN IF NOT EXISTS rank_names TEXT[] NOT NULL DEFAULT ARRAY['Gildenmeister','Offizier','Veteran','Mitglied','Rekrut'];

CREATE TABLE IF NOT EXISTS character_ignores (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    ignored_id      BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    PRIMARY KEY (character_id, ignored_id)
);

CREATE TABLE IF NOT EXISTS player_reports (
    id              BIGSERIAL PRIMARY KEY,
    at              TIMESTAMPTZ NOT NULL,
    reporter_id     BIGINT,
    reporter_name   TEXT NOT NULL,
    target_id       BIGINT,
    target_name     TEXT NOT NULL,
    reason          TEXT NOT NULL,
    text            TEXT NOT NULL DEFAULT '',
    evidence        JSONB NOT NULL DEFAULT '[]',
    handled         BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS player_reports_open ON player_reports (handled, at);
