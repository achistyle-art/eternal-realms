-- Alpha-Vorbereitung: Einladungscodes für eine geschlossene Alpha und Fehlermeldungen der Tester aus dem Spiel.

-- Einladungscodes: gespeichert wird nur der SHA-256-Hash; der Code selbst wird beim Anlegen einmal angezeigt.
CREATE TABLE IF NOT EXISTS invite_codes (
    code_hash   TEXT PRIMARY KEY CHECK (code_hash ~ '^[0-9A-F]{64}$'),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by  TEXT        NOT NULL DEFAULT '',
    note        TEXT        NOT NULL DEFAULT '' CHECK (length(note) <= 200),
    expires_at  TIMESTAMPTZ,
    used_at     TIMESTAMPTZ,
    used_by     BIGINT REFERENCES accounts(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS invite_codes_used_by ON invite_codes (used_by) WHERE used_by IS NOT NULL;

-- Fehlermeldungen (/bug): Ort, Stufe und Versionen werden vom Server ergänzt, nicht vom Client übernommen.
CREATE TABLE IF NOT EXISTS bug_reports (
    id               BIGSERIAL PRIMARY KEY,
    at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    account_id       BIGINT REFERENCES accounts(id) ON DELETE SET NULL,
    character_name   TEXT     NOT NULL DEFAULT '',
    category         TEXT     NOT NULL DEFAULT 'Fehler' CHECK (category IN ('Fehler', 'Balance', 'Text', 'Absturz', 'Vorschlag')),
    text             TEXT     NOT NULL CHECK (length(text) BETWEEN 1 AND 2000),
    zone             TEXT     NOT NULL DEFAULT '',
    pos_x            REAL     NOT NULL DEFAULT 0,
    pos_z            REAL     NOT NULL DEFAULT 0,
    level            SMALLINT NOT NULL DEFAULT 0,
    content_version  TEXT     NOT NULL DEFAULT '',
    server_version   TEXT     NOT NULL DEFAULT '',
    client_info      TEXT     NOT NULL DEFAULT '' CHECK (length(client_info) <= 200),
    handled          BOOLEAN  NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS bug_reports_open ON bug_reports (at DESC) WHERE NOT handled;
CREATE INDEX IF NOT EXISTS bug_reports_account ON bug_reports (account_id) WHERE account_id IS NOT NULL;
