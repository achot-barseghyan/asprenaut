using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>
/// Notices des fabricants livrées dans l'exe (Ressources\Manuels) avec leur sommaire et leurs délais d'entretien,
/// installées au premier lancement par <see cref="ServiceManuels.InstallerFournisAsync"/> (une seule fois).
/// </summary>
public static class ManuelsFournis
{
    private static readonly (string Ressource, string Categorie, FicheManuel Fiche)[] Catalogue =
    [
        ("scp_productmanual_regulator_vL2020-07_fr.pdf", "Détendeurs", new FicheManuel(
            "Scubapro", "Détendeurs", null,
            "MK25 EVO, MK21, MK2 EVO, MK19 EVO, MK17 EVO, MK11 · A700, S600, G260, R195", "07/2020", 24, 17,
            """
            Révision : tous les 2 ans
            Plus de 100 plongées par an : inspection 6 mois, révision 1 an
            Détendeur Nitrox : 1 an ou 100 plongées
            """,
            """
            1. Avertissements ; 4
            2. Certification EN 250 ; 4
            4. Système de détendeur ; 6
            5. Données techniques ; 7
            6. Préparation à l'utilisation ; 13
            7.2 Utilisation en eaux froides ; 15
            7.3 Après la plongée ; 16
            8.2 Entretien ; 17
            9. Nitrox ; 18
            10. Dépannage ; 20
            """)),
        ("SCP_productmanual_bc_07_2020_vO_fr.pdf", "Gilets", new FicheManuel(
            "Scubapro", "Gilets stabilisateurs", null,
            "X-Black, T-One, Go, Master Jacket, Seahawk, Classic, Level, Glide, Hydros", "07/2020", 12, 17,
            """
            Inspection et entretien : chaque année
            Usage club ou location : tous les 6 mois
            """,
            """
            1. Avertissements ; 4
            5. Réglage initial ; 6
            7. Système de lestage ; 10
            9. Fonctionnement ; 13
            11. Examen du gilet ; 15
            12. Rangement et entretien ; 17
            14 à 24. Fiches des modèles ; 18
            """)),
        ("noticeFR.pdf", "Détendeurs", new FicheManuel(
            "Aqua Lung", "Détendeurs", null,
            "Calypso, Titan, Mikron, Kronos, Legend", "08/2013", 24, 22,
            """
            Révision : tous les 2 ans
            Inspection visuelle : chaque année
            Piscine ou location : tous les 6 mois
            """,
            """
            1. Mise en garde ; 4
            1.2 Utilisation Nitrox ; 5
            2.2 Caractéristiques ; 9
            2.5 Connexion rapide Titan ; 11
            3.2 Vérifications avant plongée ; 15
            4.1 Plongée en eaux froides ; 18
            6. Entretien courant ; 21
            7. Maintenance ; 22
            9. Carnet d'entretien ; 27
            """)),
        ("Wave.pdf", "Gilets", new FicheManuel(
            "Aqua Lung", "Gilet Wave", null,
            "Inflateur Powerline, lest SureLock II · tailles XXS à XL", "2014", 12, 6,
            """
            Révision : chaque année
            Piscine ou location : tous les 3 à 6 mois
            """,
            """
            Mesures de sécurité ; 3
            Révision et inspection ; 6
            Carnet d'entretien ; 7
            Fixer le gilet sur la bouteille ; 14
            Lest intégré SureLock II ; 19
            Gonflage Powerline ; 25
            Dégonflage ; 26
            Vérifications avant plongée ; 31
            Après la plongée ; 32
            """)),
        ("mask_scubapro_2020.pdf", "Masques", new FicheManuel(
            "Scubapro", "Masques", null,
            "Tous les masques Scubapro · notice en plusieurs langues, français en page 1", "05/2020", null, 1,
            """
            Contrôle : avant chaque plongée (jupe, sangle, boucles, vitre)
            Rinçage : à l'eau douce après chaque utilisation
            Révision : aucune, à remplacer s'il est abîmé
            """,
            """
            Notice en français (colonne « Français ») ; 1
            Autres langues ; 2
            """)),
    ];

    /// <summary>
    /// Extrait dans un dossier temporaire les PDF des notices pas encore installées, compte leurs pages et les fait installer.
    /// Chaque notice ne s'installe qu'une fois (réglage <c>ManuelsFournis</c>) : rien à faire aux lancements suivants.
    /// </summary>
    public static async Task InstallerAsync(ServiceManuels manuels)
    {
        var aInstaller = Catalogue.Where(c => !manuels.EstInstalle(c.Ressource)).ToList();
        if (aInstaller.Count == 0)
        {
            return;
        }

        var dossier = Path.Combine(Path.GetTempPath(), $"asprenaut-manuels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dossier);
        try
        {
            var fournis = new List<ManuelFourni>();
            foreach (var (ressource, categorie, fiche) in aInstaller)
            {
                await using var flux = typeof(ManuelsFournis).Assembly.GetManifestResourceStream($"Manuels.{ressource}");
                if (flux is null)
                {
                    continue;
                }

                var chemin = Path.Combine(dossier, ressource);
                await using (var fichier = File.Create(chemin))
                {
                    await flux.CopyToAsync(fichier);
                }

                fournis.Add(new ManuelFourni(categorie, fiche, chemin, await LecteurPdf.CompterPagesAsync(chemin)));
            }

            await manuels.InstallerFournisAsync(fournis);
        }
        finally
        {
            try
            {
                Directory.Delete(dossier, recursive: true);
            }
            catch (IOException)
            {
                // Dossier temporaire : Windows le nettoiera.
            }
        }
    }
}
