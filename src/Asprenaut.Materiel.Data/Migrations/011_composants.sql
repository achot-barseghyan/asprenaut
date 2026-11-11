-- 011 : fiche de suivi EPI. Une intervention peut viser un composant de l'équipement (premier étage, deuxième étage,
-- octopus, manomètre d'un détendeur) ; vide = l'équipement complet (toutes les interventions déjà enregistrées).

ALTER TABLE controle ADD COLUMN composant TEXT NOT NULL DEFAULT '';