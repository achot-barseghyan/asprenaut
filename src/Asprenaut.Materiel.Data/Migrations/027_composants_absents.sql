-- 027 : composants d'un détendeur notés « NON », « sans », « aucun », « - »… dans les registres importés = pas de composant.
-- Ces cases sont vidées (même règle que ServiceInventaire.SignifieAbsent pour les nouvelles saisies).

UPDATE equipement SET manometre = '' WHERE UPPER(TRIM(manometre)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
UPDATE equipement SET octopus = '' WHERE UPPER(TRIM(octopus)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
UPDATE equipement SET numero_octopus = '' WHERE UPPER(TRIM(numero_octopus)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
UPDATE equipement SET marque_deuxieme_etage = '' WHERE UPPER(TRIM(marque_deuxieme_etage)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
UPDATE equipement SET modele_deuxieme_etage = '' WHERE UPPER(TRIM(modele_deuxieme_etage)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
UPDATE equipement SET numero_deuxieme_etage = '' WHERE UPPER(TRIM(numero_deuxieme_etage)) IN ('NON', 'NON.', 'NO', 'SANS', 'AUCUN', 'AUCUNE', 'NEANT', 'NÉANT', 'N/A', 'NA', 'PAS DE', 'RIEN', '-', '--', '/', '_', '–', '—');
