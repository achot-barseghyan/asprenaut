-- 017 : Sortie nominative : un seul équipement par catégorie et par adhérent (pas deux détendeurs, deux blocs, deux gilets).
-- Activé par défaut ; le désactiver pour une sortie demande le mode gestion.

ALTER TABLE sortie ADD COLUMN un_par_categorie INTEGER NOT NULL DEFAULT 1 CHECK (un_par_categorie IN (0, 1));
