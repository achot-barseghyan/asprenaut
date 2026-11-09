using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Saisie;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Réglages de l'écran Paramètres et règles qu'ils pilotent.</summary>
public class ReglagesTests
{
    private readonly Contexte _ctx = new();

    // --- Affichage ---

    [Fact]
    public void Le_theme_est_sombre_par_defaut_et_le_choix_est_enregistre()
    {
        Assert.Equal(ModeAffichage.Sombre, _ctx.Reglages.Affichage);
        var avant = _ctx.Stockage.Enregistrements;

        var r = _ctx.ServiceReglages.ChangerAffichage(ModeAffichage.Clair);

        Assert.True(r.Reussi);
        Assert.Equal(ModeAffichage.Clair, _ctx.Reglages.Affichage);
        Assert.Equal(avant + 1, _ctx.Stockage.Enregistrements);
        Assert.False(_ctx.ServiceReglages.ChangerAffichage((ModeAffichage)42).Reussi);
        Assert.Equal(ModeAffichage.Clair, _ctx.Reglages.Affichage);
    }

    [Fact]
    public void Le_theme_est_ecrit_en_texte_dans_le_json()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Reglages { Affichage = ModeAffichage.Clair });

        Assert.Contains("\"Affichage\":\"Clair\"", json, StringComparison.Ordinal);
        Assert.Equal(ModeAffichage.Clair, System.Text.Json.JsonSerializer.Deserialize<Reglages>(json)!.Affichage);
        Assert.Equal(ModeAffichage.Sombre, System.Text.Json.JsonSerializer.Deserialize<Reglages>("{}")!.Affichage); // Ancien fichier.
    }

    // --- Préfixe des étiquettes ---

    [Theory]
    [InlineData("club-bloc-031", "CLUB-", "BLOC-031")]
    [InlineData("asp:bloc-031", "CLUB-", "ASP:BLOC-031")]
    [InlineData("asp:bloc-031", "", "ASP:BLOC-031")]
    [InlineData(" asp: bloc-031 ", " asp: ", "BLOC-031")]
    public void Le_prefixe_retire_est_celui_des_reglages(string brut, string prefixe, string attendu) =>
        Assert.Equal(attendu, CodeScanne.Nettoyer(brut, prefixe));

    [Fact]
    public async Task Le_scan_utilise_le_prefixe_des_reglages()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.ServiceReglages.ChangerPrefixe("club-");
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "CLUB-bloc-031");

        Assert.Equal(NiveauResultat.Succes, r.Niveau);
        Assert.Equal("BLOC-031", r.Equipement?.Code);
    }

    [Fact]
    public void Prefixe_normalise_ou_refuse_s_il_est_trop_long()
    {
        Assert.True(_ctx.ServiceReglages.ChangerPrefixe(" asp: ").Reussi);
        Assert.Equal("ASP:", _ctx.Reglages.PrefixeEtiquette);

        Assert.False(_ctx.ServiceReglages.ChangerPrefixe("TROP-LONG-PREFIXE").Reussi);
        Assert.Equal("ASP:", _ctx.Reglages.PrefixeEtiquette);
    }

    // --- Révision dépassée au départ (règle 9 modulée) ---

    [Fact]
    public async Task Revision_depassee_refusee_si_le_reglage_l_interdit()
    {
        var bloc = _ctx.Equipement("BLOC-033", revision: new DateOnly(2026, 10, 1));
        _ctx.ServiceReglages.Modifier(r => r.DepartSiRevisionDepassee = false);
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-033");

        Assert.Equal(NiveauResultat.Erreur, r.Niveau);
        Assert.Contains("révision dépassée", r.Message);
        Assert.DoesNotContain(_ctx.Db.Affectations, a => a.EquipementId == bloc.Id);
    }

    [Fact]
    public async Task Revision_depassee_part_avec_avertissement_par_defaut()
    {
        _ctx.Equipement("BLOC-033", revision: new DateOnly(2026, 10, 1));
        var sortie = await _ctx.SortieAsync();

        var r = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-033");

        Assert.Equal(NiveauResultat.Avertissement, r.Niveau);
    }

    // --- Confirmation de clôture (règle 7 modulée) ---

    [Fact]
    public async Task Confirmation_demandee_seulement_s_il_reste_du_materiel_dehors()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var sortie = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031", "BLOC-032");

        Assert.True((await _ctx.Sorties.ApercuClotureAsync(sortie)).ConfirmationNecessaire);

        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-032");
        Assert.False((await _ctx.Sorties.ApercuClotureAsync(sortie)).ConfirmationNecessaire);
    }

    [Fact]
    public async Task Pas_de_confirmation_si_le_reglage_est_coupe()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031");
        _ctx.ServiceReglages.Modifier(r => r.ConfirmerCloture = false);

        var apercu = await _ctx.Sorties.ApercuClotureAsync(sortie);

        Assert.Equal(1, apercu.NbManquants);
        Assert.False(apercu.ConfirmationNecessaire);
    }

    // --- Alerte de retour ---

    [Fact]
    public async Task Sortie_en_retard_apres_le_nombre_de_jours_regle()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");
        _ctx.ServiceReglages.ChangerJoursAlerte(2);

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(1);
        Assert.False(_ctx.Sorties.EstEnRetard((await _ctx.Sorties.ObtenirResumeAsync(sortie))!));

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(2);
        Assert.True(_ctx.Sorties.EstEnRetard((await _ctx.Sorties.ObtenirResumeAsync(sortie))!));

        // Tout rendu : plus de retard.
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        Assert.False(_ctx.Sorties.EstEnRetard((await _ctx.Sorties.ObtenirResumeAsync(sortie))!));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(30, 14)]
    public void Jours_d_alerte_bornes_de_1_a_14(int demande, int attendu)
    {
        Assert.Equal(attendu, _ctx.ServiceReglages.ChangerJoursAlerte(demande));
        Assert.Equal(attendu, _ctx.Reglages.JoursAvantAlerteRetour);
    }

    // --- Révision proche ---

    [Fact]
    public void Etat_de_revision_selon_les_jours_de_prevenance()
    {
        var aujourdHui = _ctx.Horloge.AujourdHui;
        Assert.Equal(EtatRevision.Depassee, _ctx.Inventaire.EtatDeRevision(aujourdHui.AddDays(-1)));
        Assert.Equal(EtatRevision.Proche, _ctx.Inventaire.EtatDeRevision(aujourdHui.AddDays(30)));
        Assert.Equal(EtatRevision.AJour, _ctx.Inventaire.EtatDeRevision(aujourdHui.AddDays(31)));
        Assert.Equal(EtatRevision.AJour, _ctx.Inventaire.EtatDeRevision(null));

        Assert.True(_ctx.ServiceReglages.ChangerJoursRevision(60).Reussi);
        Assert.Equal(EtatRevision.Proche, _ctx.Inventaire.EtatDeRevision(aujourdHui.AddDays(45)));
        Assert.False(_ctx.ServiceReglages.ChangerJoursRevision(45).Reussi);
    }

    // --- Enregistrement ---

    [Fact]
    public void Chaque_changement_est_enregistre_et_un_refus_ne_l_est_pas()
    {
        _ctx.ServiceReglages.Modifier(r => r.SonsActifs = false);
        _ctx.ServiceReglages.ChangerClub("Asprenaut", "Marc · 06 00 00 00 00");
        Assert.Equal(2, _ctx.Stockage.Enregistrements);

        Assert.False(_ctx.ServiceReglages.ChangerClub("  ", "x").Reussi);
        Assert.False(_ctx.ServiceReglages.ChangerDossierSauvegardes(@"E:\INTERDIT").Reussi);
        Assert.Equal(2, _ctx.Stockage.Enregistrements);
        Assert.Null(_ctx.Reglages.DossierSauvegardes);

        Assert.True(_ctx.ServiceReglages.ChangerDossierSauvegardes(@"E:\Asprenaut").Reussi);
        Assert.Equal(@"E:\Asprenaut", _ctx.Reglages.DossierSauvegardes);
    }
}
