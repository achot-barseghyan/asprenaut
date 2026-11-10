-- 008 : moniteurs et classes. Une classe (Niveau 1, Niveau 2...) peut être responsable d'une sortie.
-- Les classes sont rangées dans la table moniteur (genre = 'Classe') : sortie.moniteur_id reste le seul
-- responsable de la sortie, sans reconstruire la table sortie (un DROP supprimerait son matériel en cascade).
-- Pour une classe : nom = « Niveau 2 », prénom vide, abrege = « N2 » (pastille), ni téléphone, ni niveau, ni TIV.

ALTER TABLE moniteur ADD COLUMN genre TEXT NOT NULL DEFAULT 'Moniteur' CHECK (genre IN ('Moniteur', 'Classe'));
ALTER TABLE moniteur ADD COLUMN abrege TEXT NOT NULL DEFAULT '';

-- Les classes du club, modifiables (ou à rendre inactives) dans « Moniteurs et classes ».
INSERT INTO moniteur (nom, prenom, niveau, telephone, inspecteur_tiv, actif, genre, abrege) VALUES
    ('Niveau 1', '', NULL, '', 0, 1, 'Classe', 'N1'),
    ('Niveau 2', '', NULL, '', 0, 1, 'Classe', 'N2'),
    ('Niveau 3', '', NULL, '', 0, 1, 'Classe', 'N3'),
    ('Niveau 4', '', NULL, '', 0, 1, 'Classe', 'N4');
