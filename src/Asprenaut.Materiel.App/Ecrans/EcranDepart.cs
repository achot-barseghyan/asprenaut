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
    private readonly Label _compte = Ui.TitreSection("Matériel scanné");
    private readonly ListeMateriel _liste = new("Rien n'est encore scanné. Scannez la première étiquette.");
    private readonly BoutonArrondi _valider = new("Valider le départ  →", StyleBouton.Principal);
    private StatutSortie _statut;

    public EcranDepart(ServiceSortie sorties, ServiceScan scan, IHorloge horloge, INavigation navigation)
        : base(navigation, ModeScan.Depart)
    {
        _sorties = sorties;
        _scan = scan;
        _horloge = horloge;
        _valider.Click += (_, _) => Executer(ValiderAsync);

        Controls.Add(Ui.Colonne(
            (CreerEntete(), SizeType.AutoSize, 0),
            (ZoneScan, SizeType.Absolute, 240),
            (_compte, SizeType.AutoSize, 0),
            (_liste, SizeType.Percent, 100),
            (Ui.Rangee(false, _valider), SizeType.AutoSize, 0)));
    }

    public override string Titre => "Départ du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        _statut = resume.Statut;
        AfficherSortie(resume);

        var enPreparation = _statut == StatutSortie.EnPreparation;
        _compte.Text = $"Matériel scanné ({lignes.Count})";
        _valider.Text = enPreparation ? "Valider le départ  →" : "Revenir au retour  →";
        _valider.Visible = _statut != StatutSortie.Cloturee;

        var aujourdHui = _horloge.AujourdHui;
        _liste.Afficher(
            lignes.Select(l => new ElementMateriel(
                l.EquipementId,
                l.Code,
                l.Libelle,
                Details(l),
                l.RevisionDepassee(aujourdHui) ? $"Révision dépassée ({Libelles.Date(l.DateProchaineRevision!.Value)})" : null,
                Theme.Avertissement)),
            enPreparation ? "Retirer" : null,
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

    private static string Details(LigneSortie l) =>
        string.IsNullOrWhiteSpace(l.Taille) ? $"{l.Categorie} · scanné à {Heure(l.ScanneDepartLe)}" : $"{l.Categorie} · taille {l.Taille} · scanné à {Heure(l.ScanneDepartLe)}";

    private async Task RetirerAsync(ElementMateriel element)
    {
        var resultat = await _scan.RetirerDuDepartAsync(SortieId, element.EquipementId);
        ZoneScan.AfficherMessage(resultat.Reussi ? NiveauResultat.Succes : NiveauResultat.Erreur, resultat.Message);
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
