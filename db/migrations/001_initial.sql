-- Eternal Realms – Grundschema (PostgreSQL 14+)
-- Konten, Charaktere (Spieler und KI-Mitspieler), Inventar, Fähigkeiten, Talente, Quests, Ruf.
-- Gilden, Freunde, Auktionshaus und Post sind bereits angelegt und werden in Meilenstein 2 genutzt.

CREATE TABLE accounts (
    id              BIGSERIAL PRIMARY KEY,
    username        TEXT NOT NULL,
    password_hash   TEXT NOT NULL,
    is_banned       BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login      TIMESTAMPTZ
);
CREATE UNIQUE INDEX accounts_username_ci ON accounts (lower(username));

CREATE TABLE characters (
    id              BIGSERIAL PRIMARY KEY,
    account_id      BIGINT REFERENCES accounts(id) ON DELETE CASCADE,   -- NULL bei KI-Mitspielern
    name            TEXT NOT NULL,
    race            SMALLINT NOT NULL,
    class           SMALLINT NOT NULL,
    gender          SMALLINT NOT NULL,
    appearance      JSONB NOT NULL,
    level           INT NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 60),
    xp              INT NOT NULL DEFAULT 0,
    rested_xp       INT NOT NULL DEFAULT 0,
    money           BIGINT NOT NULL DEFAULT 0 CHECK (money >= 0),
    zone_id         TEXT NOT NULL,
    pos_x           REAL NOT NULL,
    pos_y           REAL NOT NULL,
    pos_z           REAL NOT NULL,
    facing          REAL NOT NULL DEFAULT 0,
    health          INT NOT NULL DEFAULT 0,
    mana            INT NOT NULL DEFAULT 0,
    death_state     SMALLINT NOT NULL DEFAULT 0,
    is_bot          BOOLEAN NOT NULL DEFAULT FALSE,
    bot_data        JSONB,
    -- kleine Mengen ohne eigene Abfragen: als JSON/Array
    world_state     JSONB NOT NULL DEFAULT '{}'::jsonb,   -- Leiche, Ruhestein, Rückkehrpunkt
    discovered_areas TEXT[] NOT NULL DEFAULT '{}',
    travel_nodes    TEXT[] NOT NULL DEFAULT '{}',
    completed_quests TEXT[] NOT NULL DEFAULT '{}',
    played_seconds  BIGINT NOT NULL DEFAULT 0,
    logged_out_in_inn BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_logout     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX characters_name_ci ON characters (lower(name));
CREATE INDEX characters_account ON characters (account_id) WHERE account_id IS NOT NULL;
CREATE INDEX characters_bots ON characters (is_bot) WHERE is_bot;

CREATE TABLE character_abilities (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    ability_id      TEXT NOT NULL,
    rank            INT NOT NULL,
    PRIMARY KEY (character_id, ability_id)
);

CREATE TABLE character_talents (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    talent_id       TEXT NOT NULL,
    rank            INT NOT NULL,
    PRIMARY KEY (character_id, talent_id)
);

-- container: 0 = Taschen, 1 = angelegt, 2 = Bank
CREATE TABLE character_items (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    container       SMALLINT NOT NULL,
    slot            INT NOT NULL,
    item_id         TEXT NOT NULL,
    count           INT NOT NULL CHECK (count > 0),
    durability      INT NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, container, slot)
);
CREATE INDEX character_items_item ON character_items (item_id);

CREATE TABLE character_quests (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    quest_id        TEXT NOT NULL,
    state           SMALLINT NOT NULL,
    counts          INT[] NOT NULL,
    PRIMARY KEY (character_id, quest_id)
);

CREATE TABLE character_reputation (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    faction_id      TEXT NOT NULL,
    value           INT NOT NULL,
    PRIMARY KEY (character_id, faction_id)
);

-- ---------------------------------------------------------------- Meilenstein 2 (Schema vorbereitet)

CREATE TABLE guilds (
    id              BIGSERIAL PRIMARY KEY,
    name            TEXT NOT NULL,
    faction         SMALLINT NOT NULL,
    leader_id       BIGINT REFERENCES characters(id) ON DELETE SET NULL,
    motd            TEXT NOT NULL DEFAULT '',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX guilds_name_ci ON guilds (lower(name));

CREATE TABLE guild_members (
    guild_id        BIGINT NOT NULL REFERENCES guilds(id) ON DELETE CASCADE,
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    rank            SMALLINT NOT NULL DEFAULT 4,
    note            TEXT NOT NULL DEFAULT '',
    joined_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (guild_id, character_id)
);
CREATE UNIQUE INDEX guild_members_one_guild ON guild_members (character_id);

CREATE TABLE friends (
    character_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    friend_id       BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    note            TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (character_id, friend_id)
);

CREATE TABLE auctions (
    id              BIGSERIAL PRIMARY KEY,
    faction         SMALLINT NOT NULL,                 -- getrennte Auktionshäuser je Fraktion
    seller_id       BIGINT REFERENCES characters(id) ON DELETE SET NULL,
    item_id         TEXT NOT NULL,
    count           INT NOT NULL CHECK (count > 0),
    durability      INT NOT NULL DEFAULT 0,
    start_bid       BIGINT NOT NULL CHECK (start_bid > 0),
    buyout          BIGINT CHECK (buyout IS NULL OR buyout >= start_bid),
    current_bid     BIGINT,
    bidder_id       BIGINT REFERENCES characters(id) ON DELETE SET NULL,
    deposit         BIGINT NOT NULL DEFAULT 0,
    expires_at      TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX auctions_search ON auctions (faction, item_id, expires_at);
CREATE INDEX auctions_expiry ON auctions (expires_at);

CREATE TABLE mail (
    id              BIGSERIAL PRIMARY KEY,
    recipient_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    sender_name     TEXT NOT NULL,
    subject         TEXT NOT NULL,
    body            TEXT NOT NULL DEFAULT '',
    money           BIGINT NOT NULL DEFAULT 0,
    item_id         TEXT,
    item_count      INT,
    read            BOOLEAN NOT NULL DEFAULT FALSE,
    expires_at      TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX mail_recipient ON mail (recipient_id, created_at);
