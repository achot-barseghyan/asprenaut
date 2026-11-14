using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Rappel de la sauvegarde complète : une par semaine, sur un stockage externe (règle 37).</summary>
public class RappelSauvegardeTests
{
    private static readonly DateTime Maintenant = new(2026, 11, 14, 10, 0, 0);

    [Fact]
    public void Sans_sauvegarde_complete_le_rappel_est_affiche()
    {
        var etat = RappelSauvegarde.Etat(null, Maintenant);
        Assert.True(etat.AFaire);
        Assert.Equal("Aucune sauvegarde complète pour l'instant.", etat.Message);
    }

    [Fact]
    public void Le_rappel_revient_7_jours_apres_la_derniere_sauvegarde_complete()
    {
        // 6 jours (même tard le soir) : pas encore ; 7 jours : à refaire.
        var recente = RappelSauvegarde.Etat(new DateTime(2026, 11, 8, 23, 59, 0), Maintenant);
        Assert.False(recente.AFaire);
        Assert.Equal("Dernière sauvegarde complète il y a 6 jours. Prochaine conseillée le 15/11/2026.", recente.Message);

        var ancienne = RappelSauvegarde.Etat(new DateTime(2026, 11, 7, 8, 0, 0), Maintenant);
        Assert.True(ancienne.AFaire);
        Assert.Equal("Dernière sauvegarde complète il y a 7 jours : il est temps d'en refaire une.", ancienne.Message);

        Assert.Equal("Dernière sauvegarde complète aujourd'hui. Prochaine conseillée le 21/11/2026.", RappelSauvegarde.Etat(Maintenant, Maintenant).Message);
        Assert.StartsWith("Dernière sauvegarde complète hier.", RappelSauvegarde.Etat(Maintenant.AddDays(-1), Maintenant).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Noter_une_sauvegarde_complete_enleve_le_rappel_et_retient_le_dossier()
    {
        var ctx = new Contexte();
        Assert.True(ctx.ServiceReglages.EtatSauvegardeComplete(Maintenant).AFaire);

        ctx.ServiceReglages.NoterSauvegardeComplete(Maintenant, @"E:\Asprenaut");

        Assert.False(ctx.ServiceReglages.EtatSauvegardeComplete(Maintenant.AddDays(3)).AFaire);
        Assert.Equal((Maintenant, @"E:\Asprenaut"), (ctx.Reglages.DerniereSauvegardeComplete, ctx.Reglages.DossierSauvegardeComplete));
        Assert.Equal(1, ctx.Stockage.Enregistrements);
    }

    [Fact]
    public void Un_fichier_sur_le_disque_des_donnees_n_est_pas_externe()
    {
        Assert.True(RappelSauvegarde.MemeDisque(@"C:\Users\club\Documents\sauvegarde.zip", @"C:\Users\club\AppData\Local\Asprenaut\materiel.db"));
        Assert.True(RappelSauvegarde.MemeDisque(@"c:\sauvegarde.zip", @"C:\base\materiel.db"));
        Assert.False(RappelSauvegarde.MemeDisque(@"E:\sauvegarde.zip", @"C:\base\materiel.db"));
        Assert.False(RappelSauvegarde.MemeDisque(@"\\nas\club\sauvegarde.zip", @"C:\base\materiel.db"));
    }
}
