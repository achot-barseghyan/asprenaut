using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class BilanTests
{
    private readonly Contexte _ctx = new();

    public BilanTests()
    {
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Gilets" });
    }

    private Equipement Gilet(string code, string taille)
    {
        var e = _ctx.Equipement(code, categorie: "Gilets");
        e.Libelle = "Gilet stabilisateur";
        e.Taille = taille;
        return e;
    }

    /// <summary>Sortie à cette date : scans au départ, départ validé, clôturée si demandé (le reste devient manquant).</summary>
    private async Task<long> SortieAsync(DateOnly date, TypeSortie type, string[] codes, string[]? rendus = null, bool valider = true)
    {
        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(date, type, $"{Libelles.De(type)} du {date:dd/MM}", _ctx.Pascal.Id));
        var id = r.Id!.Value;
        foreach (var code in codes)
        {
            await _ctx.Scan.ScannerDepartAsync(id, code);
        }

        if (!valider)
        {
            return id;
        }

        await _ctx.Sorties.ValiderDepartAsync(id);
        if (rendus is not null)
        {
            foreach (var code in rendus)
            {
                await _ctx.Scan.ScannerRetourAsync(id, code);
            }

            await _ctx.Sorties.CloturerAsync(id);
        }

        return id;
    }

    [Fact]
    public void Les_saisons_proposees_sont_la_precedente_et_celle_en_cours()
    {
        var saisons = _ctx.Bilan.ListerSaisons();

        Assert.Equal([2025, 2026], saisons.Select(s => s.AnneeDebut));
        Assert.Equal("2026-2027", saisons[1].Libelle);
        Assert.True(saisons[1].EnCours);
        Assert.Equal((new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31)), (saisons[0].Debut, saisons[0].Fin));
    }

    // Règle : le bilan ne compte que les sorties parties (départ validé) datées dans la saison (1er septembre → 31 août).
    [Fact]
    public async Task Seules_les_sorties_parties_de_la_saison_comptent()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        await SortieAsync(new DateOnly(2025, 10, 4), TypeSortie.Piscine, ["BLOC-031", "BLOC-032"], ["BLOC-031", "BLOC-032"]);
        await SortieAsync(new DateOnly(2026, 5, 16), TypeSortie.Mer, ["BLOC-031"], ["BLOC-031"]);
        await SortieAsync(new DateOnly(2025, 8, 31), TypeSortie.Piscine, ["BLOC-031"], ["BLOC-031"]); // Saison d'avant.
        await SortieAsync(new DateOnly(2026, 9, 2), TypeSortie.Fosse, ["BLOC-031"], ["BLOC-031"]); // Saison suivante.
        await SortieAsync(new DateOnly(2026, 6, 20), TypeSortie.Lac, ["BLOC-032"], valider: false); // Jamais partie.

        var bilan = await _ctx.Bilan.ObtenirAsync(2025);

        Assert.Equal(2, bilan.Sorties);
        Assert.Equal(3, bilan.EquipementsSortis);
        Assert.False(bilan.Saison.EnCours);
        Assert.Equal(("Sorties", "2", "Octobre 2025 à mai 2026"), (bilan.Chiffres[0].Libelle, bilan.Chiffres[0].Valeur, bilan.Chiffres[0].Precision));
        Assert.Equal("2 en moyenne par sortie", bilan.Chiffres[1].Precision);
        Assert.Equal(["Mer", "Piscine"], bilan.Types.Select(t => t.Libelle).Order());
    }

    [Fact]
    public async Task Les_mois_vont_de_septembre_a_juin_et_l_ete_n_apparait_que_s_il_y_a_eu_des_sorties()
    {
        _ctx.Equipement("BLOC-031");
        await SortieAsync(new DateOnly(2025, 10, 4), TypeSortie.Piscine, ["BLOC-031"], ["BLOC-031"]);
        await SortieAsync(new DateOnly(2025, 10, 11), TypeSortie.Piscine, ["BLOC-031"], ["BLOC-031"]);

        var sansEte = await _ctx.Bilan.ObtenirAsync(2025);
        Assert.Equal(["Sep", "Oct", "Nov", "Déc", "Jan", "Fév", "Mar", "Avr", "Mai", "Juin"], sansEte.Mois.Select(m => m.Libelle));
        Assert.Equal(2, sansEte.Mois[1].Sorties);
        Assert.Equal("Pic en octobre : 2 sorties", sansEte.NoteMois);

        await SortieAsync(new DateOnly(2026, 8, 1), TypeSortie.Mer, ["BLOC-031"], ["BLOC-031"]);
        var avecAout = await _ctx.Bilan.ObtenirAsync(2025);
        Assert.Equal("Août", avecAout.Mois[^1].Libelle);
        Assert.DoesNotContain(avecAout.Mois, m => m.Libelle == "Juil");
    }

    // Règle : manquant à la clôture et toujours manquant = perdu ; rendu après la clôture = retrouvé.
    [Fact]
    public async Task Les_manquants_de_la_saison_sont_perdus_ou_retrouves()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        var mer = await SortieAsync(new DateOnly(2026, 11, 1), TypeSortie.Mer, ["BLOC-031", "BLOC-032", "DET-001"], ["BLOC-031"]);
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(2);
        await _ctx.Scan.ScannerRetourAsync(mer, "DET-001");

        var bilan = await _ctx.Bilan.ObtenirAsync(2026);

        Assert.Equal(2, bilan.Pertes.Count);
        Assert.Equal(("BLOC-032", false), (bilan.Pertes[0].Code, bilan.Pertes[0].Retrouve)); // Les perdus d'abord.
        Assert.Equal(("DET-001", true, new DateOnly(2026, 11, 8)), (bilan.Pertes[1].Code, bilan.Pertes[1].Retrouve, bilan.Pertes[1].RetrouveLe));
        Assert.Equal(("2", "1 retrouvé, 1 perdu"), (bilan.Chiffres[2].Valeur, bilan.Chiffres[2].Precision));
        Assert.Equal("Depuis septembre 2026", bilan.Chiffres[0].Precision);
        Assert.Equal("Saison en cours", bilan.NoteMois);
    }

    // Règle : tension = part du stock (catégorie + taille, hors réformé) partie lors de la sortie la plus chargée ;
    // rupture = sortie où tout le stock du groupe était dehors.
    [Fact]
    public async Task La_tension_sur_le_stock_compte_le_pic_et_les_ruptures_par_groupe()
    {
        Gilet("GIL-001", "M");
        Gilet("GIL-002", "M");
        Gilet("GIL-003", "L");
        Gilet("GIL-004", "L");
        Gilet("GIL-005", "L");
        _ctx.Equipement("GIL-006", EtatEquipement.Reforme, categorie: "Gilets").Taille = "L"; // Hors stock.
        Gilet("GIL-007", "XS"); // Seul de sa taille : pas de tension à montrer.
        await SortieAsync(new DateOnly(2026, 10, 3), TypeSortie.Piscine, ["GIL-001", "GIL-002", "GIL-003"], ["GIL-001", "GIL-002", "GIL-003"]);
        await SortieAsync(new DateOnly(2026, 10, 10), TypeSortie.Piscine, ["GIL-001", "GIL-002", "GIL-003", "GIL-004"], ["GIL-001", "GIL-002", "GIL-003", "GIL-004"]);
        await SortieAsync(new DateOnly(2026, 10, 17), TypeSortie.Piscine, ["GIL-001", "GIL-007"], ["GIL-001", "GIL-007"]);

        var tension = (await _ctx.Bilan.ObtenirAsync(2026)).Tension;

        Assert.Equal(2, tension.Count);
        Assert.Equal(new TensionBilan("Gilets taille M", 2, 2, 2), tension[0]);
        Assert.Equal(100, tension[0].Pourcentage);
        Assert.Equal(new TensionBilan("Gilets taille L", 3, 2, 0), tension[1]);
        Assert.Equal(67, tension[1].Pourcentage);
    }

    [Fact]
    public async Task Le_classement_donne_les_plus_utilises_et_les_peu_sortis()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("BLOC-033");
        _ctx.Equipement("BLOC-034", EtatEquipement.HorsService); // En panne : pas « peu sorti ».
        for (var jour = 1; jour <= 3; jour++)
        {
            await SortieAsync(new DateOnly(2026, 10, jour), TypeSortie.Piscine, ["BLOC-031", "BLOC-032"], ["BLOC-031", "BLOC-032"]);
        }

        await SortieAsync(new DateOnly(2026, 10, 10), TypeSortie.Fosse, ["BLOC-031"], ["BLOC-031"]);

        var bilan = await _ctx.Bilan.ObtenirAsync(2026);

        Assert.Equal([("BLOC-031", 4), ("BLOC-032", 3)], bilan.PlusUtilises.Select(u => (u.Code, u.Sorties)));
        Assert.Equal([("BLOC-033", 0)], bilan.PeuSortis.Select(u => (u.Code, u.Sorties)));
        Assert.Equal([("Piscine", 3), ("Fosse", 1)], bilan.Types.Select(t => (t.Libelle, t.Sorties)));
    }

    [Fact]
    public async Task Les_controles_de_la_saison_sont_comptes_par_famille()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var bloc2 = _ctx.Equipement("BLOC-032");
        var detendeur = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.DepotRevisions.Controles.AddRange(
        [
            new Controle { EquipementId = bloc.Id, Type = TypeControle.Tiv, DateControle = new DateOnly(2025, 11, 2) },
            new Controle { EquipementId = bloc2.Id, Type = TypeControle.Tiv, DateControle = new DateOnly(2026, 3, 2) },
            new Controle { EquipementId = detendeur.Id, Type = TypeControle.Revision, DateControle = new DateOnly(2026, 2, 2) },
            new Controle { EquipementId = bloc.Id, Type = TypeControle.Tiv, DateControle = new DateOnly(2026, 9, 5) }, // Saison suivante.
        ]);

        var bilan = await _ctx.Bilan.ObtenirAsync(2025);

        Assert.Equal([new RevisionsBilan("TIV", 2), new RevisionsBilan("Détendeurs", 1)], bilan.Revisions);
        Assert.Equal(("3", "2 TIV, 1 détendeur"), (bilan.Chiffres[3].Valeur, bilan.Chiffres[3].Precision));
    }

    [Fact]
    public async Task Une_saison_vide_donne_des_messages_simples()
    {
        var bilan = await _ctx.Bilan.ObtenirAsync(2025);

        Assert.Equal(0, bilan.Sorties);
        Assert.Equal(["Aucune sortie", "Aucun départ", "Tout est rentré", "Aucun contrôle enregistré"], bilan.Chiffres.Select(c => c.Precision));
        Assert.Equal("Aucune sortie", bilan.NoteMois);
        Assert.Empty(bilan.Tension);
    }
}
