using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Paramètres › Club (maquette « Paramètres · Club ») : nom, numéro, responsable et téléphone, logo.</summary>
public class ClubTests
{
    private readonly Contexte _ctx = new();
    private readonly StockageLogoMemoire _logos = new();
    private readonly ServiceReglages _service;

    public ClubTests()
    {
        _service = new ServiceReglages(_ctx.Reglages, _ctx.Stockage, _ctx.Sauvegardes, _ctx.Gestion, _logos);
    }

    [Fact]
    public void La_fiche_du_club_est_enregistree_et_le_telephone_mis_en_forme()
    {
        var r = _service.ChangerClub(new SaisieClub("  Asprenaut Chalon ", " 14 71-0025 ", " Marc Petit ", "0612345678"));

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Asprenaut Chalon", _ctx.Reglages.NomClub);
        Assert.Equal("14 71-0025", _ctx.Reglages.NumeroClub);
        Assert.Equal("Marc Petit", _ctx.Reglages.ResponsableMateriel);
        Assert.Equal("06 12 34 56 78", _ctx.Reglages.TelephoneResponsable);
        Assert.Equal("Marc Petit · 06 12 34 56 78", _ctx.Reglages.ResponsableComplet);
        Assert.Equal(ChoixLogo.Fourni, _ctx.Reglages.LogoClub); // Logo gardé.
        Assert.Equal(1, _ctx.Stockage.Enregistrements);
    }

    [Theory]
    [InlineData("", "", "", "", "Indiquez le nom du club.")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901", "", "", "", "Nom du club trop long (60 caractères au plus).")]
    [InlineData("Club", "123456789012345678901", "", "", "Numéro d'affiliation trop long (20 caractères au plus).")]
    [InlineData("Club", "", "", "+33 6 12 34 56 78 poste 12", "Téléphone trop long (20 caractères au plus).")]
    public void Une_fiche_incorrecte_est_refusee_sans_rien_changer(string nom, string numero, string responsable, string telephone, string message)
    {
        var r = _service.ChangerClub(new SaisieClub(nom, numero, responsable, telephone, ChangementLogo.Remplacer([1, 2])));

        Assert.False(r.Reussi);
        Assert.Equal(message, r.Message);
        Assert.Equal("Asprenaut", _ctx.Reglages.NomClub);
        Assert.Null(_logos.Png);
        Assert.Equal(0, _ctx.Stockage.Enregistrements);
    }

    [Fact]
    public void Le_logo_se_remplace_se_retire_et_revient_a_celui_d_origine()
    {
        Assert.Null(_service.CheminLogo()); // Logo fourni avec l'appli.

        Assert.True(_service.ChangerClub(new SaisieClub("Asprenaut", "", "", "", ChangementLogo.Remplacer([1, 2, 3]))).Reussi);
        Assert.Equal(ChoixLogo.Personnalise, _ctx.Reglages.LogoClub);
        Assert.Equal([1, 2, 3], _logos.Png);
        Assert.Equal(@"C:\Club\logo.png", _service.CheminLogo());

        // Garder : l'image reste.
        Assert.True(_service.ChangerClub(new SaisieClub("Asprenaut", "", "Marc", "")).Reussi);
        Assert.Equal([1, 2, 3], _logos.Png);

        Assert.True(_service.ChangerClub(new SaisieClub("Asprenaut", "", "", "", ChangementLogo.Retirer)).Reussi);
        Assert.Equal(ChoixLogo.Aucun, _ctx.Reglages.LogoClub);
        Assert.Null(_logos.Png);
        Assert.Null(_service.CheminLogo());

        Assert.True(_service.ChangerClub(new SaisieClub("Asprenaut", "", "", "", ChangementLogo.Origine)).Reussi);
        Assert.Equal(ChoixLogo.Fourni, _ctx.Reglages.LogoClub);
    }

    [Fact]
    public void Un_logo_impossible_a_ecrire_ou_trop_lourd_ne_change_rien()
    {
        _logos.Refuser = true;
        var r = _service.ChangerClub(new SaisieClub("Autre", "", "", "", ChangementLogo.Remplacer([1])));
        Assert.Equal("Impossible d'enregistrer le logo sur ce PC.", r.Message);
        Assert.Equal("Asprenaut", _ctx.Reglages.NomClub);
        Assert.Equal(ChoixLogo.Fourni, _ctx.Reglages.LogoClub);

        _logos.Refuser = false;
        var lourd = new byte[ServiceReglages.TailleMaxLogo + 1];
        Assert.False(_service.ChangerClub(new SaisieClub("Autre", "", "", "", ChangementLogo.Remplacer(lourd))).Reussi);
        Assert.Null(_logos.Png);
    }

    [Theory]
    [InlineData("logo.png", 1000, null)]
    [InlineData("LOGO.JPG", 1000, null)]
    [InlineData("logo.webp", 1000, "Format non pris en charge. Utilisez une image PNG, JPG, BMP ou GIF.")]
    [InlineData("logo.svg", 1000, "Format non pris en charge. Utilisez une image PNG, JPG, BMP ou GIF.")]
    [InlineData("logo.png", 0, "Ce fichier est vide.")]
    [InlineData("logo.png", 3250586, "Fichier trop lourd (3,1 Mo). 2 Mo au plus.")]
    public void Le_fichier_du_logo_est_verifie(string fichier, long taille, string? erreur) =>
        Assert.Equal(erreur, ServiceReglages.VerifierFichierLogo(fichier, taille));

    [Theory]
    [InlineData("Asprenaut", "A")]
    [InlineData("club subaquatique chalonnais", "CS")]
    [InlineData("  ", "?")]
    public void Sans_logo_les_initiales_du_club_le_remplacent(string nom, string initiales) =>
        Assert.Equal(initiales, ServiceReglages.InitialesClub(nom));

    [Fact]
    public void Modifier_le_club_demande_le_code_gestionnaire()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False(_service.ChangerClub(new SaisieClub("Autre", "", "", "", ChangementLogo.Remplacer([1]))).Reussi);
        Assert.Equal("Asprenaut", _ctx.Reglages.NomClub);
        Assert.Null(_logos.Png);
    }
}
