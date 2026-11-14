-- 019 : CACI (certificat d'absence de contre-indication) de l'adhérent. Case cochée avec une date d'expiration :
-- une fois la date passée, l'adhérent ne sort plus de matériel. Case non cochée : aucun contrôle.

ALTER TABLE adherent ADD COLUMN caci INTEGER NOT NULL DEFAULT 0 CHECK (caci IN (0, 1));
ALTER TABLE adherent ADD COLUMN caci_expiration TEXT NULL;
