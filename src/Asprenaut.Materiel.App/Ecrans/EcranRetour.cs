using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Étape 2 : scan du matériel qui rentre, puis clôture.</summary>
public sealed class EcranRetour : EcranScanBase
{
    private readonly ServiceSortie _sorties;
    private readonly ServiceScan _scan;
    private readonly Label _progressionTexte = Ui.Libelle("", Theme.PoliceTitre(12f), Theme.Retour);
    private readonly BarreProgression _progression = new() { Couleur = Theme.Retour };
    private readonly Label _titreARendre = Ui.TitreSection("À rendre");
    private readonly Label _titreRendu = Ui.TitreSection("Rendu");
    private readonly ListeMateriel _aRendre = new("Tout est rendu.");
    private readonly ListeMateriel _rendu = new("Rien n'est encore rendu.");
    private readonly BoutonArrondi _cloturer = new("Clôturer la sortie", StyleBouton.Retour);
    private readonly BoutonArrondi _ajouter = new("+  Ajouter du matériel", StyleBouton.Lien);

    public EcranRetour(ServiceSortie sorties, ServiceScan scan, INavigation navigation)
        : base(navigation, ModeScan.Retour)
    {
        _sorties = sorties;
        _scan = scan;
        _cloturer.Click += (_, _) => Executer(CloturerAsync);
        _ajouter.Click += (_, _) => Navigation.AllerA<EcranDepart>(SortieId);

        var colonnes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Fond, Margin = Padding.Empty };
        colonnes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        colonnes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        colonnes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        colonnes.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _aRendre.Dock = DockStyle.Fill;
        _rendu.Dock = DockStyle.Fill;
        _aRendre.Margin = new Padding(0, 0, 12, 0);
        _rendu.Margin = new Padding(12, 0, 0, 0);
        _titreRendu.Margin = new Padding(12, 0, 0, 4);
        colonnes.Controls.Add(_titreARendre, 0, 0);
        colonnes.Controls.Add(_titreRendu, 1, 0);
        colonnes.Controls.Add(_aRendre, 0, 1);
        colonnes.Controls.Add(_rendu, 1, 1);

        var barreBas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, BackColor = Theme.Fond, Margin = new Padding(0, 12, 0, 0) };
        barreBas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barreBas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _ajouter.Margin = Padding.Empty;
        barreBas.Controls.Add(_ajouter, 0, 0);
        barreBas.Controls.Add(_cloturer, 1, 0);

        Controls.Add(Ui.Colonne(
            (CreerEntete(), SizeType.AutoSize, 0),
            (_progressionTexte, SizeType.AutoSize, 0),
            (_progression, SizeType.Absolute, 26),
            (ZoneScan, SizeType.Absolute, 240),
            (colonnes, SizeType.Percent, 100),
            (barreBas, SizeType.AutoSize, 0)));
    }

    public override string Titre => "Retour du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        AfficherSortie(resume);

        var cloturee = resume.Statut == StatutSortie.Cloturee;
        _progressionTexte.Text = $"{resume.NbRendus} rendu{(resume.NbRendus > 1 ? "s" : "")} sur {resume.NbTotal}"
            + (resume.NbManquants > 0 ? $"  ·  {resume.NbManquants} manquant{(resume.NbManquants > 1 ? "s" : "")}" : "")
            + (cloturee ? "  ·  sortie clôturée" : "");
        _progression.Valeur = resume.Progression;
        _cloturer.Visible = !cloturee;
        _ajouter.Visible = resume.Statut == StatutSortie.EnCours;

        var aRendre = lignes.Where(l => l.Statut != StatutSortieEquipement.Rendu).ToList();
        var rendus = lignes.Where(l => l.Statut == StatutSortieEquipement.Rendu).ToList();
        _titreARendre.Text = $"À rendre ({aRendre.Count})";
        _titreRendu.Text = $"Rendu ({rendus.Count})";

        _aRendre.Afficher(aRendre.Select(l => new ElementMateriel(
            l.EquipementId,
            l.Code,
            l.Libelle,
            $"{l.Categorie} · parti à {Heure(l.ScanneDepartLe)}",
            l.Statut == StatutSortieEquipement.Manquant ? "Manquant" : null,
            Theme.Erreur)));
        _rendu.Afficher(rendus.Select(l => new ElementMateriel(
            l.EquipementId,
            l.Code,
            l.Libelle,
            $"{l.Categorie} · rendu à {(l.ScanneRetourLe is { } r ? Heure(r) : "?")}")));
    }

    protected override async Task TraiterCodeAsync(string code)
    {
        var resultat = await _scan.ScannerRetourAsync(SortieId, code);
        ZoneScan.AfficherResultat(resultat);

        if (resultat is { Action: ActionProposee.RentrerDansAutreSortie, AutreSortieId: { } autre, Code: { } c }
            && Dialogues.Confirmer(FindForm(), resultat.Message))
        {
            ZoneScan.AfficherResultat(await _scan.ScannerRetourAsync(autre, c));
        }

        await RafraichirAsync();
    }

    private async Task CloturerAsync()
    {
        var apercu = await _sorties.ApercuClotureAsync(SortieId);
        if (!Dialogues.Confirmer(FindForm(), apercu.MessageConfirmation))
        {
            return;
        }

        var resultat = await _sorties.CloturerAsync(SortieId);
        if (!resultat.Reussi)
        {
            ZoneScan.AfficherMessage(NiveauResultat.Erreur, resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), resultat.Message);
        Navigation.AllerA<EcranSorties>();
    }
}
