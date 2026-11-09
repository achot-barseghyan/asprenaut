using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Petite fenêtre modale sombre : titre, champs, message d'erreur, boutons Annuler / Valider.
/// La validation est confiée à un service (Core) : la fenêtre ne se ferme que si elle réussit.
/// </summary>
public sealed class FenetreFormulaire : Form
{
    private const int Largeur = 520;

    private readonly TableLayoutPanel _champs;
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10.5f), Theme.Erreur);
    private readonly BoutonArrondi _valider;
    private readonly Func<Task<ResultatOperation>> _validation;

    public FenetreFormulaire(string titre, string texteValider, Func<Task<ResultatOperation>> validation)
    {
        _validation = validation;
        Text = titre;
        BackColor = Theme.Surface;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = Icones.IconeFenetre;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(28, 24, 28, 24);

        _champs = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, Dock = DockStyle.Fill };
        _champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { Margin = new Padding(12, 0, 0, 0), DialogResult = DialogResult.Cancel };
        _valider = new BoutonArrondi(texteValider, StyleBouton.Principal) { Margin = new Padding(12, 0, 0, 0) };
        _valider.Click += async (_, _) => await ValiderAsync();
        CancelButton = annuler;
        AcceptButton = _valider;

        var boutons = Ui.Rangee(false, _valider, annuler);
        boutons.Margin = new Padding(0, 16, 0, 0);
        boutons.Anchor = AnchorStyles.Right;
        _message.MaximumSize = new Size(Largeur - 56, 0);
        _message.Margin = new Padding(0, 8, 0, 0);
        _message.Visible = false; // Affiché seulement en cas d'erreur.

        var titreLibelle = Ui.Libelle(titre, Theme.PoliceGrasse(16f));
        titreLibelle.Margin = new Padding(0, 0, 0, 18);

        var page = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, MinimumSize = new Size(Largeur - 56, 0) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (c, i) in new Control[] { titreLibelle, _champs, _message, boutons }.Select((c, i) => (c, i)))
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(c, 0, i);
        }

        Controls.Add(page);
    }

    /// <summary>Ajoute un champ : libellé au-dessus, contrôle pleine largeur.</summary>
    public T Champ<T>(string libelle, T controle, int hauteur = Theme.HauteurChamp)
        where T : Control
    {
        var etiquette = Ui.LibelleChamp(libelle);
        Ajouter(etiquette, SizeType.AutoSize, 0);
        controle.Margin = new Padding(0, 0, 0, 14);
        Ajouter(controle, SizeType.Absolute, hauteur + 14);
        return controle;
    }

    /// <summary>Ajoute deux champs côte à côte (libellés au-dessus).</summary>
    public void DeuxChamps(string libelle1, Control champ1, string libelle2, Control champ2, int hauteur = Theme.HauteurChamp)
    {
        var table = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 14) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, hauteur));
        var l1 = Ui.LibelleChamp(libelle1);
        var l2 = Ui.LibelleChamp(libelle2);
        l1.Margin = new Padding(0, 0, 7, 8);
        l2.Margin = new Padding(7, 0, 0, 8);
        champ1.Margin = new Padding(0, 0, 7, 0);
        champ2.Margin = new Padding(7, 0, 0, 0);
        champ1.Dock = champ2.Dock = DockStyle.Fill;
        table.Controls.Add(l1, 0, 0);
        table.Controls.Add(l2, 1, 0);
        table.Controls.Add(champ1, 0, 1);
        table.Controls.Add(champ2, 1, 1);
        Ajouter(table);
    }

    /// <summary>Ajoute une ligne libre (case à cocher, texte d'aide...).</summary>
    public void Ajouter(Control controle, SizeType type = SizeType.AutoSize, float valeur = 0)
    {
        _champs.RowStyles.Add(new RowStyle(type, valeur));
        controle.Dock = DockStyle.Fill;
        _champs.Controls.Add(controle, 0, _champs.RowStyles.Count - 1);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Natif.BarreDeTitre(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        // Construite en pixels logiques : mise à l'échelle de l'écran (125 %, 150 %...).
        foreach (Control c in Controls)
        {
            Dessin.MettreALEchelle(c, this);
        }

        Padding = new Padding((int)Dessin.Echelle(this, 28), (int)Dessin.Echelle(this, 24), (int)Dessin.Echelle(this, 28), (int)Dessin.Echelle(this, 24));
        base.OnLoad(e);
    }

    private async Task ValiderAsync()
    {
        _valider.Enabled = false;
        try
        {
            var resultat = await _validation();
            if (resultat.Reussi)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _message.Text = resultat.Message;
            _message.Visible = true;
        }
        catch (Exception ex)
        {
            Erreurs.Signaler(this, ex);
        }
        finally
        {
            if (!IsDisposed)
            {
                _valider.Enabled = true;
            }
        }
    }
}
