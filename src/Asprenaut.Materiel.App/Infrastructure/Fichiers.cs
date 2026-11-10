using System.Text;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>Enregistrement de fichiers exportés (CSV) : choix de l'emplacement, écriture, messages simples.</summary>
public static class Fichiers
{
    /// <summary>UTF-8 avec BOM : Excel lit correctement les accents.</summary>
    public static readonly Encoding Utf8Excel = new UTF8Encoding(true);

    /// <summary>Demande où enregistrer le CSV et l'écrit. Vrai si le fichier a été écrit.</summary>
    public static async Task<bool> EnregistrerCsvAsync(IWin32Window? proprietaire, string titre, string nomPropose, string contenu)
    {
        using var dialogue = new SaveFileDialog
        {
            Title = titre,
            Filter = "Fichier CSV (Excel)|*.csv",
            FileName = nomPropose,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialogue.ShowDialog(proprietaire) != DialogResult.OK)
        {
            return false;
        }

        try
        {
            await File.WriteAllTextAsync(dialogue.FileName, contenu, Utf8Excel);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogues.Erreur(proprietaire, "Impossible d'écrire le fichier. Est-il ouvert dans Excel ?");
            return false;
        }
    }

    /// <summary>
    /// Document PDF : vérifie l'imprimante PDF de Windows, demande où l'enregistrer, l'écrit (<paramref name="ecrire"/>
    /// reçoit le chemin), puis propose de l'ouvrir. Vrai si le fichier a été écrit.
    /// </summary>
    public static async Task<bool> EnregistrerPdfAsync(Control proprietaire, string titre, string nomPropose, Action<string> ecrire)
    {
        var fenetre = proprietaire.FindForm();
        if (!Impression.DocumentPdf.ImprimanteDisponible())
        {
            Dialogues.Erreur(fenetre, "L'imprimante « Microsoft Print to PDF » n'est pas installée sur ce PC : impossible de créer le PDF.");
            return false;
        }

        using var dialogue = new SaveFileDialog
        {
            Title = titre,
            Filter = "Document PDF|*.pdf",
            FileName = nomPropose,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialogue.ShowDialog(fenetre) != DialogResult.OK)
        {
            return false;
        }

        var curseur = proprietaire.Cursor;
        try
        {
            proprietaire.Cursor = Cursors.WaitCursor;
            await Task.Yield(); // Le curseur d'attente s'affiche avant l'impression.
            ecrire(dialogue.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Drawing.Printing.InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            Dialogues.Erreur(fenetre, "Impossible d'écrire le PDF. Est-il ouvert dans un autre programme ?");
            return false;
        }
        finally
        {
            proprietaire.Cursor = curseur;
        }

        if (Dialogues.Confirmer(fenetre, "Document enregistré. L'ouvrir maintenant ?"))
        {
            Ouvrir(dialogue.FileName);
        }

        return true;
    }

    /// <summary>Ouvre un fichier ou un dossier avec le programme associé (Excel, l'Explorateur...) ; sans erreur si impossible.</summary>
    public static void Ouvrir(string chemin)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(chemin) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Aucun programme associé : le fichier est quand même enregistré.
        }
    }
}
