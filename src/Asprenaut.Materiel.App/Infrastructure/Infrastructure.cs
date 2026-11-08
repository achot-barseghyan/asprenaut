using System.Globalization;
using System.Text.Json;

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

    private static string Fichier => Path.Combine(DossierApplication, "parametres.json");

    /// <summary>Chemin de la base SQLite (modifiable depuis l'écran Paramètres).</summary>
    public string CheminBase { get; set; } = CheminBaseParDefaut;

    public static ParametresApplication Charger()
    {
        try
        {
            if (File.Exists(Fichier))
            {
                var lus = JsonSerializer.Deserialize<ParametresApplication>(File.ReadAllText(Fichier));
                if (lus is not null && !string.IsNullOrWhiteSpace(lus.CheminBase))
                {
                    return lus;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Erreurs.Journaliser(ex);
        }

        return new ParametresApplication();
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(DossierApplication);
        File.WriteAllText(Fichier, JsonSerializer.Serialize(this, OptionsJson));
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
