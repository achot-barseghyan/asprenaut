-- 016 : Révisions en 5 étapes (À prévoir → Réparation interne → À envoyer → Chez le prestataire → Fait) et prestataires.
-- « À prévoir » reste calculé d'après les échéances ; les autres étapes sont notées dans revision (une seule ligne hors
-- « Fait » par équipement). Reprise des données : blocs prévus à une séance TIV à venir (ancien « Planifié ») → Interne,
-- passages en atelier en cours (ancien « En atelier ») → ChezPrestataire, interventions des 30 derniers jours → Fait.
-- Les passages en atelier restent l'historique des envois (fiche EPI, Historique, Bilan).

CREATE TABLE prestataire (
    id         INTEGER PRIMARY KEY,
    nom        TEXT    NOT NULL,
    type       TEXT    NOT NULL DEFAULT 'Magasin' CHECK (type IN ('Magasin', 'Organisme')),
    telephone  TEXT    NOT NULL DEFAULT '',
    adresse    TEXT    NOT NULL DEFAULT '',
    actif      INTEGER NOT NULL DEFAULT 1 CHECK (actif IN (0, 1))
);
CREATE UNIQUE INDEX ux_prestataire_nom ON prestataire (nom COLLATE NOCASE);

CREATE TABLE revision (
    id                   INTEGER PRIMARY KEY,
    equipement_id        INTEGER NOT NULL REFERENCES equipement(id),
    etape                TEXT    NOT NULL CHECK (etape IN ('Interne', 'AEnvoyer', 'ChezPrestataire', 'Fait')),
    prestataire_id       INTEGER NULL REFERENCES prestataire(id),
    motif                TEXT    NOT NULL DEFAULT '',
    note_envoi           TEXT    NOT NULL DEFAULT '',
    date_envoi           TEXT    NULL,
    date_retour_prevue   TEXT    NULL,
    intervenant_interne  TEXT    NOT NULL DEFAULT '',
    seance_tiv           INTEGER NOT NULL DEFAULT 0 CHECK (seance_tiv IN (0, 1)),
    atelier_id           INTEGER NULL REFERENCES atelier(id),
    controle_id          INTEGER NULL REFERENCES controle(id),
    cree_le              TEXT    NOT NULL,
    date_fait            TEXT    NULL,
    archive              INTEGER NOT NULL DEFAULT 0 CHECK (archive IN (0, 1)),
    date_archive         TEXT    NULL
);
-- Garde-fou : une seule révision en cours par équipement.
CREATE UNIQUE INDEX ux_revision_en_cours ON revision (equipement_id) WHERE etape <> 'Fait';
CREATE INDEX ix_revision_fait ON revision (etape, date_fait);

-- Prestataires repris des passages en atelier (un par nom, sans tenir compte des majuscules ; l'écriture la plus récente).
INSERT OR IGNORE INTO prestataire (nom)
SELECT trim(prestataire) FROM atelier WHERE trim(prestataire) <> '' ORDER BY id DESC;

-- En atelier → Chez le prestataire.
INSERT INTO revision (equipement_id, etape, prestataire_id, motif, date_envoi, date_retour_prevue, atelier_id, cree_le)
SELECT a.equipement_id, 'ChezPrestataire',
       (SELECT p.id FROM prestataire p WHERE p.nom = trim(a.prestataire) COLLATE NOCASE),
       a.motif, a.envoye_le, a.retour_prevu, a.id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM atelier a
WHERE a.revenu_le IS NULL
  AND a.id = (SELECT MAX(a2.id) FROM atelier a2 WHERE a2.equipement_id = a.equipement_id AND a2.revenu_le IS NULL);

-- Planifié (séance TIV à venir) → Réparation interne.
INSERT INTO revision (equipement_id, etape, intervenant_interne, seance_tiv, cree_le)
SELECT DISTINCT se.equipement_id, 'Interne', 'Séance TIV', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM seance_equipement se
JOIN seance_tiv s ON s.id = se.seance_id
WHERE s.date_seance >= date('now', 'localtime')
  AND se.equipement_id NOT IN (SELECT equipement_id FROM revision WHERE etape <> 'Fait');

-- Interventions des 30 derniers jours → Fait (colonne « Fait » inchangée après la mise à jour).
INSERT INTO revision (equipement_id, etape, controle_id, cree_le, date_fait)
SELECT equipement_id, 'Fait', id, saisi_le, date_controle
FROM controle
WHERE date_controle >= date('now', 'localtime', '-30 days');
