-- 012 : ajout d'un composant à un détendeur (fiche de suivi EPI). Ce que la fiche matériel ne garde pas :
-- date de fabrication (mois), mise en service, origine (pièce neuve ou récupérée sur un autre détendeur) et,
-- pour le manomètre, son numéro de série. Une ligne par composant ajouté ; le modèle reste dans la fiche (equipement).

CREATE TABLE composant (
    equipement_id   INTEGER NOT NULL REFERENCES equipement(id),
    role            TEXT NOT NULL,
    numero_serie    TEXT NOT NULL DEFAULT '',
    fabrication     TEXT NULL,
    mise_en_service TEXT NULL,
    origine         TEXT NOT NULL DEFAULT 'Neuve' CHECK (origine IN ('Neuve', 'Recuperee')),
    PRIMARY KEY (equipement_id, role)
);