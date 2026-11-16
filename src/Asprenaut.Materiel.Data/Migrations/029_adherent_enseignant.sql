-- 029 : un enseignant est un adhérent du club. La fiche d'enseignant (table moniteur : responsable des sorties, niveau
-- d'encadrement, inspecteur TIV) est reliée à sa fiche d'adhérent (CACI, nitrox, matériel pris en sortie).
-- Les fiches existantes sont rapprochées par l'application au démarrage (noms comparés sans accents ni majuscules).

ALTER TABLE adherent ADD COLUMN moniteur_id INTEGER NULL REFERENCES moniteur(id) ON DELETE SET NULL;

CREATE UNIQUE INDEX ux_adherent_moniteur ON adherent(moniteur_id) WHERE moniteur_id IS NOT NULL;
