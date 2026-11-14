using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Accueil : bandeau, sorties en cours (cartes) et sorties clôturées (tableau).</summary>
public sealed class EcranSorties : EcranBase
{
    private readonly ServiceSortie _sorties;
    private readonly IHorloge _horloge;
    private readonly INavigation _navigation;
    private readonly ServiceReglages _reglages;

    // Rappel de la sauvegarde complète hebdomadaire (stockage externe), visible seulement quand elle est à faire.
    private readonly BandeauMessage _rappelSauvegarde = new() { Height = 76, Margin = Padding.Empty };
    private readonly TableLayoutPanel _ligneRappel;
    private readonly Label _date = Ui.Libelle("", Theme.PoliceTitre(9.75f), Theme.AccentClair);
    private readonly Label _resume = Ui.Libelle("", Theme.Police(11.25f), Theme.TexteDoux);
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 400, HauteurElement = 196, Ecart = 18, RangeesVisibles = 1, Margin = new Padding(0, 0, 0, 30) };
    private readonly DataGridView _cloturees = new();

    // Pagination des sorties clôturées : seule la page affichée est lue dans la base.
    private readonly Label _rangPage = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);
    private readonly BoutonArrondi _precedente = new("‹ Précédentes", StyleBouton.Secondaire) { AccessibleName = "Sorties clôturées précédentes (plus récentes)" };
    private readonly BoutonArrondi _suivante = new("Suivantes ›", StyleBouton.Secondaire) { AccessibleName = "Sorties clôturées suivantes (plus anciennes)" };
    private PageSorties? _pageCloturees;

    public EcranSorties(ServiceSortie sorties, IHorloge horloge, INavigation navigation, ServiceReglages reglages)
    {
        _sorties = sorties;
        _reglages = reglages;
        _horloge = horloge;
        _navigation = navigation;

        var faire = new BoutonArrondi("Faire la sauvegarde ›", StyleBouton.Secondaire, Icones.Telecharger) { Margin = new Padding(14, 0, 0, 0) };
        faire.AccessibleDescription = "Ouvre Paramètres, sur les sauvegardes";
        faire.Click += (_, _) => _navigation.AllerA<EcranParametres>(EcranParametres.VersSauvegardes);
        _ligneRappel = Ui.Colonnes((_rappelSauvegarde, SizeType.Percent, 100), (faire, SizeType.AutoSize, 0));
        _ligneRappel.Height = 76;
        _ligneRappel.Margin = new Padding(0, 0, 0, 24);
        faire.Dock = DockStyle.None;
        faire.Anchor = AnchorStyles.Right;
        _ligneRappel.Visible = false;

        Theme.AppliquerGrille(_cloturees);
        Tableaux.AjouterColonne(_cloturees, "date", "Date", 70);
        Tableaux.AjouterColonne(_cloturees, "intitule", "Sortie", 170);
        Tableaux.AjouterColonne(_cloturees, "moniteur", "Responsable", 110);
        Tableaux.AjouterColonne(_cloturees, "materiel", "Matériel", 90);
        Tableaux.AjouterColonne(_cloturees, "bilan", "Bilan", 90);
        _cloturees.AccessibleName = "Sorties clôturées";
        // Un clic (ou Entrée) sur une sortie clôturée l'ouvre : on y voit ses manquants, et en mode gestion on peut les rendre.
        _cloturees.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _cloturees.Rows[e.RowIndex].Tag is SortieResume s)
            {
                _navigation.AllerA<EcranRetour>(s.Id);
            }
        };
        _cloturees.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _cloturees.CurrentRow?.Tag is SortieResume s)
            {
                e.SuppressKeyPress = true;
                _navigation.AllerA<EcranRetour>(s.Id);
            }
        };
        _cloturees.CellMouseEnter += (_, e) => _cloturees.Cursor = e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default;
        _cloturees.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == _cloturees.Columns["intitule"]!.Index && e.CellStyle is not null)
            {
                e.CellStyle.Font = Theme.PoliceTitre(10.5f);
                e.CellStyle.ForeColor = Theme.Texte;
            }
        };
        _cloturees.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _cloturees.Columns["bilan"]!.Index && _cloturees.Rows[e.RowIndex].Tag is SortieResume s)
            {
                if (s.NbManquants == 0)
                {
                    Tableaux.PeindrePastille(e, "Tout rendu", Theme.Retour);
                }
                else
                {
                    Tableaux.PeindrePastille(e, Libelles.Pluriel(s.NbManquants, "manquant"), Theme.Depart);
                }
            }
        };

        var carteCloturees = new Carte { Padding = new Padding(8, 6, 8, 8), Rayon = 22 };
        _cloturees.Dock = DockStyle.Fill;

        // Sous le tableau : « Sorties 1 à 12 sur 143 », puis précédentes / suivantes.
        _rangPage.AutoSize = false;
        _rangPage.Dock = DockStyle.Fill;
        _rangPage.TextAlign = ContentAlignment.MiddleLeft;
        _rangPage.Margin = new Padding(12, 0, 0, 0);
        _precedente.Margin = new Padding(0, 0, 8, 0);
        _suivante.Margin = new Padding(0, 0, 8, 0);
        _precedente.Click += (_, _) => Executer(() => AfficherClotureesAsync((_pageCloturees?.Page ?? 1) - 1));
        _suivante.Click += (_, _) => Executer(() => AfficherClotureesAsync((_pageCloturees?.Page ?? 1) + 1));
        var pagination = Ui.Colonnes((_rangPage, SizeType.Percent, 100), (_precedente, SizeType.AutoSize, 0), (_suivante, SizeType.AutoSize, 0));
        pagination.Dock = DockStyle.Bottom;
        pagination.Height = Theme.HauteurBouton + 12;
        pagination.Padding = new Padding(0, 6, 0, 0);
        carteCloturees.Controls.Add(pagination);
        carteCloturees.Controls.Add(_cloturees);
        _cloturees.BringToFront(); // Le tableau prend la place laissée par la barre du bas.

        var titreEnCours = Ui.TitreSection("Sorties en cours");
        var precision = Ui.Libelle("matériel encore dehors", Theme.Police(10.5f), Theme.TexteSecondaire);
        precision.Margin = new Padding(10, 7, 0, 0);

        var colonne = Ui.Colonne(
            (CreerBandeau(), SizeType.Absolute, 230 + 30),
            (_ligneRappel, SizeType.AutoSize, 0),
            (Ui.Rangee(true, titreEnCours, precision), SizeType.AutoSize, 0),
            (_cartes, SizeType.Absolute, 196 + 30),
            (Ui.TitreSection("Sorties clôturées"), SizeType.AutoSize, 0),
            (carteCloturees, SizeType.Percent, 100));
        Controls.Add(colonne);

        // Écran peu haut : bandeau plus bas, pour garder de la place au tableau des sorties clôturées.
        colonne.Resize += (_, _) =>
        {
            var hauteur = colonne.Height < Dessin.Echelle(colonne, 760) ? 196 : 230;
            var voulue = Dessin.Echelle(colonne, hauteur + 30);
            if (Math.Abs(colonne.RowStyles[0].Height - voulue) > 0.5f)
            {
                colonne.RowStyles[0].Height = voulue;
            }
        };
    }

    public override string Titre => "Sorties";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected override async Task ChargerAsync(object? parametre)
    {
        var enCours = await _sorties.ListerEnCoursAsync();
        if (IsDisposed)
        {
            return;
        }

        _date.Text = _horloge.AujourdHui.ToString("dddd d MMMM", Libelles.Culture).ToUpper(Libelles.Culture);

        // Sauvegarde complète de la semaine : rappel tant qu'elle n'est pas faite.
        var sauvegarde = _reglages.EtatSauvegardeComplete(_horloge.MaintenantUtc.ToLocalTime());
        _ligneRappel.Visible = sauvegarde.AFaire;
        if (sauvegarde.AFaire)
        {
            _rappelSauvegarde.Afficher(NiveauResultat.Avertissement, sauvegarde.Message, RappelSauvegarde.Conseil, detailEnCode: false);
        }
        var dehors = enCours.Sum(s => s.NbDehors);
        _resume.Text = enCours.Count == 0
            ? "Aucune sortie en cours : tout le matériel est rangé."
            : $"{Libelles.Pluriel(enCours.Count, "sortie")} en cours · {Libelles.Pluriel(dehors, "équipement")} encore dehors";

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 0)
        {
            var c = _cartes.Controls[0];
            _cartes.Controls.RemoveAt(0);
            c.Dispose();
        }

        foreach (var sortie in enCours)
        {
            var carte = new CarteSortie(sortie, _sorties.EstEnRetard(sortie));
            carte.Ouvrir += (_, _) =>
            {
                if (sortie.Statut == StatutSortie.EnPreparation)
                {
                    _navigation.AllerA<EcranDepart>(sortie.Id);
                }
                else
                {
                    _navigation.AllerA<EcranRetour>(sortie.Id);
                }
            };
            _cartes.Controls.Add(carte);
        }

        if (enCours.Count == 0)
        {
            _cartes.Controls.Add(new CarteAucune());
        }

        _cartes.ResumeLayout(true);

        await AfficherClotureesAsync(_pageCloturees?.Page ?? 1);
    }

    /// <summary>Affiche une page des sorties clôturées (12 par page, la plus récemment clôturée d'abord).</summary>
    private async Task AfficherClotureesAsync(int page)
    {
        var resultat = await _sorties.ListerClotureesAsync(page);
        if (IsDisposed)
        {
            return;
        }

        _pageCloturees = resultat;
        _cloturees.SuspendLayout();
        _cloturees.Rows.Clear();
        foreach (var s in resultat.Sorties)
        {
            var i = _cloturees.Rows.Add(Libelles.Date(s.DateSortie), s.Intitule, s.MoniteurNom, Libelles.Pluriel(s.NbTotal, "équipement"), "");
            _cloturees.Rows[i].Tag = s;
        }

        _cloturees.ResumeLayout();
        _rangPage.Text = resultat.Total == 0
            ? "Aucune sortie clôturée pour l'instant."
            : resultat.NbPages == 1
                ? Libelles.Pluriel(resultat.Total, "sortie clôturée", "sorties clôturées")
                : $"Sorties {resultat.Premiere} à {resultat.Derniere} sur {resultat.Total} · page {resultat.Page} / {resultat.NbPages}";
        _precedente.Visible = _suivante.Visible = resultat.NbPages > 1;
        _precedente.Enabled = resultat.Page > 1;
        _suivante.Enabled = resultat.Page < resultat.NbPages;
    }

    private BandeauAccueil CreerBandeau()
    {
        var bandeau = new BandeauAccueil { Margin = new Padding(0, 0, 0, 30) };
        var titre = Ui.Libelle("Bonne plongée, l'équipe !", Theme.PoliceGrasse(25.5f));
        _date.Margin = new Padding(0, 0, 0, 10);
        titre.Margin = new Padding(0, 0, 0, 8);
        _resume.Margin = new Padding(2, 0, 0, 18);

        var nouvelle = new BoutonArrondi("Nouvelle sortie", StyleBouton.Principal, Icones.Plus) { Margin = Padding.Empty };
        nouvelle.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>();
        var inventaire = new BoutonArrondi("Voir l'inventaire", StyleBouton.Verre) { Margin = new Padding(12, 0, 0, 0) };
        inventaire.Click += (_, _) => _navigation.AllerA<EcranInventaire>();

        var contenu = Ui.Pile(_date, titre, _resume, Ui.Rangee(true, nouvelle, inventaire));
        var logo = new LogoClub();
        bandeau.Controls.Add(logo);
        bandeau.Controls.Add(contenu);

        // Logo du club à gauche (150 px, moins si le bandeau est réduit), textes et boutons à sa droite, centrés en hauteur.
        bandeau.Layout += (_, _) =>
        {
            var marge = (int)Dessin.Echelle(bandeau, 23);
            var cote = Math.Max(0, Math.Min((int)Dessin.Echelle(bandeau, 150), bandeau.Height - (2 * marge)));
            logo.Bounds = new Rectangle(bandeau.Padding.Left, (bandeau.Height - cote) / 2, cote, cote);
            contenu.Location = new Point(logo.Right + (int)Dessin.Echelle(bandeau, 30), Math.Max(bandeau.Padding.Top / 2, (bandeau.Height - contenu.Height) / 2));
        };
        return bandeau;
    }

    /// <summary>Carte d'une sortie en préparation ou en cours.</summary>
    private sealed class CarteSortie : Carte
    {
        public CarteSortie(SortieResume s, bool enRetard)
        {
            Padding = new Padding(24, 22, 24, 22);
            AccessibleName = enRetard ? $"{s.Intitule}, en retard" : s.Intitule;
            if (enRetard)
            {
                // Réglage « Alerte si non rendu après N jours » : la carte passe en orange.
                CouleurBordure = Theme.Depart;
                BackColor = Theme.Teinte(Theme.Depart, 0.06f);
            }

            var enPreparation = s.Statut == StatutSortie.EnPreparation;
            var date = new PastilleDate { Date = s.DateSortie };
            var type = new Pastille(s.LibelleType.ToUpper(Libelles.Culture), Theme.Teinte(Theme.Accent, 0.12f), Theme.AccentClair, police: Theme.PoliceTitre(7.5f), hauteur: 24)
            {
                Margin = new Padding(0, 0, 0, 6),
            };
            var titre = Ui.Libelle(s.Intitule, Theme.PoliceTitre(15f));
            titre.AutoSize = false;
            titre.AutoEllipsis = true;
            titre.Height = 32;
            var moniteur = Ui.Libelle(
                string.IsNullOrWhiteSpace(s.Lieu) ? s.LibelleResponsable : $"{s.LibelleResponsable} · {s.Lieu}",
                Theme.Police(9.75f),
                Theme.TexteSecondaireClair);
            moniteur.AutoSize = false;
            moniteur.AutoEllipsis = true;
            moniteur.Height = 22;

            var infos = Ui.Colonne((type, SizeType.AutoSize, 0), (titre, SizeType.AutoSize, 0), (moniteur, SizeType.AutoSize, 0));
            type.Dock = DockStyle.None;
            type.Anchor = AnchorStyles.Left;
            infos.Margin = new Padding(18, 0, 12, 0);

            var anneau = new AnneauProgression(84);
            if (enPreparation)
            {
                anneau.Couleur = Theme.Depart;
                anneau.Valeur = s.NbTotal > 0 ? 1 : 0;
                anneau.Nombre = s.NbTotal.ToString(Libelles.Culture);
                anneau.Legende = s.NbTotal > 1 ? "scannés" : "scanné";
            }
            else
            {
                anneau.Valeur = s.Progression;
                anneau.Nombre = s.NbRendus.ToString(Libelles.Culture);
                anneau.Total = $"/{s.NbTotal}";
                anneau.Legende = s.NbRendus > 1 ? "rendus" : "rendu";
            }

            var haut = Ui.Colonnes((date, SizeType.Absolute, 62), (infos, SizeType.Percent, 100), (anneau, SizeType.Absolute, 84));
            date.Dock = anneau.Dock = DockStyle.None;
            date.Anchor = anneau.Anchor = AnchorStyles.None;

            var etat = enPreparation
                ? new Pastille("En préparation", Theme.Teinte(Theme.Depart, 0.10f), Theme.Depart, Theme.Depart, Theme.PoliceTitre(9.75f), 38)
                : new Pastille(
                    enRetard ? $"En retard · {s.NbTotal - s.NbRendus} à rendre" : $"{s.NbTotal - s.NbRendus} à rendre",
                    Theme.Teinte(Theme.Depart, enRetard ? 0.22f : 0.10f),
                    enRetard ? Theme.DepartClair : Theme.Depart,
                    Theme.Depart,
                    Theme.PoliceTitre(9.75f),
                    38);
            var bouton = enPreparation
                ? new BoutonArrondi("Continuer le départ", StyleBouton.Depart, Icones.Fleche, iconeApres: true)
                : new BoutonArrondi("Scanner le retour", StyleBouton.Retour, Icones.Fleche, iconeApres: true);
            bouton.AutoSize = false;
            bouton.Margin = new Padding(12, 0, 0, 0);
            bouton.Font = Theme.PoliceTitre(10.5f);
            bouton.Click += (_, _) => Ouvrir?.Invoke(this, EventArgs.Empty);

            var bas = Ui.Colonnes((etat, SizeType.AutoSize, 0), (bouton, SizeType.Percent, 100));
            etat.Dock = DockStyle.None;
            etat.Anchor = AnchorStyles.Left;

            var colonne = Ui.Colonne((haut, SizeType.Absolute, 86), (bas, SizeType.Percent, 100));
            bas.Margin = new Padding(0, 18, 0, 0);
            Controls.Add(colonne);
        }

        public event EventHandler? Ouvrir;
    }

    /// <summary>Carte affichée quand aucune sortie n'est en cours.</summary>
    private sealed class CarteAucune : Carte
    {
        public CarteAucune()
        {
            Padding = new Padding(28);
            var texte = Ui.Libelle("Aucune sortie en cours.\nCliquez sur « Nouvelle sortie » pour préparer le départ du matériel.", Theme.Police(11f), Theme.TexteSecondaireClair);
            texte.AutoSize = false;
            texte.Dock = DockStyle.Fill;
            texte.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(texte);
        }
    }
}
