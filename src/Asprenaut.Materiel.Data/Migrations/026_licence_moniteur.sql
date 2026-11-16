-- 026 : numéro de licence d'un moniteur (fiche « Ajouter un moniteur »). Vide = non renseigné ; toujours vide pour une classe.

ALTER TABLE moniteur ADD COLUMN licence TEXT NOT NULL DEFAULT '';
