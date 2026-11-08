-- 001 : schéma initial.
-- Conventions : noms en snake_case, dates/horodatages en texte ISO 8601 (UTC pour les horodatages),
-- énumérations stockées par leur nom exact (voir Core/Modeles/Enumerations.cs).

CREATE TABLE categorie (
    id   INTEGER PRIMARY KEY,
    nom  TEXT    NOT NULL UNIQUE
);

CREATE TABLE equipement (
    id                       INTEGER PRIMARY KEY,
    code                     TEXT    NOT NULL COLLATE NOCASE,
    libelle                  TEXT    NOT NULL,
    categorie_id             INTEGER NOT NULL REFERENCES categorie(id),
    taille                   TEXT    NULL,
    etat                     TEXT    NOT NULL DEFAULT 'EnService'
                                     CHECK (etat IN ('EnService', 'HorsService', 'Reforme')),
    date_prochaine_revision  TEXT    NULL,
    notes                    TEXT    NOT NULL DEFAULT '',
    cree_le                  TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_equipement_code ON equipement (code);
CREATE INDEX ix_equipement_categorie ON equipement (categorie_id);

CREATE TABLE moniteur (
    id      INTEGER PRIMARY KEY,
    nom     TEXT    NOT NULL,
    prenom  TEXT    NOT NULL,
    niveau  TEXT    NULL,
    actif   INTEGER NOT NULL DEFAULT 1 CHECK (actif IN (0, 1))
);

CREATE TABLE sortie (
    id                INTEGER PRIMARY KEY,
    date_sortie       TEXT    NOT NULL,
    type              TEXT    NOT NULL
                              CHECK (type IN ('Piscine', 'Fosse', 'Mer', 'Lac', 'Bapteme', 'Autre')),
    intitule          TEXT    NOT NULL,
    moniteur_id       INTEGER NOT NULL REFERENCES moniteur(id),
    lieu              TEXT    NOT NULL DEFAULT '',
    notes             TEXT    NOT NULL DEFAULT '',
    statut            TEXT    NOT NULL DEFAULT 'EnPreparation'
                              CHECK (statut IN ('EnPreparation', 'EnCours', 'Cloturee')),
    depart_valide_le  TEXT    NULL,
    cloturee_le       TEXT    NULL
);
CREATE INDEX ix_sortie_statut ON sortie (statut);

CREATE TABLE sortie_equipement (
    id                INTEGER PRIMARY KEY,
    sortie_id         INTEGER NOT NULL REFERENCES sortie(id) ON DELETE CASCADE,
    equipement_id     INTEGER NOT NULL REFERENCES equipement(id),
    scanne_depart_le  TEXT    NOT NULL,
    scanne_retour_le  TEXT    NULL,
    statut            TEXT    NOT NULL DEFAULT 'Dehors'
                              CHECK (statut IN ('Dehors', 'Rendu', 'Manquant'))
);
CREATE UNIQUE INDEX ux_sortie_equipement ON sortie_equipement (sortie_id, equipement_id);
-- Garde-fou : un équipement n'est "Dehors" que dans une seule sortie à la fois.
CREATE UNIQUE INDEX ux_sortie_equipement_dehors ON sortie_equipement (equipement_id) WHERE statut = 'Dehors';

CREATE TABLE journal (
    id          INTEGER PRIMARY KEY,
    horodatage  TEXT    NOT NULL,
    action      TEXT    NOT NULL,
    details     TEXT    NOT NULL DEFAULT ''
);
CREATE INDEX ix_journal_horodatage ON journal (horodatage);

-- Catégories de référence (pas des données de démonstration).
INSERT INTO categorie (nom) VALUES
    ('Blocs'), ('Gilets'), ('Détendeurs'), ('Combinaisons'),
    ('Palmes'), ('Masques'), ('Ordinateurs'), ('Autre');
