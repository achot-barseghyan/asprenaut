using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Étape 1 : scan du matériel qui part.</summary>
public sealed class EcranDepart : EcranScanBase
{
    private readonly ServiceSortie _sorties;
    private readonly ServiceScan _scan;
    private readonly IHorloge _horloge;
    private readonly Pastille _compte = Compteur(Theme.Depart, Theme.DepartClair);
    private readonly ListeMateriel _liste = new(StyleListe.Depart, "Rien n'est encore scanné. Scannez la première étiquette.");
    private readonly BoutonArrondi _valider = new("Valider le départ", StyleBouton.Depart, Icones.Coche, iconeApres: true);
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = new Padding(0, 0, 0, 10) };
    private IReadOnlyList<LigneSortie> _lignes = [];
    private StatutSortie _statut;

    public EcranDepart(ServiceSortie sorties, ServiceScan scan, IHorloge horloge, ServiceReglages reglages, INavigation navigation)
        : base(navigation, reglages, ModeScan.Depart)
    {
        _sorties = sorties;
        _scan = scan;
        _horloge = horloge;
        _valider.AutoSize = false;
        _valider.Font = Theme.PoliceGrasse(12f);
        _valider.Margin = new Padding(0, 12, 0, 0);
        _valider.Click += (_, _) => Executer(ValiderAsync);
        _filtre.Changement += (_, _) => AfficherListe();

        var gauche = Ui.Colonne(
            (ZoneScan, SizeType.Percent, 100),
            (ZoneScan.Bandeau, SizeType.Absolute, 76 + 16));
        ZoneScan.Bandeau.Margin = new Padding(0, 16, 0, 0);
        gauche.Margin = new Padding(0, 0, 22, 0);

        var carteListe = new Carte();
        carteListe.Controls.Add(Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("Matériel de la sortie"), _compte), SizeType.Absolute, HauteurEnteteListe),
            (_filtre, SizeType.Absolute, 44 + 10),
            (_liste, SizeType.Percent, 100),
            (_valider, SizeType.Absolute, 56 + 12)));

        Composer(Ui.Colonnes((gauche, SizeType.Percent, 58), (carteListe, SizeType.Percent, 42)));
    }

    public override string Titre => "Départ du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        _statut = resume.Statut;
        AfficherSortie(resume);

        var enPreparation = _statut == StatutSortie.EnPreparation;
        _compte.Text = lignes.Count.ToString(Libelles.Culture);
        _valider.Text = enPreparation ? "Valider le départ" : "Revenir au retour";
        _valider.Icone = enPreparation ? Icones.Coche : Icones.Fleche;
        _valider.Style = enPreparation ? StyleBouton.Depart : StyleBouton.Retour;
        _valider.Visible = _statut != StatutSortie.Cloturee;

        _lignes = lignes;
        _filtre.Afficher(lignes.Count, Categories(lignes, l => true));
        AfficherListe();
    }

    /// <summary>Catégories présentes dans la sortie (ordre alphabétique), avec le nombre d'équipements retenus.</summary>
    internal static IEnumerable<(string, int)> Categories(IEnumerable<LigneSortie> lignes, Func<LigneSortie, bool> compte) =>
        lignes.GroupBy(l => l.Categorie)
            .OrderBy(g => g.Key, StringComparer.Create(Libelles.Culture, true))
            .Select(g => (g.Key, g.Count(compte)));

    private void AfficherListe()
    {
        var enPreparation = _statut == StatutSortie.EnPreparation;
        var aujourdHui = _horloge.AujourdHui;
        _liste.DefinirVide(_filtre.Selection is null
            ? "Rien n'est encore scanné. Scannez la première étiquette."
            : "Rien dans cette catégorie.");
        _liste.Afficher(
            _lignes
                .Where(l => _filtre.Accepte(l.Categorie))
                .OrderByDescending(l => l.ScanneDepartLe)
                .Select(l => new ElementMateriel(
                    l.EquipementId,
                    l.Code,
                    string.IsNullOrWhiteSpace(l.Taille) ? l.Libelle : $"{l.Libelle} · {l.Taille}",
                    l.Categorie,
                    Alerte: l.RevisionDepassee(aujourdHui) ? $"Révision dépassée ({Libelles.Date(l.DateProchaineRevision!.Value)})" : null,
                    CouleurAlerte: Theme.Avertissement)),
            enPreparation ? "Retirer" : null,
            Icones.Croix,
            element => Executer(() => RetirerAsync(element)));
    }

    protected override async Task TraiterCodeAsync(string code)
    {
        var resultat = await _scan.ScannerDepartAsync(SortieId, code);
        ZoneScan.AfficherResultat(resultat);

        if (resultat.Action == ActionProposee.CreerEquipement && Dialogues.Confirmer(FindForm(), resultat.Message))
        {
            // Étape suivante : formulaire de création pré-rempli avec resultat.Code.
            Dialogues.Information(FindForm(), $"La création de l'équipement {resultat.Code} arrive dans une prochaine version.");
        }

        await RafraichirAsync();
    }

    private async Task RetirerAsync(ElementMateriel element)
    {
        var resultat = await _scan.RetirerDuDepartAsync(SortieId, element.EquipementId);
        ZoneScan.AfficherMessage(resultat.Reussi ? NiveauResultat.Succes : NiveauResultat.Erreur, resultat.Message, element.Code);
        await RafraichirAsync();
    }

    private async Task ValiderAsync()
    {
        if (_statut != StatutSortie.EnPreparation)
        {
            Navigation.AllerA<EcranRetour>(SortieId);
            return;
        }

        var resultat = await _sorties.ValiderDepartAsync(SortieId);
        if (!resultat.Reussi)
        {
            ZoneScan.AfficherMessage(NiveauResultat.Erreur, resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), $"{resultat.Message}\nBonne plongée ! Au retour, ouvrez la sortie pour scanner le matériel.");
        Navigation.AllerA<EcranSorties>();
    }
}
