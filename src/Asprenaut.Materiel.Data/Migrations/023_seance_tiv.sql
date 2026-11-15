-- 023 : séance TIV configurable (Paramètres › Configuration du club › Séance TIV) : note, annulation, inspecteurs choisis.
-- Reprise : chaque séance existante reçoit les inspecteurs TIV actifs (ceux qu'elle affichait jusqu'ici).

ALTER TABLE seance_tiv ADD COLUMN note TEXT NOT NULL DEFAULT '';
ALTER TABLE seance_tiv ADD COLUMN annulee INTEGER NOT NULL DEFAULT 0 CHECK (annulee IN (0, 1));

CREATE TABLE seance_inspecteur (
    seance_id    INTEGER NOT NULL REFERENCES seance_tiv (id) ON DELETE CASCADE,
    moniteur_id  INTEGER NOT NULL REFERENCES moniteur (id),
    PRIMARY KEY (seance_id, moniteur_id)
);

INSERT INTO seance_inspecteur (seance_id, moniteur_id)
SELECT s.id, m.id
FROM seance_tiv s
JOIN moniteur m ON m.inspecteur_tiv = 1 AND m.actif = 1 AND m.genre = 'Moniteur';
