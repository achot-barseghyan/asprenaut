-- 004 : types de sortie gérés dans Paramètres (ajouter, renommer, masquer, supprimer).
-- Un type a un nom libre et un pictogramme pris parmi les familles d'origine (colonne sortie.type, inchangée : elle garde
-- la famille du type choisi et sa contrainte CHECK reste valable). Les six types d'origine sont créés et les sorties
-- existantes y sont reliées d'après leur famille.

CREATE TABLE type_sortie (
    id           INTEGER PRIMARY KEY,
    nom          TEXT    NOT NULL COLLATE NOCASE,
    pictogramme  TEXT    NOT NULL CHECK (pictogramme IN ('Piscine', 'Fosse', 'Mer', 'Lac', 'Bapteme', 'Autre')),
    actif        INTEGER NOT NULL DEFAULT 1 CHECK (actif IN (0, 1)),
    ordre        INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_type_sortie_nom ON type_sortie (nom);

INSERT INTO type_sortie (nom, pictogramme, ordre) VALUES
    ('Piscine', 'Piscine', 1), ('Fosse', 'Fosse', 2), ('Mer', 'Mer', 3),
    ('Lac', 'Lac', 4), ('Baptême', 'Bapteme', 5), ('Autre', 'Autre', 6);

ALTER TABLE sortie ADD COLUMN type_sortie_id INTEGER NULL REFERENCES type_sortie(id);

UPDATE sortie SET type_sortie_id = (SELECT t.id FROM type_sortie t WHERE t.pictogramme = sortie.type);

CREATE INDEX ix_sortie_type_sortie ON sortie (type_sortie_id);
