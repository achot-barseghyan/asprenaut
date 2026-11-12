-- 015 : Nitrox. Un bloc ou un détendeur nitrox ne part, lors d'une sortie nominative, qu'avec un adhérent certifié nitrox.

ALTER TABLE equipement ADD COLUMN nitrox INTEGER NOT NULL DEFAULT 0 CHECK (nitrox IN (0, 1));
ALTER TABLE adherent ADD COLUMN nitrox INTEGER NOT NULL DEFAULT 0 CHECK (nitrox IN (0, 1));
