-- 034 : sorties autorisées d'un équipement (maquette « Ajouter un équipement », section « Sorties autorisées »).
-- On note les types de sortie INTERDITS (ids de type_sortie séparés par « ; », vide = toutes les sorties) : un type créé
-- plus tard est autorisé d'office. Pas de clé étrangère : un type supprimé (jamais utilisé) laisse un id ignoré.

ALTER TABLE equipement ADD COLUMN types_sortie_interdits TEXT NOT NULL DEFAULT '';
