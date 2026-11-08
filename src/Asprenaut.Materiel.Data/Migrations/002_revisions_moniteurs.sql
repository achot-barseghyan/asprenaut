-- 002 : écrans Moniteurs et Révisions.
-- Moniteurs : téléphone, habilitation TIV. Blocs : prochaine requalification.
-- Historique des contrôles, passages en atelier, séances TIV.

ALTER TABLE moniteur ADD COLUMN telephone TEXT NOT NULL DEFAULT '';
ALTER TABLE moniteur ADD COLUMN inspecteur_tiv INTEGER NOT NULL DEFAULT 0 CHECK (inspecteur_tiv IN (0, 1));

ALTER TABLE equipement ADD COLUMN date_requalification TEXT NULL;

CREATE TABLE controle (
    id             INTEGER PRIMARY KEY,
    equipement_id  INTEGER NOT NULL REFERENCES equipement(id),
    type           TEXT    NOT NULL CHECK (type IN ('Tiv', 'Requalification', 'Revision')),
    date_controle  TEXT    NOT NULL,
    resultat       TEXT    NOT NULL CHECK (resultat IN ('Conforme', 'ASurveiller', 'Refuse')),
    inspecteur_id  INTEGER NULL REFERENCES moniteur(id),
    notes          TEXT    NOT NULL DEFAULT '',
    saisi_le       TEXT    NOT NULL
);
CREATE INDEX ix_controle_equipement ON controle (equipement_id, type, date_controle);

CREATE TABLE atelier (
    id             INTEGER PRIMARY KEY,
    equipement_id  INTEGER NOT NULL REFERENCES equipement(id),
    prestataire    TEXT    NOT NULL,
    motif          TEXT    NOT NULL DEFAULT '',
    envoye_le      TEXT    NOT NULL,
    retour_prevu   TEXT    NULL,
    revenu_le      TEXT    NULL
);
-- Garde-fou : un équipement n'est en atelier qu'une fois à la fois.
CREATE UNIQUE INDEX ux_atelier_en_cours ON atelier (equipement_id) WHERE revenu_le IS NULL;

CREATE TABLE seance_tiv (
    id           INTEGER PRIMARY KEY,
    date_seance  TEXT    NOT NULL,
    heure        TEXT    NOT NULL DEFAULT '',
    lieu         TEXT    NOT NULL DEFAULT ''
);
CREATE INDEX ix_seance_tiv_date ON seance_tiv (date_seance);
