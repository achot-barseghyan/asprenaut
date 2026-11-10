-- 006 : compteur d'utilisation pondéré. Chaque type de sortie compte un nombre d'utilisations par sortie
-- (piscine, fosse, baptême, autre : 1 ; lac : 2 ; mer : 4), repris à la création d'une sortie et modifiable sur celle-ci
-- (deux plongées dans la journée : 2). Un retour scanné ajoute les utilisations de sa sortie au compteur de l'équipement.
-- Les sorties déjà enregistrées prennent la valeur de leur type.

ALTER TABLE type_sortie ADD COLUMN utilisations INTEGER NOT NULL DEFAULT 1 CHECK (utilisations BETWEEN 1 AND 10);
UPDATE type_sortie SET utilisations = 4 WHERE pictogramme = 'Mer';
UPDATE type_sortie SET utilisations = 2 WHERE pictogramme = 'Lac';

ALTER TABLE sortie ADD COLUMN utilisations INTEGER NOT NULL DEFAULT 1 CHECK (utilisations BETWEEN 1 AND 10);
UPDATE sortie SET utilisations = COALESCE((SELECT t.utilisations FROM type_sortie t WHERE t.id = sortie.type_sortie_id), 1);
