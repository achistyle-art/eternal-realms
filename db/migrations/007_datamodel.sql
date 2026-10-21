-- Block H (Kapitel 53/56): Datenmodell vervollständigen – fehlende Indizes auf Fremdschlüsseln, Wertebereiche der
-- Kontospalten und Realm-Informationen für Updates und Rollbacks.

-- Fremdschlüssel ohne Index machen Löschungen (ON DELETE CASCADE/SET NULL) langsam.
CREATE INDEX IF NOT EXISTS friends_friend ON friends (friend_id);
CREATE INDEX IF NOT EXISTS character_ignores_ignored ON character_ignores (ignored_id);
CREATE INDEX IF NOT EXISTS mail_sender ON mail (sender_id) WHERE sender_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS auctions_bidder ON auctions (bidder_id) WHERE bidder_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS guilds_leader ON guilds (leader_id) WHERE leader_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS player_reports_target ON player_reports (target_id) WHERE target_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS session_tokens_expiry ON session_tokens (expires_at);

-- Wertebereiche (Block F): Fehlversuche nie negativ, Spielleiter-Stufe 0–2.
ALTER TABLE accounts ADD CONSTRAINT accounts_failed_logins_range CHECK (failed_logins >= 0);
ALTER TABLE accounts ADD CONSTRAINT accounts_gm_level_range CHECK (gm_level BETWEEN 0 AND 2);
ALTER TABLE accounts ADD CONSTRAINT accounts_totp_secret_format CHECK (totp_secret IS NULL OR totp_secret ~ '^[A-Z2-7]{16,64}$');

-- Realm-Informationen: zuletzt gestartete Content- und Serverversion (Update-Skript, Rollback-Prüfung).
CREATE TABLE IF NOT EXISTS realm_info (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
