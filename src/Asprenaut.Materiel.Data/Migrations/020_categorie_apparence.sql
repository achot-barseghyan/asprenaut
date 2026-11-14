-- 020 : pictogramme et couleur de chaque catégorie (choix de la catégorie dans la fiche d'un équipement).
-- NULL = d'après le nom (Blocs → bloc et cyan, Gilets → gilet et vert...), comme avant.

ALTER TABLE categorie ADD COLUMN icone TEXT NULL;
ALTER TABLE categorie ADD COLUMN couleur TEXT NULL;
