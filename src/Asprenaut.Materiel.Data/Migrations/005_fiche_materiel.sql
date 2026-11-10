-- 005 : fiche matériel, comme les registres du club (onglets Stab et Détendeurs).
-- Pour tout équipement : marque, modèle, numéro de série (pour un détendeur, ceux du premier étage).
-- Pour un détendeur : montage étrier / DIN, deuxième étage (marque, modèle, numéro), D.S, manomètre, octopus et son numéro.

ALTER TABLE equipement ADD COLUMN marque TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN modele TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN numero_serie TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN montage TEXT NULL CHECK (montage IN ('Etrier', 'Din'));
ALTER TABLE equipement ADD COLUMN marque_deuxieme_etage TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN modele_deuxieme_etage TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN numero_deuxieme_etage TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN direct_system INTEGER NULL CHECK (direct_system IN (0, 1));
ALTER TABLE equipement ADD COLUMN manometre TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN octopus TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN numero_octopus TEXT NOT NULL DEFAULT '';
