-- 013 : problèmes signalés par les moniteurs (au retour d'une sortie ou depuis l'inventaire).
-- Un signalement est ouvert tant que traite_le est vide : il apparaît dans Révisions › À prévoir et dans la fiche EPI,
-- et se ferme à la prochaine intervention enregistrée sur l'équipement. mis_hors_service : c'est lui qui a mis
-- l'équipement hors service (il le remet en service s'il redescend à « À surveiller » ou à l'intervention).

CREATE TABLE signalement (
    id                INTEGER PRIMARY KEY,
    equipement_id     INTEGER NOT NULL REFERENCES equipement(id),
    sortie_id         INTEGER NULL REFERENCES sortie(id) ON DELETE SET NULL,
    cause             TEXT    NOT NULL,
    gravite           TEXT    NOT NULL CHECK (gravite IN ('ASurveiller', 'HorsService')),
    precision         TEXT    NOT NULL DEFAULT '',
    signale_le        TEXT    NOT NULL,
    mis_hors_service  INTEGER NOT NULL DEFAULT 0 CHECK (mis_hors_service IN (0, 1)),
    traite_le         TEXT    NULL
);
-- Garde-fou : un seul signalement ouvert par équipement (un nouveau remplace le précédent).
CREATE UNIQUE INDEX ux_signalement_ouvert ON signalement (equipement_id) WHERE traite_le IS NULL;
