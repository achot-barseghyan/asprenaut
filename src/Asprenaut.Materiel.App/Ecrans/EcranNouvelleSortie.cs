using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Formulaire de création d'une sortie.</summary>
public sealed class EcranNouvelleSortie : EcranBase
{
    private readonly ServiceSortie _sorties;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly INavigation _navigation;
    private readonly ListeDeroulante _type = new();
    private readonly DateTimePicker _date = new();
    private readonly ListeDeroulante _moniteur = new();
    private readonly ChampSaisie _intitule = new() { TexteIndicatif = "Ex. : Cours en piscine" };
    private readonly ChampSaisie _lieu = new() { TexteIndicatif = "Ex. : Piscine municipale" };
    private readonly ChampSaisie _notes = new(multiligne: true);
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10.5f), Theme.Erreur);
    private readonly BoutonArrondi _creer = new("Créer et scanner le matériel  →", StyleBouton.Principal);

    public EcranNouvelleSortie(ServiceSortie sorties, ServiceMoniteurs moniteurs, INavigation navigation)
    {
        _sorties = sorties;
        _moniteurs = moniteurs;
        _navigation = navigation;

        foreach (var type in Enum.GetValues<TypeSortie>())
        {
            _type.Items.Add(new Choix<TypeSortie>(type, Libelles.De(type)));
        }

        _type.SelectedIndex = 0;
        _date.Format = DateTimePickerFormat.Long;
        _date.Font = Theme.Police(11f);
        _date.Margin = new Padding(0, 0, 0, 12);
        _date.CalendarMonthBackground = Theme.Surface;
        _date.CalendarForeColor = Theme.Texte;

        var formulaire = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = Theme.Surface,
        };
        formulaire.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        formulaire.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Ligne(string libelle, Control champ)
        {
            var l = Ui.Libelle(libelle, Theme.PoliceTitre(10.5f), Theme.TexteSecondaireClair);
            l.Margin = new Padding(0, 12, 12, 0);
            champ.Dock = DockStyle.Fill;
            formulaire.Controls.Add(l);
            formulaire.Controls.Add(champ);
        }

        Ligne("Type de sortie", _type);
        Ligne("Date", _date);
        Ligne("Moniteur responsable", _moniteur);
        Ligne("Intitulé", _intitule);
        Ligne("Lieu", _lieu);
        Ligne("Notes", _notes);

        var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire);
        annuler.Click += (_, _) => _navigation.AllerA<EcranSorties>();
        _creer.Click += (_, _) => Executer(CreerAsync);

        var carte = new Carte { Padding = new Padding(28, 20, 28, 20), Margin = Padding.Empty };
        carte.Controls.Add(formulaire);

        var cadre = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        cadre.Controls.Add(carte);
        cadre.Resize += (_, _) => carte.SetBounds(0, 0, Math.Min(cadre.Width, (int)Dessin.Echelle(cadre, 820)), cadre.Height);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Renseignez la sortie, puis scannez le matériel qui part."))), SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100),
            (_message, SizeType.AutoSize, 0),
            (Ui.Rangee(false, _creer, annuler), SizeType.AutoSize, 0)));
    }

    public override string Titre => "Nouvelle sortie";

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected override async Task ChargerAsync(object? parametre)
    {
        var moniteurs = await _moniteurs.ListerActifsAsync();
        _moniteur.Items.Clear();
        foreach (var m in moniteurs)
        {
            _moniteur.Items.Add(new Choix<long>(m.Id, m.NomComplet));
        }

        if (_moniteur.Items.Count == 0)
        {
            _message.Text = "Aucun moniteur enregistré. Ajoutez-en un dans « Moniteurs et plongeurs ».";
        }

        _intitule.Champ.Focus();
    }

    private async Task CreerAsync()
    {
        _creer.Enabled = false;
        try
        {
            var demande = new NouvelleSortie(
                DateOnly.FromDateTime(_date.Value),
                (_type.SelectedItem as Choix<TypeSortie>)?.Valeur ?? TypeSortie.Autre,
                _intitule.Valeur,
                (_moniteur.SelectedItem as Choix<long>)?.Valeur,
                _lieu.Valeur,
                _notes.Valeur);
            var resultat = await _sorties.CreerAsync(demande);
            if (resultat is { Reussi: true, Id: { } id })
            {
                _navigation.AllerA<EcranDepart>(id);
                return;
            }

            _message.Text = resultat.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                _creer.Enabled = true;
            }
        }
    }
}
