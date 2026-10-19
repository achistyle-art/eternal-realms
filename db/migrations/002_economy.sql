-- Meilenstein 2: Post, Auktionshaus und Geldprotokoll.
-- Die Tabellen mail und auctions aus 001 wurden noch nicht benutzt und werden durch das endgültige Schema ersetzt.
DROP TABLE IF EXISTS mail;
DROP TABLE IF EXISTS auctions;

CREATE TABLE mail (
    id              BIGINT PRIMARY KEY,                -- vom Server vergeben (eindeutig, aufsteigend)
    recipient_id    BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    sender_id       BIGINT REFERENCES characters(id) ON DELETE SET NULL,
    sender_name     TEXT NOT NULL,
    subject         TEXT NOT NULL,
    body            TEXT NOT NULL DEFAULT '',
    money           BIGINT NOT NULL DEFAULT 0 CHECK (money >= 0),
    cod             BIGINT NOT NULL DEFAULT 0 CHECK (cod >= 0),
    items           JSONB NOT NULL DEFAULT '[]',       -- bis zu 12 Stapel {itemId, count, durability}
    deliver_at      TIMESTAMPTZ NOT NULL,
    expires_at      TIMESTAMPTZ NOT NULL,
    is_read         BOOLEAN NOT NULL DEFAULT FALSE,
    returned        BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX mail_recipient ON mail (recipient_id);
CREATE INDEX mail_expires ON mail (expires_at);

CREATE TABLE auctions (
    id              BIGINT PRIMARY KEY,
    house           SMALLINT NOT NULL,                 -- getrennte Häuser je Fraktion
    seller_id       BIGINT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    seller_name     TEXT NOT NULL,
    seller_is_bot   BOOLEAN NOT NULL DEFAULT FALSE,
    item_id         TEXT NOT NULL,
    count           INT NOT NULL CHECK (count > 0),
    durability      INT NOT NULL DEFAULT 0,
    start_bid       BIGINT NOT NULL CHECK (start_bid > 0),
    buyout          BIGINT NOT NULL DEFAULT 0 CHECK (buyout = 0 OR buyout >= start_bid),
    current_bid     BIGINT NOT NULL DEFAULT 0 CHECK (current_bid >= 0),
    bidder_id       BIGINT REFERENCES characters(id) ON DELETE SET NULL,
    bidder_name     TEXT NOT NULL DEFAULT '',
    deposit         BIGINT NOT NULL DEFAULT 0,
    duration_hours  SMALLINT NOT NULL CHECK (duration_hours IN (12, 24, 48)),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at      TIMESTAMPTZ NOT NULL
);
CREATE INDEX auctions_house_item ON auctions (house, item_id);
CREATE INDEX auctions_seller ON auctions (seller_id);
CREATE INDEX auctions_expires ON auctions (expires_at);

-- Jede Geldbewegung (Kapitel 15): nur anfügen, nie ändern.
CREATE TABLE economy_log (
    id              BIGSERIAL PRIMARY KEY,
    at              TIMESTAMPTZ NOT NULL,
    character_id    BIGINT,                            -- bewusst ohne Fremdschlüssel: Protokoll bleibt nach Löschung erhalten
    character_name  TEXT NOT NULL DEFAULT '',
    delta           BIGINT NOT NULL,
    balance         BIGINT NOT NULL,
    reason          TEXT NOT NULL,
    reference       TEXT NOT NULL DEFAULT ''
);
CREATE INDEX economy_log_character ON economy_log (character_id, at);
CREATE INDEX economy_log_reason ON economy_log (reason, at);
