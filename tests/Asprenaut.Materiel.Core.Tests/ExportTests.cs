using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Saisie;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.Core.Tests;

public class ExportTests
{
    [Theory]
    [InlineData("Bloc 12 L", "Bloc 12 L")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("dit \"oui\"", "\"dit \"\"oui\"\"\"")]
    [InlineData("=SOMME(A1)", "'=SOMME(A1)")] // Pas de formule exécutée par Excel.
    [InlineData("-12", "-12")]
    [InlineData(null, "")]
    public void Un_champ_csv_est_protege(string? valeur, string attendu) => Assert.Equal(attendu, Csv.Champ(valeur));

    [Fact]
    public void L_inventaire_exporte_ses_colonnes_en_francais()
    {
        var ctx = new Memoire.Contexte();
        ctx.Reglages.NomClub = "Asprenaut Chalon";
        ctx.Reglages.NumeroClub = "14 71-0025";
        var csv = ctx.Inventaire.ExporterCsv(
        [
            new LigneInventaire
            {
                Code = "S01", Libelle = "Gilet", Categorie = "Gilets", Marque = "Aqualung", Modele = "Wave", Taille = "XL", NumeroSerie = "BB388677",
                Etat = EtatEquipement.EnService, SortieEnCoursId = 4, SortieEnCoursIntitule = "Fosse",
            },
            new LigneInventaire
            {
                Code = "A05", Libelle = "Détendeur", Categorie = "Détendeurs", Marque = "Scubapro", Modele = "MK2", NumeroSerie = "0614500681",
                Montage = MontageDetendeur.Etrier, MarqueDeuxiemeEtage = "Scub", ModeleDeuxiemeEtage = "R 295", NumeroDeuxiemeEtage = "0614500681",
                DirectSystem = true, Manometre = "Scubapro", Octopus = "R 095", NumeroOctopus = "1641000963", Etat = EtatEquipement.EnService,
                DerniereRevision = new DateOnly(2021, 3, 17), DateProchaineRevision = new DateOnly(2023, 3, 17),
            },
        ]);

        var lignes = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Code;Équipement;Catégorie;Marque;Modèle;Taille;N° série;État;Statut;Sortie en cours;Date de la dernière révision;"
            + "Prochaine révision / TIV;Prochaine requalification;Étrier / DIN;Marque deuxième étage;Deuxième étage;N° deuxième étage;"
            + "D.S;Mano;Octopus;N° octopus;Identification", lignes[0]);
        Assert.Equal("S01;Gilet;Gilets;Aqualung;Wave;XL;BB388677;En service;Dehors;Fosse;;;;;;;;;;;;(Stab) S01 taille XL Asprenaut Chalon 14 71-0025", lignes[1]);
        Assert.Equal("A05;Détendeur;Détendeurs;Scubapro;MK2;;0614500681;En service;Disponible;;17/03/2021;17/03/2023;;Étrier;Scub;R 295;0614500681;"
            + "Oui;Scubapro;R 095;1641000963;(Detendeur) A05 Asprenaut Chalon 14 71-0025", lignes[2]);
    }

    [Theory]
    [InlineData("2026-11-06", GenreColonne.Date, "06/11/2026")]
    [InlineData("1", GenreColonne.OuiNon, "Oui")]
    [InlineData("HorsService", GenreColonne.EtatEquipement, "Hors service")]
    [InlineData("Bapteme", GenreColonne.TypeSortie, "Baptême")]
    [InlineData("Cloturee", GenreColonne.StatutSortie, "Clôturée")]
    [InlineData("Tiv", GenreColonne.TypeControle, "TIV")]
    [InlineData("ASurveiller", GenreColonne.ResultatControle, "À surveiller")]
    [InlineData("valeur inattendue", GenreColonne.TypeSortie, "valeur inattendue")]
    [InlineData(null, GenreColonne.Date, "")]
    public void Les_valeurs_brutes_sont_mises_en_forme(string? brut, GenreColonne genre, string attendu) =>
        Assert.Equal(attendu, ServiceExport.Formater(brut, genre));

    [Fact]
    public void Un_horodatage_utc_est_ecrit_en_heure_locale()
    {
        var utc = new DateTime(2026, 7, 14, 10, 30, 0, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Libelles.Culture), ServiceExport.Formater("2026-07-14T10:30:00.000Z", GenreColonne.Horodatage));
    }
}
