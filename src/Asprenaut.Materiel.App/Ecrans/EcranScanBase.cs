using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Base commune aux écrans Départ et Retour : en-tête de la sortie, indicateur d'étapes,
/// zone de scan, et traitement des scans un par un (jamais deux en parallèle).
/// </summary>
public abstract class EcranScanBase : EcranBase
{
    private readonly SemaphoreSlim _unParUn = new(1, 1);

    protected EcranScanBase(INavigation navigation, ModeScan mode)
    {
        Navigation = navigation;
        Retour = new BoutonArrondi("‹  Sorties", StyleBouton.Lien) { Margin = new Padding(0, 0, 0, 4), Padding = new Padding(8, 0, 12, 0) };
        Retour.Click += (_, _) => Navigation.AllerA<EcranSorties>();
        TitreSortie = Ui.Titre("");
        InfosSortie = Ui.SousTitre("");
        Etapes = new IndicateurEtapes { EtapeActive = mode == ModeScan.Depart ? 1 : 2 };
        ZoneScan = new ScanInput { Mode = mode };
        ZoneScan.CodeScanne += (_, e) => Executer(() => TraiterUnParUnAsync(e.Code));
    }

    public override EntreeMenu Menu => EntreeMenu.Sorties;

    protected INavigation Navigation { get; }

    protected long SortieId { get; private set; }

    protected BoutonArrondi Retour { get; }

    protected Label TitreSortie { get; }

    protected Label InfosSortie { get; }

    protected IndicateurEtapes Etapes { get; }

    protected ScanInput ZoneScan { get; }

    /// <summary>En-tête standard : lien retour, intitulé, infos, étapes.</summary>
    protected Control CreerEntete() => Ui.Entete(Ui.Pile(Retour, TitreSortie, InfosSortie), Etapes);

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

    protected void AfficherSortie(SortieResume s)
    {
        TitreSortie.Text = s.Intitule;
        var infos = $"{Libelles.DateLongue(s.DateSortie)}  ·  {s.MoniteurNom}  ·  {Libelles.De(s.Type)}";
        InfosSortie.Text = string.IsNullOrWhiteSpace(s.Lieu) ? infos : $"{infos}  ·  {s.Lieu}";
    }

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
