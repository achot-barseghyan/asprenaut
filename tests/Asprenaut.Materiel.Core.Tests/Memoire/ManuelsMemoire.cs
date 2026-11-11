using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Manuels en mémoire ; équipements liés lus dans la base mémoire (même catégorie, même marque, hors réformés).</summary>
public sealed class ManuelRepositoryMemoire(BaseMemoire db) : IManuelRepository
{
    public List<Manuel> Manuels { get; } = [];

    private IEnumerable<Equipement> Lies(Manuel m) => db.Equipements.Where(e =>
        e.CategorieId == m.CategorieId && e.Etat != EtatEquipement.Reforme && m.Marque.Length > 0
        && Marque(e.Marque) == Marque(m.Marque));

    public Task<IReadOnlyDictionary<long, long>> ListerManuelsDesEquipementsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, long>>(db.Equipements
            .Select(e => (e.Id, Manuel: Manuels.OrderBy(m => m.Id).FirstOrDefault(m =>
                m.CategorieId == e.CategorieId && m.Marque.Length > 0 && Marque(e.Marque) == Marque(m.Marque))))
            .Where(p => p.Manuel is not null)
            .ToDictionary(p => p.Id, p => p.Manuel!.Id));

    private static string Marque(string marque) =>
        new(marque.ToLowerInvariant().Where(c => c is not (' ' or '-' or '.')).ToArray());

    private Manuel Complet(Manuel m)
    {
        m.Categorie = db.Categories.FirstOrDefault(c => c.Id == m.CategorieId)?.Nom;
        m.NbEquipements = Lies(m).Count();
        return m;
    }

    public Task<IReadOnlyList<Manuel>> ListerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Manuel>>(Manuels.Select(Complet).OrderBy(m => m.Marque).ThenBy(m => m.Titre).ToList());

    public Task<Manuel?> ObtenirAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Manuels.FirstOrDefault(m => m.Id == id) is { } m ? Complet(m) : null);

    public Task<long> AjouterAsync(Manuel manuel, CancellationToken ct = default)
    {
        manuel.Id = db.ProchainId();
        Manuels.Add(manuel);
        return Task.FromResult(manuel.Id);
    }

    public Task MettreAJourAsync(Manuel manuel, CancellationToken ct = default) => Task.CompletedTask;

    public Task SupprimerAsync(long id, CancellationToken ct = default)
    {
        Manuels.RemoveAll(m => m.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListerCodesEquipementsAsync(long manuelId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Manuels.FirstOrDefault(m => m.Id == manuelId) is { } m
            ? Lies(m).Select(e => e.Code).Order().ToList()
            : []);
}

/// <summary>Dossier des manuels simulé : noms des fichiers « copiés ».</summary>
public sealed class StockageManuelsMemoire : IStockageManuels
{
    public HashSet<string> Fichiers { get; } = [];

    public string Importer(string cheminSource)
    {
        var fichier = Path.GetFileName(cheminSource);
        Fichiers.Add(fichier);
        return fichier;
    }

    public string Chemin(string fichier) => Path.Combine(@"C:\Manuels", fichier);

    public bool Existe(string fichier) => Fichiers.Contains(fichier);

    public void Supprimer(string fichier) => Fichiers.Remove(fichier);
}
