-- 021 : formation en cours de l'adhérent = une classe du club (Membres › Classes ; table moniteur, genre = 'Classe').
-- « Niveau 2 » ne veut pas dire qu'il a le niveau 2, mais qu'il le prépare : le niveau texte devient un lien vers la classe.
-- adherent.niveau reste (jamais de reconstruction de table) : il garde le dernier nom écrit, la lecture prend celui de la classe.

-- Classes manquantes pour les niveaux déjà donnés aux adhérents (« Autonome »…) : rien n'est perdu.
INSERT INTO moniteur (nom, prenom, niveau, telephone, inspecteur_tiv, actif, genre, abrege)
SELECT n.nom, '', NULL, '', 0, 1, 'Classe', UPPER(SUBSTR(n.nom, 1, 4))
FROM (SELECT DISTINCT TRIM(niveau) AS nom FROM adherent WHERE TRIM(COALESCE(niveau, '')) <> '') n
WHERE NOT EXISTS (SELECT 1 FROM moniteur m WHERE m.genre = 'Classe' AND m.nom = n.nom COLLATE NOCASE)
GROUP BY n.nom COLLATE NOCASE;

ALTER TABLE adherent ADD COLUMN classe_id INTEGER NULL REFERENCES moniteur(id);

UPDATE adherent
SET classe_id = (SELECT m.id FROM moniteur m
                 WHERE m.genre = 'Classe' AND m.nom = TRIM(adherent.niveau) COLLATE NOCASE
                 ORDER BY m.actif DESC, m.id LIMIT 1)
WHERE TRIM(COALESCE(niveau, '')) <> '';
