-- 003 : écran Historique.
-- Le journal note l'équipement et la sortie concernés (sans clé étrangère : le journal ne bloque aucune suppression).
-- Les entrées déjà écrites sont complétées à partir de leur texte : elles commencent par le code de l'équipement
-- (« BLOC-031 part avec la sortie 3 « … » ») et citent la sortie par « sortie N ».

ALTER TABLE journal ADD COLUMN equipement_id INTEGER NULL;
ALTER TABLE journal ADD COLUMN sortie_id INTEGER NULL;

UPDATE journal
SET equipement_id = (
    SELECT e.id FROM equipement e
    WHERE substr(journal.details, 1, length(e.code) + 1) = e.code || ' '
    ORDER BY length(e.code) DESC
    LIMIT 1)
WHERE action IN ('ScanDepart', 'RetraitDepart', 'ScanRetour', 'ManquantRetrouve', 'ManquantDeclare',
                 'EquipementCree', 'ControleEnregistre', 'EnvoiAtelier', 'RetourAtelier');

-- « …sortie 12 « Cours » », « (sortie 12) », « Sortie 12 clôturée » : le nombre qui suit le premier « ortie ».
UPDATE journal
SET sortie_id = CAST(substr(details, instr(details, 'ortie ') + 6) AS INTEGER)
WHERE action IN ('ScanDepart', 'RetraitDepart', 'ScanRetour', 'ManquantRetrouve', 'ManquantDeclare',
                 'SortieCreee', 'DepartValide', 'SortieCloturee')
  AND instr(details, 'ortie ') > 0;

UPDATE journal SET sortie_id = NULL
WHERE sortie_id IS NOT NULL AND sortie_id NOT IN (SELECT id FROM sortie);

CREATE INDEX ix_journal_equipement ON journal (equipement_id);
