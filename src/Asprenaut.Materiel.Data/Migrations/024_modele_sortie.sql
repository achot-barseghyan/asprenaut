-- 024 : modèles de sortie (écran Sorties, onglet « Modèles » et « Démarrage rapide ») : les sorties qui reviennent toujours
-- pareil, créées ensuite en un clic. Type ou responsable supprimé : le modèle reste, à compléter (SET NULL).

CREATE TABLE modele_sortie (
    id                INTEGER PRIMARY KEY,
    nom               TEXT    NOT NULL,
    jour              INTEGER NULL CHECK (jour BETWEEN 0 AND 6),
    type_sortie_id    INTEGER NULL REFERENCES type_sortie (id) ON DELETE SET NULL,
    lieu              TEXT    NOT NULL DEFAULT '',
    intitule          TEXT    NOT NULL DEFAULT '',
    moniteur_id       INTEGER NULL REFERENCES moniteur (id) ON DELETE SET NULL,
    utilisations      INTEGER NULL CHECK (utilisations BETWEEN 1 AND 10),
    un_par_categorie  INTEGER NOT NULL DEFAULT 1 CHECK (un_par_categorie IN (0, 1)),
    notes             TEXT    NOT NULL DEFAULT '',
    cree_le           TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_modele_sortie_nom ON modele_sortie (nom COLLATE NOCASE);
