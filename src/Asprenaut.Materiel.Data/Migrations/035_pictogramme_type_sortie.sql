-- 035 : pictogramme (28 au choix) et couleur (7 au choix) de chaque type de sortie (maquette « Ajouter un type de sortie »).
-- Les types existants reprennent ceux de leur famille. NULL = d'après la famille (colonne pictogramme).

ALTER TABLE type_sortie ADD COLUMN icone TEXT NULL;
ALTER TABLE type_sortie ADD COLUMN couleur TEXT NULL;

UPDATE type_sortie SET
    icone = CASE pictogramme
        WHEN 'Piscine' THEN 'piscine'
        WHEN 'Fosse' THEN 'fosse'
        WHEN 'Mer' THEN 'mer'
        WHEN 'Lac' THEN 'lac'
        WHEN 'Bapteme' THEN 'bapteme'
        ELSE 'autre' END,
    couleur = CASE pictogramme
        WHEN 'Piscine' THEN 'cyan'
        WHEN 'Bapteme' THEN 'vert'
        WHEN 'Mer' THEN 'corail'
        WHEN 'Fosse' THEN 'violet'
        ELSE 'bleu' END;
