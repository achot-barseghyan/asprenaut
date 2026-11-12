-- 014 : adhérents du club et sorties nominatives.
-- Pour certains types de sortie (mer, lac), chaque équipement qui part est attribué à un adhérent :
-- s'il n'est pas rendu, on sait qui l'avait. Un adhérent n'est jamais supprimé : il passe inactif (actif = 0).

CREATE TABLE adherent (
    id         INTEGER PRIMARY KEY,
    nom        TEXT    NOT NULL,
    prenom     TEXT    NOT NULL,
    niveau     TEXT    NULL,
    licence    TEXT    NULL,
    telephone  TEXT    NULL,
    actif      INTEGER NOT NULL DEFAULT 1 CHECK (actif IN (0, 1))
);
-- Une licence renseignée n'appartient qu'à un adhérent (l'import Excel met à jour par licence).
CREATE UNIQUE INDEX ux_adherent_licence ON adherent (licence) WHERE licence IS NOT NULL AND licence <> '';

-- Type de sortie nominatif : le nom de l'adhérent est demandé à chaque scan au départ. Mer et Lac le sont d'origine.
ALTER TABLE type_sortie ADD COLUMN nominatif INTEGER NOT NULL DEFAULT 0 CHECK (nominatif IN (0, 1));
UPDATE type_sortie SET nominatif = 1 WHERE nom IN ('Mer', 'Lac');

-- La sortie garde le réglage de son type au moment de sa création : changer le type ensuite ne la touche pas.
-- Les sorties existantes (en cours comprises) restent non nominatives.
ALTER TABLE sortie ADD COLUMN nominatif INTEGER NOT NULL DEFAULT 0 CHECK (nominatif IN (0, 1));

-- Adhérent qui a pris l'équipement (sortie nominative), vide sinon.
ALTER TABLE sortie_equipement ADD COLUMN adherent_id INTEGER NULL REFERENCES adherent(id);

-- Un équipement déclaré manquant à la clôture garde dans le journal l'adhérent qui l'avait.
ALTER TABLE journal ADD COLUMN adherent_id INTEGER NULL;
