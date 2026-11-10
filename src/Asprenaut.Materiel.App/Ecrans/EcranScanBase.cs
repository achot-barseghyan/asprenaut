using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Base commune aux écrans Départ et Retour : fil d'Ariane, carte d'en-tête de la sortie (date, intitulé,
/// moniteur, étapes), zone de scan, et traitement des scans un par un (jamais deux en parallèle).
/// </summary>
public abstract class EcranScanBase : EcranBase
{
    private readonly SemaphoreSlim _unParUn = new(1, 1);
    private readonly FilAriane _fil;
    private readonly PastilleDate _date = new();
    private readonly Label _titreSortie = Ui.Libelle("", Theme.PoliceGrasse(21f));
    private readonly Label _infosSortie = Ui.Libelle("", Theme.Police(10.5f), Theme.TexteSecondaireClair);

    protected EcranScanBase(INavigation navigation, ServiceReglages reglages, ModeScan mode)
    {
        Navigation = navigation;
        _fil = new FilAriane("Sorties", () => Navigation.AllerA<EcranSorties>());
        Etapes = new IndicateurEtapes { EtapeActive = mode == ModeScan.Depart ? 1 : 2 };
        ZoneScan = new ScanInput { Mode = mode };
        ZoneScan.Configurer(reglages.Actuels);
        ZoneScan.CodeScanne += (_, e) => Executer(() => TraiterUnParUnAsync(e.Code));
    }

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected INavigation Navigation { get; }

    protected long SortieId { get; private set; }

    protected IndicateurEtapes Etapes { get; }

    protected ScanInput ZoneScan { get; }

    /// <summary>Met en page : fil d'Ariane (avec des actions à droite), carte d'en-tête, puis le corps propre à l'écran.</summary>
    protected void Composer(Control corps, params Control[] actions)
    {
        _titreSortie.AutoEllipsis = true;
        _titreSortie.Margin = new Padding(0, 0, 0, 6);
        _infosSortie.Margin = Padding.Empty;
        var textes = Ui.Pile(_titreSortie, _infosSortie);
        textes.Margin = new Padding(18, 0, 12, 0);

        var entete = new Carte { Padding = new Padding(26, 20, 26, 20), Margin = new Padding(0, 0, 0, 22) };
        var ligne = Ui.Colonnes((_date, SizeType.Absolute, 62), (textes, SizeType.Percent, 100), (Etapes, SizeType.Absolute, 320));
        _date.Dock = textes.Dock = Etapes.Dock = DockStyle.None;
        _date.Anchor = AnchorStyles.Left;
        textes.Anchor = AnchorStyles.Left;
        Etapes.Anchor = AnchorStyles.Right;
        entete.Controls.Add(ligne);

        Control haut = _fil;
        if (actions.Length > 0)
        {
            var ligneHaut = Ui.Entete(_fil, actions);
            ligneHaut.Margin = new Padding(0, 0, 0, 12);
            haut = ligneHaut;
        }

        Controls.Add(Ui.Colonne(
            (haut, SizeType.AutoSize, 0),
            (entete, SizeType.Absolute, 112 + 22),
            (corps, SizeType.Percent, 100)));
    }

    protected sealed override async Task ChargerAsync(object? parametre)
    {
        SortieId = parametre as long? ?? throw new ArgumentException("Identifiant de sortie attendu.", nameof(parametre));
        await RafraichirAsync();
        ZoneScan.PrendreFocus();
    }

    /// <summary>Recharge les données affichées.</summary>
    protected abstract Task RafraichirAsync();

    /// <summary>Traite un code scanné (appelé un code à la fois).</summary>
    protected abstract Task TraiterCodeAsync(string code);

    protected void AfficherSortie(SortieResume s, string? complement = null)
    {
        _fil.Courant = $"{s.DateSortie.ToString("d MMMM", Libelles.Culture)} · {s.Intitule}";
        _date.Date = s.DateSortie;
        _titreSortie.Text = s.Intitule;
        var infos = $"{s.LibelleType} · {Libelles.Pluriel(s.Utilisations, "utilisation")} · {s.LibelleResponsable}";
        if (!string.IsNullOrWhiteSpace(s.Lieu))
        {
            infos += $" · {s.Lieu}";
        }

        _infosSortie.Text = complement is null ? infos : $"{infos} · {complement}";
    }

    /// <summary>Hauteur de la ligne d'en-tête d'une carte de liste (marge comprise).</summary>
    protected const int HauteurEnteteListe = 36 + 14;

    /// <summary>En-tête d'une carte de liste : titre à gauche, compteur à droite.</summary>
    protected static TableLayoutPanel EnteteListe(Label titre, Pastille compteur)
    {
        var entete = Ui.Colonnes((titre, SizeType.Percent, 100), (compteur, SizeType.AutoSize, 0));
        titre.Dock = compteur.Dock = DockStyle.None;
        titre.Anchor = AnchorStyles.Left;
        compteur.Anchor = AnchorStyles.Right;
        entete.Margin = new Padding(0, 0, 0, 14);
        return entete;
    }

    /// <summary>Compteur rond d'une liste ("12").</summary>
    protected static Pastille Compteur(Color couleur, Color couleurTexte) =>
        new("0", Theme.Teinte(couleur, 0.16f), couleurTexte, police: Theme.PoliceGrasse(11.25f), hauteur: 32, largeurMin: 36);

    protected static string Heure(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", Libelles.Culture);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _unParUn.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task TraiterUnParUnAsync(string code)
    {
        await _unParUn.WaitAsync();
        try
        {
            if (!IsDisposed)
            {
                await TraiterCodeAsync(code);
            }
        }
        finally
        {
            _unParUn.Release();
        }
    }
}
