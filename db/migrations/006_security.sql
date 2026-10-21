-- Block F: Kontoschutz, Sitzungstokens, Spielleiter-Rechte, Sperren und Prüfprotokoll.
ALTER TABLE accounts
    ADD COLUMN IF NOT EXISTS failed_logins   INT         NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_until    TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS totp_secret     TEXT,                 -- Base32, NULL = keine Zwei-Faktor-Anmeldung
    ADD COLUMN IF NOT EXISTS totp_last_step  BIGINT      NOT NULL DEFAULT 0,  -- verhindert Wiederverwendung eines Codes
    ADD COLUMN IF NOT EXISTS gm_level        SMALLINT    NOT NULL DEFAULT 0,  -- 0 Spieler, 1 Spielleiter, 2 Admin
    ADD COLUMN IF NOT EXISTS ban_until       TIMESTAMPTZ,          -- befristete Sperre (is_banned = dauerhaft)
    ADD COLUMN IF NOT EXISTS ban_reason      TEXT        NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS muted_until     TIMESTAMPTZ;

-- Wiederverbindungstokens: nur der SHA-256-Hash wird gespeichert, jedes Token gilt genau einmal.
CREATE TABLE IF NOT EXISTS session_tokens (
    token_hash  TEXT PRIMARY KEY,
    account_id  BIGINT      NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    expires_at  TIMESTAMPTZ NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS session_tokens_account ON session_tokens (account_id);

-- Prüfprotokoll: Anmeldungen, Sicherheitsänderungen, Spielleiter-Aktionen und Anti-Cheat-Meldungen.
CREATE TABLE IF NOT EXISTS audit_log (
    id          BIGSERIAL PRIMARY KEY,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    account_id  BIGINT,                -- Handelnder (NULL = System/Konsole)
    actor       TEXT        NOT NULL DEFAULT '',
    action      TEXT        NOT NULL,
    target      TEXT        NOT NULL DEFAULT '',
    detail      TEXT        NOT NULL DEFAULT '',
    ip          TEXT        NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS audit_log_action ON audit_log (action, created_at DESC);
CREATE INDEX IF NOT EXISTS audit_log_target ON audit_log (target, created_at DESC);
