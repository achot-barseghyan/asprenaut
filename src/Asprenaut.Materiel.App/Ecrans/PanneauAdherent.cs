using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Départ d'une sortie nominative (maquette « Départ du matériel ») : à la place de la zone de scan, après chaque scan,
/// « À qui est ce détendeur ? ». Raccourcis vers les 3 derniers adhérents de la sortie (le plus récent en avant), puis
/// la liste de tous les adhérents actifs, filtrée au fil de la frappe. Tout se fait au clavier : flèches et Entrée dans
/// la recherche, Tab vers les raccourcis. Un code tapé par le pistolet dans la recherche est refusé (<see cref="ScanRefuse"/>).
/// </summary>
public sealed class PanneauAdherent : Carte
{
    private readonly Label _heure = Ui.Libelle("", Theme.PoliceTitre(9.75f), Theme.Retour);
    private readonly Label _question = Ui.Libelle("", Theme.PoliceGrasse(17f));
    private readonly Label _equipement = Ui.Libelle("", Theme.Police(10.5f), Theme.TexteSecondaireClair);
    private readonly PictoCategorie _picto = new();
    private readonly Label _titreRecents = Ui.Libelle("Même adhérent que les derniers scans", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
    private readonly FlowLayoutPanel _recents = new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };
    private readonly ChampSaisie _recherche = new() { TexteIndicatif = "Rechercher un adhérent…", Icone = Icones.Recherche };
    private readonly ListeChoix _liste = new();
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(9.5f), Theme.Avertissement);
    private readonly ValidationSansEntree _rafale;
    private readonly TableLayoutPanel _grille;
    private IReadOnlyList<Adherent> _tous = [];
    private string _code = "";
    private bool _nitrox;

    public PanneauAdherent()
    {
        CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.55f);
        BackColor = Theme.Sur(Theme.Surface, Theme.Accent, 0.05f);
        Padding = new Padding(26, 22, 26, 20);
        Rayon = Theme.RayonZoneScan;

        _recherche.Champ.AccessibleName = "Rechercher un adhérent";
        _recherche.Champ.TextChanged += (_, _) => Filtrer();
        _recherche.Champ.KeyDown += ToucheRecherche;
        _rafale = new ValidationSansEntree(_recherche.Champ, () => RefuserScan(_recherche.Valeur));
        _liste.AccessibleName = "Adhérents";
        _liste.ChoixValide += (_, _) => Valider();
        _liste.BackColor = BackColor;

        var titreListe = Ui.Libelle("Ou choisissez dans la liste des adhérents", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        foreach (var l in new[] { _heure, _question, _equipement, _titreRecents, titreListe, _message })
        {
            l.Margin = Padding.Empty;
        }

        _question.AutoEllipsis = true;
        var textes = Ui.Pile(_heure, _question, _equipement);
        textes.Margin = new Padding(14, 0, 0, 0);
        var entete = Ui.Colonnes((_picto, SizeType.Absolute, 60), (textes, SizeType.Percent, 100));
        _picto.Anchor = AnchorStyles.Left;
        _picto.Dock = DockStyle.None;
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;

        var bloque = new BandeauBloque();

        // Message (adhérent non choisi, scan refusé) et « Annuler ce scan » (scan fait par erreur : l'équipement ne part pas).
        var annuler = new BoutonArrondi("Annuler ce scan", StyleBouton.Lien, Icones.Croix) { Margin = Padding.Empty };
        annuler.Click += (_, _) => AnnulerScan?.Invoke(this, EventArgs.Empty);
        _message.AutoEllipsis = true;
        var basDeListe = Ui.Colonnes((_message, SizeType.Percent, 100), (annuler, SizeType.AutoSize, 0));
        _message.Dock = annuler.Dock = DockStyle.None;
        _message.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        annuler.Anchor = AnchorStyles.Right;

        _grille = Ui.Colonne(
            (entete, SizeType.Absolute, 74),
            (_titreRecents, SizeType.Absolute, 30),
            (_recents, SizeType.Absolute, 66),
            (titreListe, SizeType.Absolute, 30),
            (_recherche, SizeType.Absolute, Theme.HauteurChamp + 8),
            (_liste, SizeType.Percent, 100),
            (basDeListe, SizeType.Absolute, Theme.HauteurCliquable + 2),
            (bloque, SizeType.Absolute, 52));
        _recherche.Margin = new Padding(0, 0, 0, 8);
        _titreRecents.TextAlign = titreListe.TextAlign = ContentAlignment.BottomLeft;
        Controls.Add(_grille);
        AccessibleRole = AccessibleRole.Grouping;
    }

    /// <summary>Un adhérent a été choisi (raccourci, liste, Entrée).</summary>
    public event EventHandler<long>? Choisi;

    private bool _avecRecents;

    /// <summary>
    /// Petit écran (1280 × 800) : sans place pour la liste, on retire les deux titres de section et le bandeau « bloqué »
    /// (le message rouge sous le panneau explique déjà un scan refusé) ; la recherche et la liste restent.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        AjusterRangees();
    }

    private void AjusterRangees()
    {
        if (_grille is null || _grille.RowStyles.Count < 8)
        {
            return;
        }

        float E(float v) => Dessin.Echelle(this, v);
        var fixe = 74 + (_avecRecents ? 66 : 0) + Theme.HauteurChamp + 8 + Theme.HauteurCliquable + 2;
        var compact = Height - Padding.Vertical < E(fixe + 30 + 30 + 52 + 150);
        _grille.RowStyles[1].Height = _avecRecents && !compact ? E(30) : 0;
        _grille.RowStyles[2].Height = _avecRecents ? E(66) : 0;
        _grille.RowStyles[3].Height = compact ? 0 : E(30);
        _grille.RowStyles[7].Height = compact ? 0 : E(52);
        _titreRecents.Visible = _avecRecents && !compact;
        _recents.Visible = _avecRecents;
        _grille.GetControlFromPosition(0, 3)!.Visible = !compact;
        _grille.GetControlFromPosition(0, 7)!.Visible = !compact;
    }

    /// <summary>« Annuler ce scan » : l'équipement en attente est retiré de la sortie.</summary>
    public event EventHandler? AnnulerScan;

    /// <summary>Le pistolet a tapé un code dans la recherche : le scan suivant est refusé tant que l'équipement n'a pas d'adhérent.</summary>
    public event EventHandler<string>? ScanRefuse;

    /// <summary>Code de l'équipement qui attend son adhérent.</summary>
    public string Code => _code;

    /// <summary>
    /// Équipement à attribuer (« À qui est ce détendeur ? »), raccourcis de la sortie et adhérents actifs.
    /// Garde la recherche si c'est le même équipement (rechargement après un refus).
    /// </summary>
    public void Afficher(LigneSortie ligne, IReadOnlyList<AdherentRecent> recents, IReadOnlyList<Adherent> tous)
    {
        var nouveau = ligne.Code != _code;
        _code = ligne.Code;
        _tous = tous;
        _picto.Icone = Icones.PourCategorie(ligne.Categorie);
        _heure.Text = $"Scanné à {ligne.ScanneDepartLe.ToLocalTime():HH:mm}";
        _question.Text = $"À qui est {Article(ligne.Categorie)} ?";
        _nitrox = ligne.Nitrox;
        _equipement.Text = ligne.Nitrox ? $"{ligne.Libelle} · {ligne.Code} · nitrox : adhérent certifié nitrox" : $"{ligne.Libelle} · {ligne.Code}";
        AccessibleName = $"{_question.Text} {ligne.Libelle}, {ligne.Code}";

        _recents.SuspendLayout();
        while (_recents.Controls.Count > 0)
        {
            var c = _recents.Controls[0];
            _recents.Controls.RemoveAt(0);
            c.Dispose();
        }

        for (var i = 0; i < recents.Count; i++)
        {
            var r = recents[i];
            var refuse = _tous.FirstOrDefault(a => a.Id == r.AdherentId) is not { } adherent || ServiceAdherents.RefusPour(adherent, _nitrox, _code) is not null;
            var detail = refuse ? (_nitrox ? "Pas certifié nitrox" : "Désactivé") : (i == 0 ? "Dernier scan · " : "") + $"{r.NbEquipements} équip.";
            var tuile = new TuileAdherent(r.Nom, detail, enAvant: i == 0 && !refuse)
            {
                Margin = new Padding(0, 0, 8, 0),
                TabIndex = i,
            };
            tuile.Click += (_, _) => Choisi?.Invoke(this, r.AdherentId);
            _recents.Controls.Add(tuile);
            tuile.Ajuster(); // Taille en pixels de l'écran, d'après le nom et le détail.
        }

        _recents.ResumeLayout(true);
        _avecRecents = recents.Count > 0;
        AjusterRangees();

        if (nouveau)
        {
            _message.Text = "";
            _recherche.Valeur = "";
        }

        Filtrer();
    }

    /// <summary>« ce détendeur », « cette combinaison », « cette paire de palmes »...</summary>
    public static string Article(string categorie) => categorie.Trim().ToLower(Libelles.Culture) switch
    {
        var c when c.StartsWith("détendeur", StringComparison.Ordinal) => "ce détendeur",
        var c when c.StartsWith("gilet", StringComparison.Ordinal) => "ce gilet",
        var c when c.StartsWith("bloc", StringComparison.Ordinal) => "ce bloc",
        var c when c.StartsWith("combinaison", StringComparison.Ordinal) => "cette combinaison",
        var c when c.StartsWith("palme", StringComparison.Ordinal) => "cette paire de palmes",
        var c when c.StartsWith("masque", StringComparison.Ordinal) => "ce masque",
        var c when c.StartsWith("ordinateur", StringComparison.Ordinal) => "cet ordinateur",
        _ => "cet équipement",
    };

    public void PrendreFocus() => _recherche.Champ.Focus();

    /// <summary>Message sous la liste (« Choisissez d'abord l'adhérent de A07… »).</summary>
    public void Signaler(string message) => _message.Text = message;

    private void Filtrer()
    {
        // Équipement nitrox : les certifiés d'abord ; les autres restent visibles, marqués (le service refuse leur choix).
        var trouves = ServiceAdherents.Filtrer(_tous, _recherche.Valeur).OrderBy(a => ServiceAdherents.RefusPour(a, _nitrox, _code) is not null).ToList();
        // Badge large (comme un code) : deux initiales tiennent toujours, même « HM ».
        _liste.Afficher(trouves.Select(a => new ElementChoix(a.Id.ToString(Libelles.Culture), a.Initiales, true, a.NomComplet,
            ServiceAdherents.RefusPour(a, _nitrox, _code) is null
                ? string.Join(" · ", new[] { a.Niveau, _nitrox ? "certifié nitrox" : null }.Where(t => !string.IsNullOrEmpty(t)))
                : string.Join(" · ", new[] { a.Niveau, "pas certifié nitrox" }.Where(t => !string.IsNullOrEmpty(t))))).ToList(),
            _tous.Count == 0 ? "Aucun adhérent : ajoutez-les dans Membres › Adhérents." : "Aucun adhérent ne correspond.");
        if (trouves.Count > 0 && _liste.Choix is null)
        {
            _liste.Choisir(trouves[0].Id.ToString(Libelles.Culture));
        }
    }

    private void ToucheRecherche(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down or Keys.Up:
                _liste.Deplacer(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                e.Handled = e.SuppressKeyPress = true;
                _rafale.Annuler();
                if (ServiceAdherents.Filtrer(_tous, _recherche.Valeur).Count == 0 && _recherche.Valeur.Trim().Length > 0)
                {
                    RefuserScan(_recherche.Valeur); // Rien ne correspond : c'est sans doute le pistolet.
                }
                else
                {
                    Valider();
                }

                break;
            case Keys.Escape:
                _recherche.Valeur = "";
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void Valider()
    {
        if (_liste.Choix is { } choix && long.TryParse(choix.Cle, out var id))
        {
            Choisi?.Invoke(this, id);
        }
        else
        {
            _message.Text = "Choisissez un adhérent dans la liste.";
        }
    }

    private void RefuserScan(string texte)
    {
        var code = texte.Trim();
        _recherche.Valeur = "";
        if (code.Length > 0)
        {
            ScanRefuse?.Invoke(this, code);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rafale.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Pictogramme de la catégorie dans un carré teinté.</summary>
    private sealed class PictoCategorie : ControleDessine
    {
        private string _icone = Icones.Autre;

        public PictoCategorie() => Size = new Size(60, 60);

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Icone
        {
            get => _icone;
            set => Invalider(ref _icone, value);
        }

        protected override void Peindre(Graphics g)
        {
            var cote = Math.Min(Width, Height) - 1;
            var r = new RectangleF(0, (Height - cote) / 2f, cote, cote);
            using (var chemin = Dessin.Arrondi(r, E(18)))
            using (var fond = new SolidBrush(Theme.Sur(Dessin.CouleurParent(this), Theme.Accent, 0.14f)))
            {
                g.FillPath(fond, chemin);
            }

            Icones.Dessiner(g, _icone, RectangleF.Inflate(r, -cote * 0.25f, -cote * 0.25f), Theme.AccentClair);
        }
    }

    /// <summary>« Le scan suivant est bloqué tant que cet équipement n'a pas d'adhérent. » (cadenas, bandeau jaune).</summary>
    private sealed class BandeauBloque : ControleDessine
    {
        private const string Texte = "Le scan suivant est bloqué tant que cet équipement n'a pas d'adhérent.";

        public BandeauBloque()
        {
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = Texte;
            Margin = new Padding(0, 6, 0, 0);
        }

        protected override void Peindre(Graphics g)
        {
            var parent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var fond = new SolidBrush(Theme.Sur(parent, Theme.Avertissement, 0.08f)))
            using (var bord = new Pen(Theme.Sur(parent, Theme.Avertissement, 0.35f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var t = E(18);
            Icones.Dessiner(g, Icones.Cadenas, new RectangleF(E(14), (Height - t) / 2, t, t), Theme.Avertissement, 2f);
            var x = (int)(E(14) + t + E(10));
            TextRenderer.DrawText(g, Texte, Theme.Police(9.75f), new Rectangle(x, 0, Width - x - (int)E(10), Height), Theme.Texte,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Raccourci vers un adhérent récent : pastille aux initiales, nom, « Dernier scan · 3 équip. ». Clic, Entrée ou Espace.</summary>
    private sealed class TuileAdherent : Control
    {
        private readonly string _detail;
        private readonly bool _enAvant;
        private bool _survol;

        public TuileAdherent(string nom, string detail, bool enAvant)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = nom;
            _detail = detail;
            _enAvant = enAvant;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = $"{nom}, {detail}";
        }

        private float E(float v) => Dessin.Echelle(this, v);

        /// <summary>Largeur d'après le texte (pastille + marges en pixels logiques), hauteur 58 : à appeler une fois la tuile placée.</summary>
        public void Ajuster()
        {
            var texte = Math.Max(TextRenderer.MeasureText(Text, Theme.PoliceTitre(10.5f)).Width, TextRenderer.MeasureText(_detail, Theme.Police(9f)).Width);
            Size = new Size(Math.Max((int)E(180), texte + (int)E(78)), (int)E(58));
        }

        protected override void OnMouseEnter(EventArgs e) { _survol = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { _survol = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = e.SuppressKeyPress = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var parent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var fond = _enAvant ? Theme.Sur(parent, Theme.Accent, _survol ? 0.20f : 0.14f) : Theme.Sur(parent, Theme.Fond, _survol ? 0.25f : 0.45f);
            var bord = Focused ? Theme.Accent : _enAvant ? Theme.Sur(parent, Theme.Accent, 0.55f) : Theme.Sur(parent, Theme.AccentClair, 0.18f);
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var pinceau = new SolidBrush(fond))
            using (var stylo = new Pen(bord, Focused ? E(2) : E(1.5f)))
            {
                g.FillPath(pinceau, chemin);
                g.DrawPath(stylo, chemin);
            }

            var d = E(38);
            var cercle = new RectangleF(E(10), (Height - d) / 2, d, d);
            using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Accent, 0.22f)))
            {
                g.FillEllipse(pinceau, cercle);
            }

            Dessin.Texte(g, Libelles.Initiales(Text), Theme.PoliceGrasse(9.75f), Rectangle.Round(cercle), Theme.AccentClair, centre: true);
            var x = (int)(cercle.Right + E(10));
            var milieu = Height / 2;
            Dessin.Texte(g, Text, Theme.PoliceTitre(10.5f), new Rectangle(x, milieu - (int)E(20), Width - x - (int)E(8), (int)E(22)), Theme.Texte);
            Dessin.Texte(g, _detail, Theme.Police(9f), new Rectangle(x, milieu + (int)E(1), Width - x - (int)E(8), (int)E(18)), Theme.TexteSecondaire);
        }
    }
}
