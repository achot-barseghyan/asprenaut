using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Écran Inventaire : filtres cochés, raccourcis, nombres des tuiles et du panneau, tri (règle 46).</summary>
public class FiltresInventaireTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 6);
    private readonly Contexte _ctx = new();
    private readonly List<LigneInventaire> _lignes = [];
    private readonly Dictionary<long, UtilisationInventaire> _usages = [];

    private LigneInventaire Ligne(string code, long categorie = 1, string marque = "", string? taille = null, DateOnly? revision = null,
        EtatEquipement etat = EtatEquipement.EnService, bool dehors = false, int? utilisations = null, int? seuil = null, EtatCompteur compteur = EtatCompteur.Normal)
    {
        var l = new LigneInventaire
        {
            EquipementId = _lignes.Count + 1, Code = code, Libelle = "Gilet", CategorieId = categorie, Categorie = categorie == 1 ? "Gilets" : "Détendeurs",
            Marque = marque, Taille = taille, DateProchaineRevision = revision, Etat = etat, SortieEnCoursId = dehors ? 99 : null,
        };
        _lignes.Add(l);
        if (utilisations is { } u)
        {
            _usages[l.EquipementId] = new UtilisationInventaire(u, seuil, compteur, OrigineSeuil.Categorie);
        }

        return l;
    }

    private VueInventaire Vue(FiltreInventaire filtre) => _ctx.Inventaire.Filtrer(_lignes, _usages, filtre);

    private static IEnumerable<string> Codes(VueInventaire v) => v.Lignes.Select(l => l.Code);

    private static int Nombre(VueInventaire v, FacetteInventaire facette, string valeur) =>
        v.Groupes.Single(g => g.Facette == facette).Options.Single(o => o.Valeur == valeur).Nombre;

    private void Jeu()
    {
        Ligne("S2", marque: "Aqualung", taille: "M", revision: Aujourdhui.AddDays(-3));
        Ligne("S10", marque: "AQUALUNG", taille: "XS", revision: Aujourdhui.AddDays(10), dehors: true);
        Ligne("S1", marque: "Mares", taille: "L", revision: Aujourdhui.AddDays(200), etat: EtatEquipement.HorsService);
        Ligne("A1", categorie: 2, marque: "Scubapro", revision: Aujourdhui.AddDays(-30), utilisations: 95, seuil: 100, compteur: EtatCompteur.Proche);
        Ligne("A2", categorie: 2, utilisations: 120, seuil: 100, compteur: EtatCompteur.Atteint);
    }

    [Fact]
    public void Les_valeurs_cochees_se_combinent_et_chaque_nombre_ignore_son_propre_groupe()
    {
        Jeu();
        var f = new FiltreInventaire().Basculer(FacetteInventaire.Revision, nameof(FiltreRevision.Depassee));
        var v = Vue(f);
        Assert.Equal(["A1", "S2"], Codes(v));
        Assert.Equal(2, v.RevisionsDepassees);
        Assert.Equal(2, Nombre(v, FacetteInventaire.Revision, nameof(FiltreRevision.Depassee)));
        Assert.Equal(1, Nombre(v, FacetteInventaire.Revision, nameof(FiltreRevision.Proche))); // Ce qu'on aurait en la cochant aussi.
        Assert.Equal(1, Nombre(v, FacetteInventaire.Marque, "Aqualung")); // Les autres groupes suivent le filtre.

        // Deux valeurs du même groupe : l'une ou l'autre ; deux groupes : l'un et l'autre.
        f = f.Basculer(FacetteInventaire.Revision, nameof(FiltreRevision.Proche));
        Assert.Equal(["A1", "S2", "S10"], Codes(Vue(f)));
        f = f.Basculer(FacetteInventaire.Marque, "aqualung");
        Assert.Equal(["S2", "S10"], Codes(Vue(f)));
        Assert.Equal(3, f.NombreCriteres);
        Assert.Equal(["Révision : Dépassée", "Révision : Dans 30 jours", "Marque : aqualung"], Vue(f).Etiquettes.Select(e => e.Texte));

        // Décocher retire la valeur ; « Tout effacer » garde le tri.
        f = f.Basculer(FacetteInventaire.Marque, "aqualung") with { Tri = TriInventaire.Marque };
        Assert.Equal(2, f.NombreCriteres);
        Assert.Equal((0, TriInventaire.Marque), (f.Effacer().NombreCriteres, f.Effacer().Tri));
    }

    [Fact]
    public void Les_tuiles_comptent_les_filtres_sans_la_categorie()
    {
        Jeu();
        var f = new FiltreInventaire { CategorieId = 1 }.Basculer(FacetteInventaire.Statut, nameof(StatutInventaire.Disponible));
        var v = Vue(f);
        Assert.Equal(["S2"], Codes(v));
        Assert.Equal(3, v.TotalCategories); // S2, A1, A2 disponibles.
        Assert.Equal(1, v.ParCategorie[1]);
        Assert.Equal(2, v.ParCategorie[2]);
    }

    [Fact]
    public void Marques_et_tailles_proposees_une_fois_sans_les_majuscules_et_dans_l_ordre_des_tailles()
    {
        Jeu();
        var v = Vue(new FiltreInventaire());
        var marques = v.Groupes.Single(g => g.Facette == FacetteInventaire.Marque).Options;
        Assert.Equal(["Aqualung", "Mares", "Scubapro", ""], marques.Select(o => o.Valeur)); // Écriture la plus fréquente (à égalité : la première).
        Assert.Equal((2, "Sans marque"), (marques[0].Nombre, marques[3].Libelle));
        Assert.Equal(["XS", "M", "L", ""], v.Groupes.Single(g => g.Facette == FacetteInventaire.Taille).Options.Select(o => o.Valeur));
        Assert.DoesNotContain(v.Groupes.Single(g => g.Facette == FacetteInventaire.Statut).Options, o => o.Valeur == nameof(StatutInventaire.Reforme));
    }

    [Fact]
    public void Un_raccourci_remplace_les_filtres_choisit_son_tri_et_se_retire_au_second_clic()
    {
        Jeu();
        var depart = new FiltreInventaire { CategorieId = 2 }.Basculer(FacetteInventaire.Marque, "Scubapro");
        var usure = _ctx.Inventaire.Raccourcis.Single(r => r.Cle == "usure");
        var f = ServiceInventaire.AppliquerRaccourci(depart, usure);
        Assert.Equal((2L, TriInventaire.Utilisation, true, 2), (f.CategorieId!.Value, f.Tri, f.Descendant, f.NombreCriteres));
        var v = Vue(f);
        Assert.Equal(["A2", "A1"], Codes(v)); // Le plus usé d'abord.
        Assert.True(v.Raccourcis.Single(r => r.Raccourci.Cle == "usure").Actif);
        Assert.Equal(1, v.Raccourcis.Single(r => r.Raccourci.Cle == "revision-depassee").Nombre); // Dans la catégorie : A1.

        var retire = ServiceInventaire.AppliquerRaccourci(f, usure);
        Assert.Equal((0, 2L), (retire.NombreCriteres, retire.CategorieId!.Value));
    }

    [Fact]
    public void Le_tri_suit_les_codes_naturels_les_echeances_et_met_les_vides_a_la_fin()
    {
        Jeu();
        Ligne("S3", etat: EtatEquipement.Reforme, revision: Aujourdhui.AddDays(-400));
        Assert.Equal(["A1", "A2", "S1", "S2", "S3", "S10"], Codes(Vue(new FiltreInventaire())));

        var revision = new FiltreInventaire().TrierPar(TriInventaire.Revision);
        Assert.False(revision.Descendant);
        Assert.Equal(["A1", "S2", "S10", "S1", "A2", "S3"], Codes(Vue(revision))); // Réformé : plus d'échéance.
        Assert.Equal(["S1", "S10", "S2", "A1", "A2", "S3"], Codes(Vue(revision.TrierPar(TriInventaire.Revision))));

        var marque = new FiltreInventaire().TrierPar(TriInventaire.Marque).TrierPar(TriInventaire.Marque);
        Assert.Equal(["A1", "S1", "S2", "S10", "A2", "S3"], Codes(Vue(marque))); // Z → A, sans marque à la fin.

        Assert.True(new FiltreInventaire().TrierPar(TriInventaire.Utilisation).Descendant);
        Assert.Equal(nameof(FiltreRevision.SansDate), _ctx.Inventaire.RevisionDe(_lignes[^1]).ToString());
    }
}
