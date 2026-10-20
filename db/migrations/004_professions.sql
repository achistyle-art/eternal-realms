-- Berufe (Kapitel 17): dauerhafte Verzauberungen an Gegenständen.
-- Der Berufsfortschritt selbst liegt im JSON-Feld characters.world_state (Fertigkeit, Obergrenze, Rezepte je Beruf).
ALTER TABLE character_items ADD COLUMN IF NOT EXISTS enchant TEXT NOT NULL DEFAULT '';
ALTER TABLE auctions ADD COLUMN IF NOT EXISTS enchant TEXT NOT NULL DEFAULT '';
