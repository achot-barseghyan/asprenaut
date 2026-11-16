-- 030 : inspecteur TIV indépendant du rôle d'enseignant. Un adhérent peut être inspecteur TIV sans être enseignant : sa fiche
-- d'encadrement (table moniteur) a alors actif = 0 (pas responsable de sortie) et inspecteur_tiv = 1. Les inspecteurs sont
-- désormais lus sans regarder actif : un ancien enseignant déjà inactif ne doit pas redevenir inspecteur.

UPDATE moniteur SET inspecteur_tiv = 0 WHERE genre = 'Moniteur' AND actif = 0 AND inspecteur_tiv = 1;
