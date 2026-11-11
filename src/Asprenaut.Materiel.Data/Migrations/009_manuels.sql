-- 009 : manuels des fabricants (écran Manuels). Le PDF est copié dans le dossier « Manuels » à côté de la base ;
-- la table garde son nom de fichier, sa catégorie et ce que l'on a saisi : sommaire (une section par ligne,
-- « 8.2 Entretien ; 17 ») et délais d'entretien du fabricant (une ligne par délai).
-- Équipements liés : même catégorie et même marque (calculé, pas de table de liaison).

CREATE TABLE manuel (
    id              INTEGER PRIMARY KEY,
    marque          TEXT    NOT NULL,
    titre           TEXT    NOT NULL,
    categorie_id    INTEGER NULL REFERENCES categorie(id),
    modeles         TEXT    NOT NULL DEFAULT '',
    edition         TEXT    NOT NULL DEFAULT '',
    fichier         TEXT    NOT NULL,
    nb_pages        INTEGER NOT NULL CHECK (nb_pages >= 1),
    mois_revision   INTEGER NULL CHECK (mois_revision IS NULL OR mois_revision BETWEEN 1 AND 120),
    page_entretien  INTEGER NULL CHECK (page_entretien IS NULL OR page_entretien >= 1),
    entretien       TEXT    NOT NULL DEFAULT '',
    sommaire        TEXT    NOT NULL DEFAULT ''
);
CREATE INDEX ix_manuel_categorie ON manuel (categorie_id);
