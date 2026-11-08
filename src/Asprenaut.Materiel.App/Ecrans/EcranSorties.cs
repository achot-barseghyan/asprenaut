using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Accueil : sorties en cours (cartes) et sorties clôturées (tableau).</summary>
public sealed class EcranSorties : EcranBase
{
    private readonly ServiceSortie _sorties;
    private readonly INavigation _navigation;
    private readonly FlowLayoutPanel _cartes;
    private readonly Label _aucuneEnCours;
    private readonly DataGridView _cloturees;

    public EcranSorties(ServiceSortie sorties, INavigation navigation)
    {
        _sorties = sorties;
        _navigation = navigation;

        var nouvelle = new BoutonArrondi("+  Nouvelle sortie", StyleBouton.Principal);
        nouvelle.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>();

        _aucuneEnCours = Ui.Libelle("Aucune sortie en cours. Cliquez sur « Nouvelle sortie » pour commencer.", Theme.Police(11f), Theme.TexteSecondaire);
        _cartes = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Fond,
            Margin = new Padding(0, 4, 0, 16),
        };
        _cartes.Controls.Add(_aucuneEnCours);
        Natif.ScrollbarsSombres(_cartes);

        _cloturees = new DataGridView();
        Theme.AppliquerGrille(_cloturees);
        _cloturees.Columns.Add("date", "Date");
        _cloturees.Columns.Add("intitule", "Intitulé");
        _cloturees.Columns.Add("type", "Type");
        _cloturees.Columns.Add("moniteur", "Moniteur");
        _cloturees.Columns.Add("rendus", "Rendus");
        _cloturees.Columns.Add("manquants", "Manquants");
        _cloturees.Columns["intitule"]!.FillWeight = 200;
        _cloturees.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _cloturees.Rows[e.RowIndex].Tag is long id)
            {
                _navigation.AllerA<EcranRetour>(id);
            }
        };
        _cloturees.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == _cloturees.Columns["manquants"]!.Index && e.Value is int n && n > 0)
            {
                e.CellStyle!.ForeColor = e.CellStyle.SelectionForeColor = Theme.Erreur;
            }
        };

        var carteCloturees = new Carte { Padding = new Padding(12), Margin = Padding.Empty };
        _cloturees.Dock = DockStyle.Fill;
        carteCloturees.Controls.Add(_cloturees);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Préparez le départ du matériel, puis scannez son retour.")), nouvelle), SizeType.AutoSize, 0),
            (Ui.TitreSection("En cours"), SizeType.AutoSize, 0),
            (_cartes, SizeType.Absolute, 260),
            (Ui.TitreSection("Sorties clôturées"), SizeType.AutoSize, 0),
            (carteCloturees, SizeType.Percent, 100)));
    }

    public override string Titre => "Sorties";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected override async Task ChargerAsync(object? parametre)
    {
        var enCours = await _sorties.ListerEnCoursAsync();
        var cloturees = await _sorties.ListerClotureesAsync();

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 1)
        {
            var c = _cartes.Controls[1];
            _cartes.Controls.RemoveAt(1);
            c.Dispose();
        }

        foreach (var sortie in enCours)
        {
            var carte = new CarteSortie(sortie);
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

        _aucuneEnCours.Visible = enCours.Count == 0;
        _cartes.ResumeLayout(true);

        _cloturees.Rows.Clear();
        foreach (var s in cloturees)
        {
            var i = _cloturees.Rows.Add(Libelles.Date(s.DateSortie), s.Intitule, Libelles.De(s.Type), s.MoniteurNom, $"{s.NbRendus} / {s.NbTotal}", s.NbManquants);
            _cloturees.Rows[i].Tag = s.Id;
        }
    }

    /// <summary>Carte d'une sortie en préparation ou en cours.</summary>
    private sealed class CarteSortie : Carte
    {
        public CarteSortie(SortieResume s)
        {
            Size = new Size(360, 236);
            Padding = new Padding(20, 18, 20, 18);
            Margin = new Padding(0, 0, 16, 0);

            var enPreparation = s.Statut == StatutSortie.EnPreparation;
            var progression = enPreparation
                ? $"En préparation · {Libelles.Pluriel(s.NbTotal, "équipement")} scanné{(s.NbTotal > 1 ? "s" : "")}"
                : $"{s.NbRendus} rendu{(s.NbRendus > 1 ? "s" : "")} sur {s.NbTotal}";

            var barre = new BarreProgression { Valeur = s.Progression, Couleur = Theme.Retour };
            var bouton = enPreparation
                ? new BoutonArrondi("Continuer le départ", StyleBouton.Depart)
                : new BoutonArrondi("Ouvrir et scanner le retour", StyleBouton.Retour);
            bouton.AutoSize = false;
            bouton.Margin = new Padding(0, 8, 0, 0);
            bouton.Click += (_, _) => Ouvrir?.Invoke(this, EventArgs.Empty);

            var titre = Ui.Libelle(s.Intitule, Theme.PoliceTitre(14f));
            titre.AutoEllipsis = true;
            var colonne = Ui.Colonne(
                (Ui.Libelle(Libelles.DateLongue(s.DateSortie), Theme.Police(10f), Theme.TexteSecondaire), SizeType.AutoSize, 0),
                (titre, SizeType.AutoSize, 0),
                (Ui.Libelle($"{s.MoniteurNom} · {Libelles.De(s.Type)}", Theme.Police(10.5f), Theme.TexteSecondaireClair), SizeType.AutoSize, 0),
                (Ui.Libelle(progression, Theme.PoliceTitre(10.5f), enPreparation ? Theme.Depart : Theme.Retour), SizeType.AutoSize, 0),
                (barre, SizeType.Absolute, 22),
                (bouton, SizeType.Percent, 100));
            colonne.BackColor = Theme.Surface;
            Controls.Add(colonne);
        }

        public event EventHandler? Ouvrir;
    }
}
