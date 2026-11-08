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
    private readonly AnneauProgression _anneau = new(104);
    private readonly BoutonArrondi _cloturer = new("Clôturer la sortie", StyleBouton.Secondaire);
    private readonly Label _conseil = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaireClair);
    private readonly BoutonArrondi _ajouter = new("+ Ajouter du matériel à cette sortie", StyleBouton.Lien);
    private readonly Pastille _compteARendre = Compteur(Theme.Depart, Theme.DepartClair);
    private readonly Pastille _compteRendu = Compteur(Theme.Retour, Theme.Retour);
    private readonly ListeMateriel _aRendre = new(StyleListe.ARendre, "Tout le matériel est rentré", Icones.CercleCoche, Theme.Retour);
    private readonly ListeMateriel _rendu = new(StyleListe.Rendu, "Rien n'est encore rendu.");
    private readonly FiltreCategories _filtre = new(Theme.Depart, "Type") { Margin = new Padding(0, 0, 0, 12) };
    private IReadOnlyList<LigneSortie> _lignes = [];

    public EcranRetour(ServiceSortie sorties, ServiceScan scan, ServiceReglages reglages, INavigation navigation)
        : base(navigation, reglages, ModeScan.Retour)
    {
        _sorties = sorties;
        _scan = scan;
        _cloturer.Click += (_, _) => Executer(CloturerAsync);
        _ajouter.Click += (_, _) => Navigation.AllerA<EcranDepart>(SortieId);

        // Carte de progression : anneau, bouton de clôture, conseil, ajout de matériel.
        _cloturer.AutoSize = false;
        _cloturer.Margin = Padding.Empty;
        _conseil.AutoSize = false;
        _ajouter.Margin = Padding.Empty;
        _ajouter.Height = Theme.HauteurCliquable;
        _ajouter.Font = Theme.PoliceTitre(10f);
        var actions = Ui.Colonne(
            (_cloturer, SizeType.Absolute, 48),
            (_conseil, SizeType.Percent, 100),
            (_ajouter, SizeType.Absolute, 44));
        _conseil.Margin = new Padding(0, 8, 0, 0);
        _ajouter.Dock = DockStyle.None;
        _ajouter.Anchor = AnchorStyles.Left;
        var carteProgression = new Carte { Padding = new Padding(20, 18, 20, 14), Margin = new Padding(0, 16, 0, 0) };
        var ligne = Ui.Colonnes((_anneau, SizeType.Absolute, 104 + 20), (actions, SizeType.Percent, 100));
        _anneau.Dock = DockStyle.None;
        _anneau.Anchor = AnchorStyles.Left;
        carteProgression.Controls.Add(ligne);

        // Carte étroite (petit écran) : l'anneau laisse la place au bouton et au conseil.
        carteProgression.Resize += (_, _) =>
        {
            var large = carteProgression.Width >= Dessin.Echelle(carteProgression, 360);
            _anneau.Visible = large;
            ligne.ColumnStyles[0].Width = large ? Dessin.Echelle(carteProgression, 124) : 0;
        };

        var gauche = Ui.Colonne(
            (ZoneScan, SizeType.Percent, 100),
            (ZoneScan.Bandeau, SizeType.Absolute, 76 + 16),
            (carteProgression, SizeType.Absolute, 190 + 16));
        ZoneScan.Bandeau.Margin = new Padding(0, 16, 0, 0);
        gauche.Margin = new Padding(0, 0, 22, 0);

        // Écran peu haut : la zone de scan garde au moins la place de sa consigne et de son champ ;
        // le bandeau et la carte de progression se resserrent.
        gauche.Resize += (_, _) =>
        {
            var petit = gauche.Height < Dessin.Echelle(gauche, 560);
            gauche.RowStyles[1].Height = Dessin.Echelle(gauche, (petit ? 64 : 76) + 16);
            gauche.RowStyles[2].Height = Dessin.Echelle(gauche, (petit ? 150 : 190) + 16);
            _conseil.Visible = !petit;
        };

        var carteARendre = new Carte { CouleurBordure = Theme.Teinte(Theme.Depart, 0.22f), Padding = new Padding(20), Margin = new Padding(0, 0, 8, 0) };
        carteARendre.Controls.Add(Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("À rendre"), _compteARendre), SizeType.Absolute, HauteurEnteteListe),
            (_aRendre, SizeType.Percent, 100)));
        var carteRendu = new Carte { CouleurBordure = Theme.Teinte(Theme.Retour, 0.22f), Padding = new Padding(20), Margin = new Padding(8, 0, 0, 0) };
        carteRendu.Controls.Add(Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("Rendu"), _compteRendu), SizeType.Absolute, HauteurEnteteListe),
            (_rendu, SizeType.Percent, 100)));
        var cartes = Ui.Colonnes((carteARendre, SizeType.Percent, 50), (carteRendu, SizeType.Percent, 50));
        _filtre.Changement += (_, _) => AfficherListes();
        var droite = Ui.Colonne((_filtre, SizeType.Absolute, 44 + 12), (cartes, SizeType.Percent, 100));

        Composer(Ui.Colonnes((gauche, SizeType.Percent, 40), (droite, SizeType.Percent, 60)));
    }

    public override string Titre => "Retour du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        AfficherSortie(resume, resume.DepartValideLe is { } parti ? $"parti à {Heure(parti)}" : null);

        var cloturee = resume.Statut == StatutSortie.Cloturee;
        var aRendre = lignes.Where(l => l.Statut != StatutSortieEquipement.Rendu).ToList();
        var rendus = lignes.Where(l => l.Statut == StatutSortieEquipement.Rendu).OrderByDescending(l => l.ScanneRetourLe).ToList();

        _anneau.Valeur = resume.Progression;
        _anneau.Nombre = resume.NbRendus.ToString(Libelles.Culture);
        _anneau.Total = $"/{resume.NbTotal}";
        _anneau.Legende = resume.NbRendus > 1 ? "rendus" : "rendu";

        var toutRendu = aRendre.Count == 0;
        _cloturer.Visible = !cloturee;
        _cloturer.Style = toutRendu ? StyleBouton.Retour : StyleBouton.Secondaire;
        _ajouter.Visible = resume.Statut == StatutSortie.EnCours;
        _conseil.Text = cloturee
            ? resume.NbManquants > 0
                ? $"Sortie clôturée avec {Libelles.Pluriel(resume.NbManquants, "manquant")}. Scannez un manquant s'il revient."
                : "Sortie clôturée : tout a été rendu."
            : toutRendu
                ? "Tout est rentré. Vous pouvez clôturer la sortie."
                : $"{Libelles.Pluriel(resume.NbDehors, "équipement")} {(resume.NbDehors > 1 ? "seront notés manquants" : "sera noté manquant")} si vous clôturez maintenant.";

        // Puces « Type » : toutes les catégories de la sortie, comptées sur le matériel encore à rendre.
        _lignes = lignes;
        _filtre.Afficher(aRendre.Count, EcranDepart.Categories(lignes, l => l.Statut != StatutSortieEquipement.Rendu));
        AfficherListes();
    }

    private void AfficherListes()
    {
        var aRendre = _lignes.Where(l => l.Statut != StatutSortieEquipement.Rendu && _filtre.Accepte(l.Categorie)).ToList();
        var rendus = _lignes.Where(l => l.Statut == StatutSortieEquipement.Rendu && _filtre.Accepte(l.Categorie))
            .OrderByDescending(l => l.ScanneRetourLe).ToList();
        _compteARendre.Text = aRendre.Count.ToString(Libelles.Culture);
        _compteRendu.Text = rendus.Count.ToString(Libelles.Culture);

        if (_filtre.Selection is null)
        {
            _aRendre.DefinirVide("Tout le matériel est rentré", Icones.CercleCoche, Theme.Retour);
            _rendu.DefinirVide("Rien n'est encore rendu.");
        }
        else
        {
            _aRendre.DefinirVide("Rien à rendre dans cette catégorie", Icones.CercleCoche, Theme.Retour);
            _rendu.DefinirVide("Rien de rendu dans cette catégorie.");
        }

        _aRendre.Afficher(aRendre.Select(l => new ElementMateriel(
            l.EquipementId,
            l.Code,
            l.Libelle,
            l.Categorie,
            Alerte: l.Statut == StatutSortieEquipement.Manquant ? "Manquant" : null,
            CouleurAlerte: Theme.Erreur)));
        _rendu.Afficher(rendus.Select(l => new ElementMateriel(
            l.EquipementId,
            l.Code,
            l.Libelle,
            l.Categorie,
            l.ScanneRetourLe is { } r ? Heure(r) : null)));
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
        if (apercu.ConfirmationNecessaire && !Dialogues.Confirmer(FindForm(), apercu.MessageConfirmation))
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
