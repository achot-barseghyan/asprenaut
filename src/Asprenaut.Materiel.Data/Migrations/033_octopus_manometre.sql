-- 033 : fiche du détendeur complète (maquette « Ajouter un équipement » : marque, modèle et n° de série pour chaque élément).
-- La fiche gardait le modèle et le n° de l'octopus, la marque du manomètre ; s'ajoutent la marque de l'octopus, le modèle
-- et le n° de série du manomètre. Reprise : le n° d'un manomètre ajouté depuis la fiche EPI (table composant, migration 012).

ALTER TABLE equipement ADD COLUMN marque_octopus TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN modele_manometre TEXT NOT NULL DEFAULT '';
ALTER TABLE equipement ADD COLUMN numero_manometre TEXT NOT NULL DEFAULT '';

UPDATE equipement
SET numero_manometre = (SELECT c.numero_serie FROM composant c WHERE c.equipement_id = equipement.id AND c.role = 'Manomètre')
WHERE EXISTS (SELECT 1 FROM composant c WHERE c.equipement_id = equipement.id AND c.role = 'Manomètre' AND c.numero_serie <> '');
