-- 031 : niveau de plongée déjà obtenu par l'adhérent (« Niveau 1 »… ou un autre niveau tapé : PA40, Guide de palanquée).
-- À côté de la formation en cours (classe suivie, migration 021). Null = non renseigné.

ALTER TABLE adherent ADD COLUMN niveau_obtenu TEXT NULL;
