using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Mode consultation / mode gestion : code gestionnaire et actions protégées.</summary>
public class GestionTests
{
    private readonly Contexte _ctx = new();

    /// <summary>Définit le code 1234 puis repasse en consultation.</summary>
    private void Proteger(string code = "1234")
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, code, code).Reussi);
        _ctx.Gestion.Verrouiller();
    }

    private Equipement NouveauBloc(string code = "BLOC-099") => new()
    {
        Code = code,
        Libelle = "Bloc 12 L",
        CategorieId = _ctx.Db.Categories.First(c => c.Nom == "Blocs").Id,
    };

    // Règle : sans code gestionnaire, rien n'est verrouillé (installation sans blocage).
    [Fact]
    public async Task Sans_code_tout_est_permis()
    {
        Assert.False(_ctx.Gestion.ProtectionActive);
        Assert.True(_ctx.Gestion.ModeGestion);
        Assert.True((await _ctx.Inventaire.CreerAsync(NouveauBloc())).Reussi);
        Assert.True(_ctx.ServiceReglages.ChangerPrefixe("CLUB-").Reussi);
    }

    [Fact]
    public void Le_code_fait_4_a_6_chiffres_saisis_deux_fois_et_n_est_pas_stocke_en_clair()
    {
        Assert.False(_ctx.Gestion.DefinirCode(null, "123", "123").Reussi);
        Assert.False(_ctx.Gestion.DefinirCode(null, "1234567", "1234567").Reussi);
        Assert.False(_ctx.Gestion.DefinirCode(null, "12a4", "12a4").Reussi);
        Assert.False(_ctx.Gestion.DefinirCode(null, "1234", "1243").Reussi);
        Assert.False(_ctx.Gestion.ProtectionActive);

        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        Assert.True(_ctx.Gestion.ProtectionActive);
        Assert.True(_ctx.Gestion.ModeGestion); // Celui qui vient de le définir reste en mode gestion.
        Assert.DoesNotContain("1234", _ctx.Reglages.CodeGestionnaire!, StringComparison.Ordinal);
        Assert.StartsWith("pbkdf2-sha256:", _ctx.Reglages.CodeGestionnaire);
    }

    // Règle : changer (ou retirer) le code demande l'ancien.
    [Fact]
    public void Changer_le_code_demande_l_ancien()
    {
        Proteger("1234");

        Assert.False(_ctx.Gestion.DefinirCode(null, "5678", "5678").Reussi);
        Assert.False(_ctx.Gestion.DefinirCode("0000", "5678", "5678").Reussi);
        Assert.True(_ctx.Gestion.DefinirCode("1234", "5678", "5678").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False(_ctx.Gestion.Deverrouiller("1234").Reussi);
        Assert.True(_ctx.Gestion.Deverrouiller("5678").Reussi);

        Assert.False(_ctx.Gestion.RetirerCode("1234").Reussi);
        Assert.True(_ctx.Gestion.RetirerCode("5678").Reussi);
        Assert.False(_ctx.Gestion.ProtectionActive);
    }

    // Règle : avec un code, les actions protégées sont refusées en consultation ; scanner et créer une sortie restent libres.
    [Fact]
    public async Task En_consultation_les_actions_protegees_sont_refusees()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        Proteger();

        Assert.Equal(ServiceGestion.MessageVerrouille, (await _ctx.Inventaire.CreerAsync(NouveauBloc())).Message);
        Assert.False((await _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Prenom = "Léa", Nom = "Roux", Actif = true })).Reussi);
        Assert.False((await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, DateOnly.FromDateTime(Contexte.Maintenant), _ctx.Pascal.Id)).Reussi);
        Assert.False((await _ctx.Revisions.EnvoyerEnAtelierAsync("BLOC-031", "Aqua", "", null)).Reussi);
        Assert.False(_ctx.ServiceReglages.ChangerPrefixe("CLUB-").Reussi);
        Assert.False(_ctx.ServiceReglages.Modifier(r => r.SonsActifs = false).Reussi);
        Assert.True(_ctx.Reglages.SonsActifs);
        Assert.False(_ctx.ServiceReglages.ChangerAffichage(ModeAffichage.Clair).Reussi); // Le thème aussi est protégé.
        Assert.Equal(ModeAffichage.Sombre, _ctx.Reglages.Affichage);

        var vide = await _ctx.SortieAsync("Vide");
        Assert.False((await _ctx.Sorties.SupprimerAsync(vide)).Reussi);

        // Libres pour tous : créer une sortie, scanner le départ et le retour, valider, clôturer sans manquant.
        var sortie = await _ctx.SortieAsync("Piscine");
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031")).Niveau);
        Assert.True((await _ctx.Sorties.ValiderDepartAsync(sortie)).Reussi);
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031")).Niveau);
        Assert.True((await _ctx.Sorties.CloturerAsync(sortie)).Reussi);

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        Assert.True((await _ctx.Inventaire.CreerAsync(NouveauBloc())).Reussi);
        Assert.True((await _ctx.Sorties.SupprimerAsync(vide)).Reussi);
    }

    // Règle : sans activité pendant la durée choisie, le mode gestion se referme ; l'activité le prolonge.
    [Fact]
    public void Le_mode_gestion_se_referme_apres_la_duree_sans_activite()
    {
        Proteger();
        _ctx.Reglages.MinutesDeverrouillage = 15;
        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        var changements = 0;
        _ctx.Gestion.EtatChange += (_, _) => changements++;

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(10);
        _ctx.Gestion.NoterActivite();
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(24);
        Assert.True(_ctx.Gestion.ModeGestion);
        Assert.Equal(TimeSpan.FromMinutes(1), _ctx.Gestion.TempsRestant);

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(25);
        Assert.False(_ctx.Gestion.ModeGestion);
        Assert.Null(_ctx.Gestion.TempsRestant);
        Assert.Equal(1, changements);
    }

    [Fact]
    public void Trop_de_codes_faux_bloquent_une_minute()
    {
        Proteger();
        for (var i = 0; i < ServiceGestion.EssaisAvantBlocage; i++)
        {
            Assert.False(_ctx.Gestion.Deverrouiller("0000").Reussi);
        }

        var bloque = _ctx.Gestion.Deverrouiller("1234");
        Assert.False(bloque.Reussi);
        Assert.Contains("Réessayez", bloque.Message);

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(1);
        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
    }

    // Règle : clôturer en laissant des manquants demande le code seulement si le réglage est activé.
    [Fact]
    public async Task Cloturer_avec_des_manquants_demande_le_code_si_le_reglage_est_actif()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var libre = await _ctx.SortieEnCoursAsync("Libre", "BLOC-031");
        var protegee = await _ctx.SortieEnCoursAsync("Protégée", "BLOC-032");
        Proteger();

        Assert.True((await _ctx.Sorties.CloturerAsync(libre)).Reussi); // Réglage coupé : libre.

        _ctx.Reglages.CodePourCloturerAvecManquants = true;
        Assert.False((await _ctx.Sorties.CloturerAsync(protegee)).Reussi);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.Db.Affectations.Single(a => a.SortieId == protegee).Statut);

        _ctx.Gestion.Deverrouiller("1234");
        Assert.True((await _ctx.Sorties.CloturerAsync(protegee)).Reussi);
    }

    // Règle : « Verrouiller à la fermeture » coupé → le mode gestion laissé ouvert est repris au lancement suivant.
    [Fact]
    public void Le_mode_gestion_est_repris_au_lancement_si_on_ne_verrouille_pas_a_la_fermeture()
    {
        Proteger();
        _ctx.Gestion.Deverrouiller("1234");

        _ctx.Gestion.PreparerFermeture();
        Assert.False(new ServiceGestion(_ctx.Reglages, _ctx.Stockage, _ctx.Horloge).ModeGestion); // Par défaut : reverrouillé.

        _ctx.Reglages.VerrouillerALaFermeture = false;
        _ctx.Gestion.PreparerFermeture();
        Assert.True(new ServiceGestion(_ctx.Reglages, _ctx.Stockage, _ctx.Horloge).ModeGestion);

        _ctx.Gestion.Verrouiller();
        _ctx.Gestion.PreparerFermeture();
        Assert.False(new ServiceGestion(_ctx.Reglages, _ctx.Stockage, _ctx.Horloge).ModeGestion);
    }

    [Fact]
    public void La_duree_de_deverrouillage_est_5_15_30_ou_60_minutes()
    {
        Assert.True(_ctx.ServiceReglages.ChangerMinutesDeverrouillage(30).Reussi);
        Assert.Equal(30, _ctx.Reglages.MinutesDeverrouillage);
        Assert.False(_ctx.ServiceReglages.ChangerMinutesDeverrouillage(20).Reussi);
        Assert.Equal(30, _ctx.Reglages.MinutesDeverrouillage);
    }
}
