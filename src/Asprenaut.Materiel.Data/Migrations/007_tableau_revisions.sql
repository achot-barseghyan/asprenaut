-- 007 : écran Révisions en tableau (À prévoir → Planifié → En atelier → Fait).
-- Les blocs ajoutés à une séance TIV sont notés (colonne « Planifié ») ; une intervention garde sa nature
-- (TIV, contrôle visuel, révision atelier, réparation...) et son intervenant (inspecteur, magasin, organisme).

CREATE TABLE seance_equipement (
    seance_id      INTEGER NOT NULL REFERENCES seance_tiv(id) ON DELETE CASCADE,
    equipement_id  INTEGER NOT NULL REFERENCES equipement(id),
    PRIMARY KEY (seance_id, equipement_id)
);

ALTER TABLE controle ADD COLUMN nature TEXT NOT NULL DEFAULT '';
ALTER TABLE controle ADD COLUMN intervenant TEXT NOT NULL DEFAULT '';
