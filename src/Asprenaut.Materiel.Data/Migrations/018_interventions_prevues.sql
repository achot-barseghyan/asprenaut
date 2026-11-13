-- 018 : interventions prévues. On voit un problème sur un équipement et on note qu'il faudra une intervention :
-- il apparaît dans Révisions › À prévoir (motif, description) jusqu'à la prochaine intervention enregistrée (close_le),
-- ou jusqu'à ce qu'on la retire (annulee = 1). Une seule ouverte par équipement.

CREATE TABLE intervention_prevue (
    id             INTEGER PRIMARY KEY,
    equipement_id  INTEGER NOT NULL REFERENCES equipement(id),
    motif          TEXT    NOT NULL,
    description    TEXT    NOT NULL DEFAULT '',
    prevue_le      TEXT    NOT NULL,
    close_le       TEXT    NULL,
    annulee        INTEGER NOT NULL DEFAULT 0 CHECK (annulee IN (0, 1))
);
CREATE UNIQUE INDEX ux_intervention_prevue_ouverte ON intervention_prevue (equipement_id) WHERE close_le IS NULL;
