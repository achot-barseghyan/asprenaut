-- 022 : couleur de la pastille d'une classe (fenêtre « Ajouter une classe »), clé de Moniteur.CouleursClasse.
-- NULL = d'après le numéro de la classe ; toujours NULL pour un moniteur.

ALTER TABLE moniteur ADD COLUMN couleur TEXT NULL;
