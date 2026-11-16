-- 028 : noms génériques des registres importés (« DÉTENDEUR », « STAB », « MASQUE »…) rendus lisibles et suivis du code :
-- « Détendeur adulte A24 », « Détendeur enfant E03 », « Gilet stabilisateur S12 », « Masque M1 »
-- (même règle que ServiceInventaire.NomLisible pour les imports suivants).

UPDATE equipement
SET libelle = CASE
        WHEN code GLOB 'A[0-9]*' AND code NOT GLOB 'A*[^0-9]*' THEN 'Détendeur adulte '
        WHEN code GLOB 'E[0-9]*' AND code NOT GLOB 'E*[^0-9]*' THEN 'Détendeur enfant '
        ELSE 'Détendeur '
    END || code
WHERE TRIM(libelle) IN ('DÉTENDEUR', 'DÉTENDEURS', 'DETENDEUR', 'DETENDEURS', 'Détendeur', 'Détendeurs', 'détendeur', 'détendeurs', 'Detendeur', 'detendeur');

UPDATE equipement
SET libelle = 'Gilet stabilisateur ' || code
WHERE UPPER(TRIM(libelle)) IN ('STAB', 'STABS', 'GILET', 'GILETS', 'GILET STAB', 'GILETS STAB', 'GILET STABS');

-- Un seul mot tout en majuscules sans accent (« MASQUE », « TUBA ») : seulement la première lettre en majuscule.
UPDATE equipement
SET libelle = SUBSTR(TRIM(libelle), 1, 1) || LOWER(SUBSTR(TRIM(libelle), 2)) || ' ' || code
WHERE LENGTH(TRIM(libelle)) > 1 AND TRIM(libelle) NOT GLOB '*[^A-Z]*';
