-- 032 : autres brevets de l'adhérent (PA40, RIFAP…), séparés par « ; ». Le brevet obtenu (migration 031) n'est plus que
-- Débutant ou Niveau 1 à 4 : un autre brevet déjà noté y est déplacé. Null = aucun.

ALTER TABLE adherent ADD COLUMN autres_brevets TEXT NULL;

UPDATE adherent
SET autres_brevets = niveau_obtenu, niveau_obtenu = NULL
WHERE niveau_obtenu IS NOT NULL
  AND niveau_obtenu NOT IN ('Débutant', 'Niveau 1', 'Niveau 2', 'Niveau 3', 'Niveau 4');
