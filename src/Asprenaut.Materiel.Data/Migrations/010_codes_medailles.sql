-- 010 : codes des médailles du club. Les anciens codes proposés par l'application deviennent ceux des médailles QR :
-- DET-007 → A07 (détendeur adulte), GIL-012 → S12 (gilet stab). Les sorties, contrôles, atelier et séances suivent
-- (ils sont reliés par l'identifiant, pas par le code) ; le texte du journal est corrigé.
-- Un code déjà pris (A07 existe déjà) ou en double reste inchangé : il se corrige à la main dans la fiche.

CREATE TEMP TABLE renommage (ancien TEXT PRIMARY KEY, nouveau TEXT NOT NULL);

INSERT INTO renommage (ancien, nouveau)
SELECT code, printf('A%02d', CAST(substr(code, 5) AS INTEGER))
FROM equipement
WHERE code GLOB 'DET-[0-9]*' AND substr(code, 5) NOT GLOB '*[^0-9]*';

INSERT INTO renommage (ancien, nouveau)
SELECT code, printf('S%02d', CAST(substr(code, 5) AS INTEGER))
FROM equipement
WHERE code GLOB 'GIL-[0-9]*' AND substr(code, 5) NOT GLOB '*[^0-9]*';

DELETE FROM renommage WHERE nouveau IN (SELECT code FROM equipement);
DELETE FROM renommage WHERE nouveau IN (SELECT nouveau FROM renommage GROUP BY nouveau HAVING COUNT(*) > 1);

UPDATE equipement
SET code = (SELECT r.nouveau FROM renommage r WHERE r.ancien = equipement.code)
WHERE code IN (SELECT ancien FROM renommage);

-- Journal : « DET-001 « Détendeur » créé » → « A01 « Détendeur » créé » (le code le plus long d'abord : DET-010 avant DET-01).
UPDATE journal
SET details = (SELECT REPLACE(journal.details, r.ancien, r.nouveau) FROM renommage r
               WHERE instr(journal.details, r.ancien) > 0 ORDER BY length(r.ancien) DESC LIMIT 1)
WHERE EXISTS (SELECT 1 FROM renommage r WHERE instr(journal.details, r.ancien) > 0);

DROP TABLE renommage;
