-- 025 : compteur d'utilisation repris d'un fichier (import Excel de l'inventaire, colonne « Utilisations ») : le matériel
-- avait déjà servi avant l'application. Une ligne par équipement (la dernière reprise remplace la précédente) ; elle ne
-- compte plus dès qu'un contrôle de l'équipement complet est daté du jour de la reprise ou après.

CREATE TABLE utilisation_reprise (
    equipement_id  INTEGER PRIMARY KEY REFERENCES equipement (id) ON DELETE CASCADE,
    utilisations   INTEGER NOT NULL CHECK (utilisations >= 0),
    reprise_le     TEXT    NOT NULL
);
