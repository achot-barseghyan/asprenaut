using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Petite fenêtre modale sombre : titre, champs, message d'erreur, boutons Annuler / Valider.
/// La validation est confiée à un service (Core) : la fenêtre ne se ferme que si elle réussit.
/// </summary>
public sealed class FenetreFormulaire : Form, IFondDessine
{
    /// <summary>Largeur d'origine des formulaires (pixels logiques, marges comprises).</summary>
    public const int LargeurNormale = 520;

    private readonly int _marge;
    private readonly int _ecart;

    private readonly TableLayoutPanel _champs;
    private readonly Panel _cadre;
    private readonly TableLayoutPanel _page;
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(10.5f), Theme.Erreur);
    private readonly BoutonArrondi _valider;
    private readonly Label _sousTitre = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaireClair);
    private readonly Pastille _badge = new("", Theme.FondRetour, Theme.Retour, police: Theme.PoliceGrasse(7.5f), hauteur: 22);
    private readonly AvatarEntete _avatar = new();
    private readonly FlowLayoutPanel _etiquettes = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent };
    private Label _titre = null!;
    private TableLayoutPanel _bas = null!;
    private TableLayoutPanel _entete = null!;
    private readonly Label _note = new LibelleObligatoire("", Theme.Police(9f), Theme.TexteSecondaire, etoileDevant: true);
    private readonly Func<Task<ResultatOperation>> _validation;

    /// <param name="largeur">Largeur de la fenêtre (marges comprises), en pixels logiques.</param>
    /// <param name="marge">Marge à gauche et à droite ; l'écart entre deux champs côte à côte la suit.</param>
    public FenetreFormulaire(string titre, string texteValider, Func<Task<ResultatOperation>> validation, int largeur = LargeurNormale, int marge = 28)
    {
        _validation = validation;
        _marge = marge;
        _ecart = Math.Max(7, marge / 4);
        var largeurChamps = largeur - 2 * marge;
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
        Padding = new Padding(marge, 24, marge, 24);

        // Champs dans un cadre qui défile si la fenêtre dépasse la hauteur de l'écran (fiche d'un détendeur sur un petit écran).
        _champs = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            MinimumSize = new Size(largeurChamps, 0),
        };
        _champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _cadre = new Panel { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
        _cadre.Controls.Add(_champs);
        Natif.ScrollbarsDuTheme(_cadre);
        _champs.SizeChanged += (_, _) => AjusterCadre();

        var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { Margin = new Padding(12, 0, 0, 0), DialogResult = DialogResult.Cancel };
        _valider = new BoutonArrondi(texteValider, StyleBouton.Principal) { Margin = new Padding(12, 0, 0, 0) };
        _valider.Click += async (_, _) => await ValiderAsync();
        CancelButton = annuler;
        AcceptButton = _valider;

        var boutons = Ui.Rangee(false, _valider, annuler);
        boutons.Margin = Padding.Empty;
        boutons.Anchor = AnchorStyles.Right;
        // Bas de la fenêtre : note facultative à gauche (« * champs obligatoires »), boutons à droite.
        _note.Visible = false;
        _note.Anchor = AnchorStyles.Left;
        var bas = _bas = Ui.Colonnes((_note, SizeType.Percent, 100), (boutons, SizeType.AutoSize, 0));
        bas.AutoSize = true;
        bas.Dock = DockStyle.Fill;
        // Colonnes met Dock Fill : la note se centre à côté des boutons (Anchor après Dock, qui le remet à zéro).
        _note.Dock = boutons.Dock = DockStyle.None;
        _note.Anchor = AnchorStyles.Left;
        boutons.Anchor = AnchorStyles.Right;
        bas.Margin = new Padding(0, 16, 0, 0);
        _message.MaximumSize = new Size(largeurChamps, 0);
        _message.Margin = new Padding(0, 8, 0, 0);
        _message.Visible = false; // Affiché seulement en cas d'erreur.

        var titreLibelle = _titre = Ui.Libelle(titre, Theme.PoliceGrasse(16f));
        titreLibelle.Margin = Padding.Empty;
        _sousTitre.Margin = new Padding(0, 2, 0, 0);
        _sousTitre.Visible = false;
        _badge.Visible = false;
        _badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _badge.Margin = new Padding(12, 4, 0, 0);
        _avatar.Visible = false;
        _avatar.Margin = new Padding(0, 0, 14, 0);
        _avatar.Anchor = AnchorStyles.Left;
        _etiquettes.Visible = false;
        _etiquettes.Margin = new Padding(0, 6, 0, 0);
        var textes = Ui.Pile(titreLibelle, _sousTitre, _etiquettes);
        textes.Anchor = AnchorStyles.Left;
        var entete = _entete = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18), Dock = DockStyle.Fill };
        entete.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entete.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entete.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entete.Controls.Add(_avatar, 0, 0);
        entete.Controls.Add(textes, 1, 0);
        entete.Controls.Add(_badge, 2, 0);

        var page = _page = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = Padding.Empty, MinimumSize = new Size(largeurChamps, 0) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (c, i) in new Control[] { entete, _cadre, _message, bas }.Select((c, i) => (c, i)))
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(c, 0, i);
        }

        Controls.Add(page);
        page.SizeChanged += (_, _) => { if (Separations) { Invalidate(); } }; // Filets suivant la hauteur (message d'erreur affiché…).
    }

    /// <summary>Sous-titre sous le titre (« Détendeur Scubapro MK25 EVO · A700 ») et pastille optionnelle à droite (« Mode gestion »).</summary>
    /// <param name="icone">Pictogramme dans un rond à gauche du titre (maquette « Ajouter un adhérent »).</param>
    public void EnTete(string sousTitre, string? badge = null, string? icone = null)
    {
        _sousTitre.Text = sousTitre;
        _sousTitre.Visible = sousTitre.Length > 0;
        _badge.Text = badge ?? "";
        _badge.Visible = !string.IsNullOrEmpty(badge);
        _avatar.Icone = icone ?? "";
        _avatar.Visible = icone is not null;
    }

    /// <summary>Titre affiché dans la fenêtre (aperçu : « Camille Martin » pendant la saisie).</summary>
    public void Titre(string texte) => _titre.Text = texte;

    /// <summary>Rond de l'en-tête (voir <see cref="EnTete"/>) : <see cref="AvatarEntete.Initiales"/> pendant la saisie.</summary>
    public AvatarEntete Avatar => _avatar;

    /// <summary>
    /// Étiquettes sous le titre (« Niveau 2 », « NITROX », « CACI valide ») ; à la place du sous-titre tant qu'il y en a.
    /// </summary>
    public void Etiquettes(IEnumerable<(string Texte, Color Couleur)> etiquettes)
    {
        var liste = etiquettes.ToList();
        _etiquettes.SuspendLayout();
        while (_etiquettes.Controls.Count > 0)
        {
            _etiquettes.Controls[0].Dispose();
        }

        foreach (var (texte, couleur) in liste)
        {
            var pastille = new Pastille(texte.ToUpper(Core.Modeles.Libelles.Culture), Theme.Teinte(couleur, 0.16f), couleur, police: Theme.PoliceGrasse(7.5f), hauteur: 22)
            {
                Margin = new Padding(0, 0, 6, 0),
                TabStop = false,
            };
            if (IsHandleCreated)
            {
                Dessin.MettreALEchelle(pastille, this); // Ajoutée après l'affichage.
            }

            _etiquettes.Controls.Add(pastille);
        }

        _etiquettes.ResumeLayout();
        _etiquettes.Visible = liste.Count > 0;
        _sousTitre.Visible = liste.Count == 0 && _sousTitre.Text.Length > 0;
    }

    /// <summary>Filet sous l'en-tête et bas de fenêtre sur fond plus sombre, séparé par un filet (maquette « Ajouter un adhérent »).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Separations { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (!Separations)
        {
            return;
        }

        using var filet = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
        var yEntete = _page.Top + _entete.Bottom + (_entete.Margin.Bottom / 2);
        e.Graphics.DrawLine(filet, 0, yEntete, ClientSize.Width, yEntete);
        var yBas = _page.Top + _bas.Top - _bas.Margin.Top;
        using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Fond, 0.45f)))
        {
            e.Graphics.FillRectangle(fond, 0, yBas, ClientSize.Width, ClientSize.Height - yBas);
        }

        e.Graphics.DrawLine(filet, 0, yBas, ClientSize.Width, yBas);
    }

    /// <summary>Note en bas à gauche, précédée d'une étoile (« champs obligatoires »).</summary>
    public void Note(string texte)
    {
        _note.Text = texte;
        _note.Visible = texte.Length > 0;
    }

    /// <summary>Texte du bouton de validation.</summary>
    public BoutonArrondi Valider => _valider;

    /// <summary>Ajoute un champ : libellé au-dessus, contrôle pleine largeur.</summary>
    public T Champ<T>(string libelle, T controle, int hauteur = Theme.HauteurChamp)
        where T : Control
    {
        AjouterChamp(_champs, libelle, controle, hauteur);
        return controle;
    }

    /// <summary>Libellé au-dessus d'un champ ; « Prénom * » → étoile de couleur (champ obligatoire).</summary>
    public static Label LibelleChamp(string texte)
    {
        if (!texte.EndsWith(" *", StringComparison.Ordinal))
        {
            return Ui.LibelleChamp(texte);
        }

        return new LibelleObligatoire(texte[..^2], Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair) { Margin = new Padding(0, 0, 0, 8) };
    }

    internal static void AjouterChamp(TableLayoutPanel table, string libelle, Control controle, int hauteur)
    {
        AjouterLigne(table, LibelleChamp(libelle), SizeType.AutoSize, 0);
        controle.Margin = new Padding(0, 0, 0, 14);
        AjouterLigne(table, controle, SizeType.Absolute, hauteur + 14);
    }

    internal static void AjouterLigne(TableLayoutPanel table, Control controle, SizeType type, float valeur)
    {
        table.RowStyles.Add(new RowStyle(type, valeur));
        controle.Dock = DockStyle.Fill;
        table.Controls.Add(controle, 0, table.RowStyles.Count - 1);
    }

    /// <summary>Deux champs côte à côte, libellés au-dessus, séparés de 2 × <paramref name="ecart"/>.</summary>
    internal static TableLayoutPanel RangeeDeuxChamps(string libelle1, Control champ1, string libelle2, Control champ2, int hauteur, int ecart)
    {
        var table = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 14) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, hauteur));
        var l1 = LibelleChamp(libelle1);
        var l2 = LibelleChamp(libelle2);
        l1.Margin = new Padding(0, 0, ecart, 8);
        l2.Margin = new Padding(ecart, 0, 0, 8);
        champ1.Margin = new Padding(0, 0, ecart, 0);
        champ2.Margin = new Padding(ecart, 0, 0, 0);
        champ1.Dock = champ2.Dock = DockStyle.Fill;
        table.Controls.Add(l1, 0, 0);
        table.Controls.Add(l2, 1, 0);
        table.Controls.Add(champ1, 0, 1);
        table.Controls.Add(champ2, 1, 1);
        return table;
    }

    /// <summary>
    /// Ajoute deux champs côte à côte (libellés au-dessus). Retourne la rangée : on peut la masquer (Visible) ou
    /// changer ses libellés (<see cref="Libelles"/>) selon le reste du formulaire.
    /// </summary>
    public TableLayoutPanel DeuxChamps(string libelle1, Control champ1, string libelle2, Control champ2, int hauteur = Theme.HauteurChamp)
    {
        var table = RangeeDeuxChamps(libelle1, champ1, libelle2, champ2, hauteur, _ecart);
        Ajouter(table);
        return table;
    }

    /// <summary>Libellés (gauche, droite) d'une rangée créée par <see cref="DeuxChamps"/>.</summary>
    public static (Label Gauche, Label Droite) Libelles(TableLayoutPanel rangee) =>
        ((Label)rangee.GetControlFromPosition(0, 0)!, (Label)rangee.GetControlFromPosition(1, 0)!);

    /// <summary>Ajoute une ligne libre (case à cocher, texte d'aide...).</summary>
    public void Ajouter(Control controle, SizeType type = SizeType.AutoSize, float valeur = 0)
    {
        AjouterLigne(_champs, controle, type, valeur);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // La fenêtre grandit vers le bas (panneau du CACI ouvert…) : elle remonte pour ne pas passer sous la barre des tâches.
        var zone = Screen.FromControl(this).WorkingArea;
        if (Visible && Bottom > zone.Bottom)
        {
            Top = Math.Max(zone.Top, zone.Bottom - Height);
        }
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

        Padding = new Padding((int)Dessin.Echelle(this, _marge), (int)Dessin.Echelle(this, 24), (int)Dessin.Echelle(this, _marge), (int)Dessin.Echelle(this, 24));
        _page.Location = new Point(Padding.Left, Padding.Top); // Sinon la page reste collée au bord gauche.
        AjusterCadre();
        base.OnLoad(e);
    }

    /// <summary>
    /// Le cadre prend la taille des champs, mais pas plus haut que l'écran moins le titre et les boutons :
    /// au-delà, les champs défilent (barre verticale à droite, place réservée).
    /// </summary>
    private void AjusterCadre()
    {
        var ecran = Screen.FromControl(this).WorkingArea.Height;
        var max = Math.Max((int)Dessin.Echelle(this, 220), ecran - (int)Dessin.Echelle(this, 290));
        var defile = _champs.Height > max;
        var largeur = _champs.Width + (defile ? SystemInformation.VerticalScrollBarWidth + (int)Dessin.Echelle(this, 8) : 0);
        var taille = new Size(largeur, Math.Min(_champs.Height, max));
        if (_cadre.Size != taille)
        {
            _cadre.Size = taille;
        }
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
