using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>
/// Paramètres locaux de l'application, dans %LOCALAPPDATA%\Asprenaut\parametres.json.
/// </summary>
public sealed class ParametresApplication
{
    private static readonly JsonSerializerOptions OptionsJson = new() { WriteIndented = true };

    public static string DossierApplication =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Asprenaut");

    public static string CheminBaseParDefaut => Path.Combine(DossierApplication, "materiel.db");

    public static string FichierParDefaut => Path.Combine(DossierApplication, "parametres.json");

    /// <summary>Chemin de la base SQLite.</summary>
    public string CheminBase { get; set; } = CheminBaseParDefaut;

    /// <summary>Réglages du club (écran Paramètres).</summary>
    public Reglages Reglages { get; set; } = new();

    /// <summary>Fichier d'où viennent ces paramètres, et où ils sont réenregistrés.</summary>
    [JsonIgnore]
    public string Fichier { get; private set; } = FichierParDefaut;

    /// <summary>
    /// Charge les paramètres. Avec <c>--base</c>, le fichier est pris à côté de la base d'essai :
    /// les essais ne modifient jamais les réglages réels du club.
    /// </summary>
    public static ParametresApplication Charger(string? fichier = null)
    {
        fichier ??= FichierParDefaut;
        try
        {
            if (File.Exists(fichier))
            {
                var lus = JsonSerializer.Deserialize<ParametresApplication>(File.ReadAllText(fichier));
                if (lus is not null && !string.IsNullOrWhiteSpace(lus.CheminBase))
                {
                    lus.Fichier = fichier;
                    lus.Reglages ??= new Reglages();
                    return lus;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Erreurs.Journaliser(ex);
        }

        return new ParametresApplication { Fichier = fichier };
    }

    /// <summary>
    /// Vrai après une restauration complète : le fichier contient déjà les réglages restaurés, ceux en mémoire ne doivent
    /// plus l'écraser jusqu'au redémarrage.
    /// </summary>
    [JsonIgnore]
    public bool Remplacee { get; set; }

    public void Enregistrer()
    {
        if (Remplacee)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Fichier) ?? DossierApplication);
        File.WriteAllText(Fichier, JsonSerializer.Serialize(this, OptionsJson));
    }
}

/// <summary>Enregistre les réglages dans parametres.json (avec le reste des paramètres).</summary>
public sealed class StockageReglagesJson(ParametresApplication parametres) : IStockageReglages
{
    public void Enregistrer(Reglages reglages)
    {
        parametres.Reglages = reglages;
        try
        {
            parametres.Enregistrer();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le réglage reste actif jusqu'à la fermeture ; on note seulement l'échec.
            Erreurs.Journaliser(ex);
        }
    }
}

/// <summary>
/// Réglages de parametres.json pour la sauvegarde complète : seulement <see cref="Reglages"/> (le chemin de la base est propre
/// au PC). À la restauration, le dossier des sauvegardes de ce PC est gardé, le mode gestion n'est pas rouvert au démarrage,
/// et l'ancien fichier est copié en « parametres_avant-restauration.json ».
/// </summary>
public sealed class ReglagesSauvegardables(ParametresApplication parametres) : IReglagesSauvegardables
{
    private static readonly JsonSerializerOptions OptionsJson = new() { WriteIndented = true };

    public string Lire() => JsonSerializer.Serialize(parametres.Reglages, OptionsJson);

    public bool EstLisible(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Reglages>(json) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Remplacer(string json)
    {
        var nouveaux = JsonSerializer.Deserialize<Reglages>(json) ?? throw new JsonException("Réglages vides.");
        nouveaux.DossierSauvegardes = parametres.Reglages.DossierSauvegardes;
        nouveaux.DeverrouilleAuDemarrage = false;
        if (File.Exists(parametres.Fichier))
        {
            File.Copy(parametres.Fichier, Path.Combine(Path.GetDirectoryName(parametres.Fichier) ?? ".", "parametres_avant-restauration.json"), overwrite: true);
        }

        // Écrit le fichier sans toucher aux réglages en mémoire (les services les gardent jusqu'au redémarrage).
        var copie = new ParametresApplication { CheminBase = parametres.CheminBase, Reglages = nouveaux };
        Directory.CreateDirectory(Path.GetDirectoryName(parametres.Fichier) ?? ParametresApplication.DossierApplication);
        File.WriteAllText(parametres.Fichier, JsonSerializer.Serialize(copie, OptionsJson));
        parametres.Remplacee = true;
    }
}

/// <summary>Erreurs inattendues : message simple à l'utilisateur, détail dans erreurs.log.</summary>
public static class Erreurs
{
    private static string FichierLog => Path.Combine(ParametresApplication.DossierApplication, "erreurs.log");

    /// <summary>Branche les gestionnaires globaux (à appeler au démarrage).</summary>
    public static void Installer()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Signaler(null, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Journaliser(ex);
            }
        };
    }

    public static void Signaler(IWin32Window? proprietaire, Exception ex)
    {
        Journaliser(ex);
        Dialogues.Erreur(proprietaire, "Une erreur inattendue est survenue. L'opération n'a pas été faite.\n\n" + ex.Message);
    }

    public static void Journaliser(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(ParametresApplication.DossierApplication);
            File.AppendAllText(FichierLog, $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Rien de plus à faire si le journal d'erreurs lui-même est inaccessible.
        }
    }
}

/// <summary>Boîtes de dialogue standard, messages courts en français.</summary>
public static class Dialogues
{
    private const string Titre = "Asprenaut · Matériel";

    public static bool Confirmer(IWin32Window? proprietaire, string message) =>
        MessageBox.Show(proprietaire, message, Titre, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public static void Information(IWin32Window? proprietaire, string message) =>
        MessageBox.Show(proprietaire, message, Titre, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Erreur(IWin32Window? proprietaire, string message) =>
        MessageBox.Show(proprietaire, message, Titre, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
