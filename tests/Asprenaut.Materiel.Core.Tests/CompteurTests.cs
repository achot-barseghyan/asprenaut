using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Compteur d'utilisation : révision au seuil de sorties (retours scannés) depuis le dernier contrôle.</summary>
public class CompteurTests
{
    private readonly Contexte _ctx = new();

    /// <summary>Fait partir et revenir l'équipement <paramref name="fois"/> fois (une sortie chaque fois).</summary>
    private async Task SortirAsync(string code, int fois)
    {
        for (var i = 0; i < fois; i++)
        {
            var sortie = await _ctx.SortieEnCoursAsync($"Sortie {i}", code);
            await _ctx.Scan.ScannerRetourAsync(sortie, code);
        }
    }

    // Règle : seuil propre à l'équipement, sinon celui du modèle (même nom), sinon celui de la catégorie ; 0 = pas de compteur.
    [Fact]
    public void Le_seuil_le_plus_precis_l_emporte()
    {
        var r = new Reglages();
        r.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Modele, Cle = "Détendeur Mares", Sorties = 200 });
        r.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Equipement, Cle = "DET-007", Sorties = 150 });

        Assert.Equal(150, ServiceCompteur.Seuil(r, "det-007", "Détendeur Mares", "Détendeurs"));
        Assert.Equal(200, ServiceCompteur.Seuil(r, "DET-001", " détendeur mares ", "Détendeurs"));
        Assert.Equal(100, ServiceCompteur.Seuil(r, "DET-002", "Détendeur Apeks", "Détendeurs"));
        Assert.Null(ServiceCompteur.Seuil(r, "ORDI-001", "Ordinateur", "Ordinateurs")); // Désactivé par défaut.
        Assert.Null(ServiceCompteur.Seuil(r, "PALM-001", "Palmes", "Palmes"));
    }

    [Theory]
    [InlineData(89, true, EtatCompteur.Normal)]
    [InlineData(90, true, EtatCompteur.Proche)]
    [InlineData(90, false, EtatCompteur.Normal)]
    [InlineData(100, false, EtatCompteur.Atteint)]
    [InlineData(130, true, EtatCompteur.Atteint)]
    public void Etat_du_compteur(int utilisations, bool prevenir, EtatCompteur attendu) =>
        Assert.Equal(attendu, ServiceCompteur.Etat(utilisations, 100, prevenir));

    // Règle : seuil atteint → part avec un avertissement, ou refusé si « Bloquer au seuil » est activé.
    [Fact]
    public async Task Au_seuil_le_depart_avertit_ou_refuse()
    {
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 3;
        await SortirAsync("DET-001", 3);

        var sortie = await _ctx.SortieAsync("Piscine");
        var avertissement = await _ctx.Scan.ScannerDepartAsync(sortie, "DET-001");
        Assert.Equal(NiveauResultat.Avertissement, avertissement.Niveau);
        Assert.Equal("DET-001 · Détendeur ajouté. Attention : révision à faire (3 utilisations, seuil 3).", avertissement.Message);

        await _ctx.Scan.RetirerDuDepartAsync(sortie, avertissement.Equipement!.Id);
        _ctx.Reglages.BloquerAuSeuil = true;
        var refus = await _ctx.Scan.ScannerDepartAsync(sortie, "DET-001");
        Assert.Equal(NiveauResultat.Erreur, refus.Niveau);
        Assert.Equal("DET-001 · Détendeur : 3 utilisations depuis sa dernière révision (seuil 3). Révision à faire avant de repartir.", refus.Message);
    }

    // Règle : un contrôle enregistré remet le compteur à zéro (les retours du jour du contrôle sont comptés avant lui).
    [Fact]
    public async Task Un_controle_remet_le_compteur_a_zero()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 3;
        await SortirAsync("DET-001", 3);
        Assert.Equal(EtatCompteur.Atteint, (await _ctx.Compteur.UsageAsync(det))!.Etat);

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.Conforme, DateOnly.FromDateTime(Contexte.Maintenant), null)).Reussi);
        Assert.Equal(0, (await _ctx.Compteur.UsageAsync(det))!.Utilisations);

        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(1);
        await SortirAsync("DET-001", 1);
        Assert.Equal(new UsageEquipement(1, 3, EtatCompteur.Normal), await _ctx.Compteur.UsageAsync(det));
    }

    // Inventaire, colonne « Utilisation » : utilisations de chaque équipement, avec le seuil et son origine s'il y en a un.
    [Fact]
    public async Task L_inventaire_montre_les_utilisations_et_l_origine_du_seuil()
    {
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.Equipement("DET-002", categorie: "Détendeurs").Libelle = "Détendeur Mares";
        _ctx.Equipement("DET-003", categorie: "Détendeurs");
        _ctx.Equipement("BLOC-001", categorie: "Blocs");
        _ctx.Equipement("DET-009", EtatEquipement.Reforme, categorie: "Détendeurs");
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 10;
        _ctx.Reglages.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Modele, Cle = "Détendeur Mares", Sorties = 20 });
        _ctx.Reglages.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Equipement, Cle = "DET-003", Sorties = 2 });
        await SortirAsync("DET-001", 9);
        await SortirAsync("DET-003", 2);
        await SortirAsync("BLOC-001", 3);

        var u = await _ctx.Compteur.ListerUtilisationsAsync(await _ctx.Inventaire.ListerAsync(null, null));
        var parCode = (await _ctx.Inventaire.ListerAsync(null, null)).Where(l => u.ContainsKey(l.EquipementId)).ToDictionary(l => l.Code, l => u[l.EquipementId]);

        Assert.Equal(new UtilisationInventaire(9, 10, EtatCompteur.Proche, OrigineSeuil.Categorie), parCode["DET-001"]);
        Assert.Equal(new UtilisationInventaire(0, 20, EtatCompteur.Normal, OrigineSeuil.Modele), parCode["DET-002"]);
        Assert.Equal(new UtilisationInventaire(2, 2, EtatCompteur.Atteint, OrigineSeuil.Equipement), parCode["DET-003"]);
        Assert.Equal("9 / 10 utilisations", parCode["DET-001"].Texte);
        Assert.Equal("3 utilisations", parCode["BLOC-001"].Texte); // Pas de compteur pour les blocs : le nombre seul.
        Assert.Null(parCode["BLOC-001"].Seuil);
        Assert.False(parCode.ContainsKey("DET-009")); // Réformé.
    }

    // Paramètres › Ajouter un seuil spécifique : modèles et équipements de l'inventaire, avec leur seuil actuel.
    [Fact]
    public async Task Les_modeles_et_equipements_proposes_montrent_leur_seuil_actuel()
    {
        _ctx.Equipement("A05", categorie: "Détendeurs").Libelle = "Détendeur Scubapro MK2";
        var a07 = _ctx.Equipement("A07", categorie: "Détendeurs");
        a07.Libelle = "Détendeur Scubapro MK2";
        a07.Marque = "Scubapro";
        a07.Modele = "MK2";
        _ctx.Equipement("E03", categorie: "Détendeurs").Libelle = "Détendeur enfant";
        _ctx.Equipement("A99", EtatEquipement.Reforme, categorie: "Détendeurs").Libelle = "Détendeur réformé";
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Combinaisons" });
        _ctx.Equipement("COMB-001", categorie: "Combinaisons").Libelle = "Combinaison 5 mm"; // Pas de compteur : jamais proposée.
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 100;
        _ctx.Reglages.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Modele, Cle = "Détendeur Scubapro MK2", Sorties = 120 });
        _ctx.Reglages.SeuilsSpecifiques.Add(new SeuilSpecifique { Portee = PorteeSeuil.Equipement, Cle = "A07", Sorties = 150 });
        await SortirAsync("A07", 2);

        var modeles = await _ctx.Compteur.ListerModelesAsync();
        Assert.Equal([new ModeleCandidat("Détendeur enfant", "Détendeurs", 1, 100), new ModeleCandidat("Détendeur Scubapro MK2", "Détendeurs", 2, 120)], modeles);
        Assert.Single(await _ctx.Compteur.ListerModelesAsync("ENFANT"));

        var equipements = await _ctx.Compteur.ListerEquipementsAsync();
        Assert.Equal(["A05", "A07", "E03"], equipements.Select(e => e.Code));
        var a07Propose = equipements[1];
        Assert.Equal(("Scubapro MK2", 2, 150, OrigineSeuil.Equipement), (a07Propose.Fiche, a07Propose.Usage.Utilisations, a07Propose.Usage.Seuil, a07Propose.Usage.Origine));
        Assert.Equal(OrigineSeuil.Modele, equipements[0].Usage.Origine);

        // Recherche : médaille scannée, marque.
        Assert.Equal("A07", Assert.Single(await _ctx.Compteur.ListerEquipementsAsync("(Detendeur) A07 Asprenaut Chalon 14 71-0025")).Code);
        Assert.Equal(["A05", "A07"], (await _ctx.Compteur.ListerEquipementsAsync("scubapro")).Select(e => e.Code)); // Nom ou marque.
    }

    [Fact]
    public async Task Le_seuil_specifique_ajoute_prend_la_valeur_choisie_bornee()
    {
        _ctx.Equipement("A24", categorie: "Détendeurs");
        _ctx.Equipement("E03", categorie: "Détendeurs");

        Assert.True((await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Equipement, "(Detendeur) A24 Asprenaut Chalon 14 71-0025", 150)).Reussi);
        Assert.True((await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Equipement, "E03", 5000)).Reussi);

        Assert.Equal([("A24", 150), ("E03", ServiceCompteur.SeuilSpecifiqueMax)], _ctx.Reglages.SeuilsSpecifiques.Select(s => (s.Cle, s.Sorties)));
    }

    // Règle : dans Révisions, seuil atteint → en retard ; à 90 % → à planifier (si « Prévenir à 90 % »).
    [Fact]
    public async Task L_echeancier_montre_les_equipements_proches_ou_au_seuil()
    {
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        _ctx.Equipement("DET-002", categorie: "Détendeurs");
        _ctx.Reglages.SeuilsSorties["Détendeurs"] = 10;
        await SortirAsync("DET-001", 10);
        await SortirAsync("DET-002", 9);

        var usage = (await _ctx.Revisions.ListerEcheancesAsync()).Where(l => l.ParUsage).ToList();

        Assert.Equal([("DET-001", EtatEcheance.EnRetard, "10/10 utilisations"), ("DET-002", EtatEcheance.Bientot, "9/10 utilisations")],
            usage.Select(l => (l.Code, l.Etat, l.Usage!)));
        Assert.Equal("Révision fabricant · usage", usage[0].Controle);

        _ctx.Reglages.PrevenirAvantSeuil = false;
        Assert.Single(await _ctx.Revisions.ListerEcheancesAsync(), l => l.ParUsage);
    }

    [Fact]
    public async Task Un_seuil_specifique_vise_un_code_ou_un_nom_qui_existe()
    {
        _ctx.Equipement("DET-001", categorie: "Détendeurs");

        Assert.Equal("Code inconnu : DET-099.", (await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Equipement, "det-099")).Message);
        Assert.True((await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Equipement, "ASP:det-001")).Reussi);
        Assert.False((await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Equipement, "DET-001")).Reussi); // Déjà là.
        Assert.Equal("Aucun équipement ne s'appelle « Détendeur Inconnu » dans l'inventaire.",
            (await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Modele, "Détendeur Inconnu")).Message);
        Assert.True((await _ctx.Compteur.AjouterSeuilSpecifiqueAsync(PorteeSeuil.Modele, "détendeur")).Reussi);

        Assert.Equal(
            [new SeuilSpecifiqueDecrit(PorteeSeuil.Equipement, "DET-001", 100, "Détendeur"), new SeuilSpecifiqueDecrit(PorteeSeuil.Modele, "Détendeur", 100, "1 équipement")],
            await _ctx.Compteur.DecrireSeuilsSpecifiquesAsync());

        Assert.Equal(1000, _ctx.Compteur.ChangerSeuilSpecifique(PorteeSeuil.Modele, "DÉTENDEUR", 5000).Id);
        Assert.Equal(10, _ctx.Compteur.ChangerSeuilSpecifique(PorteeSeuil.Modele, "Détendeur", 0).Id);
        Assert.True(_ctx.Compteur.RetirerSeuilSpecifique(PorteeSeuil.Equipement, "DET-001").Reussi);
        Assert.Single(_ctx.Reglages.SeuilsSpecifiques);
    }

    [Fact]
    public void Le_seuil_d_une_categorie_est_borne_et_protege()
    {
        Assert.Equal(500, _ctx.Compteur.ChangerSeuilCategorie("Détendeurs", 900).Id);
        Assert.Equal(0, _ctx.Compteur.ChangerSeuilCategorie("ordinateurs", -10).Id);
        Assert.False(_ctx.Compteur.ChangerSeuilCategorie("Palmes", 50).Reussi);

        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        Assert.False(_ctx.Compteur.ChangerSeuilCategorie("Détendeurs", 100).Reussi);
        Assert.Equal(500, _ctx.Reglages.SeuilsSorties["Détendeurs"]);
    }
}
