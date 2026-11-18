using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Rangée de champs côte à côte, libellé au-dessus de chacun (maquette « Ajouter un équipement » : Pour | Code | Nom).
/// Une cellule peut avoir une largeur fixe ; les autres se partagent la place. Une cellule masquée (<see cref="Montrer"/>)
/// laisse sa place aux autres.
/// </summary>
public sealed class RangeeChamps : Panel
{
    private const int Ecart = 14;
    private const int EcartLibelle = 8;

    private sealed class Cellule(Label libelle, Control champ, int largeurFixe)
    {
        public Label Libelle { get; } = libelle;
        public Control Champ { get; } = champ;
        public int LargeurFixe { get; } = largeurFixe;
        public bool Affichee { get; set; } = true;
    }

    private readonly List<Cellule> _cellules = [];

    public RangeeChamps()
    {
        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 0, 14);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Ajoute un champ ; « Code * » → étoile. <paramref name="largeurFixe"/> en pixels logiques (0 : partagée).</summary>
    public T Ajouter<T>(string libelle, T champ, int largeurFixe = 0)
        where T : Control => Ajouter(FenetreFormulaire.LibelleChamp(libelle), champ, largeurFixe);

    public T Ajouter<T>(Label libelle, T champ, int largeurFixe = 0)
        where T : Control
    {
        libelle.AutoSize = true;
        libelle.Margin = Padding.Empty;
        champ.Margin = Padding.Empty;
        champ.Dock = DockStyle.None;
        Controls.Add(libelle);
        Controls.Add(champ);
        _cellules.Add(new Cellule(libelle, champ, largeurFixe));
        return champ;
    }

    /// <summary>Affiche ou masque la cellule de ce champ.</summary>
    public void Montrer(Control champ, bool affichee)
    {
        if (_cellules.FirstOrDefault(c => c.Champ == champ) is not { } cellule || cellule.Affichee == affichee)
        {
            return;
        }

        cellule.Affichee = affichee;
        cellule.Libelle.Visible = cellule.Champ.Visible = affichee;
        PerformLayout();
        Parent?.PerformLayout(this, nameof(Height));
    }

    private float K => Dessin.Echelle(this, 1);

    private int HauteurLibelles => _cellules.Where(c => c.Affichee).Select(c => c.Libelle.GetPreferredSize(Size.Empty).Height).DefaultIfEmpty(0).Max();

    public override Size GetPreferredSize(Size proposedSize) =>
        new(MesureFormulaire.Largeur(this, proposedSize), HauteurLibelles + (int)(K * (EcartLibelle + Theme.HauteurChamp)));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var affichees = _cellules.Where(c => c.Affichee).ToList();
        if (affichees.Count == 0)
        {
            return;
        }

        var k = K;
        var ecart = (int)(Ecart * k);
        var fixes = affichees.Where(c => c.LargeurFixe > 0).Sum(c => (int)(c.LargeurFixe * k));
        var partagees = affichees.Count(c => c.LargeurFixe <= 0);
        var reste = Math.Max(0, Width - fixes - (ecart * (affichees.Count - 1)));
        var hLibelles = HauteurLibelles;
        var yChamp = hLibelles + (int)(EcartLibelle * k);
        var hChamp = (int)(Theme.HauteurChamp * k);
        var x = 0;
        for (var i = 0; i < affichees.Count; i++)
        {
            var c = affichees[i];
            var largeur = c.LargeurFixe > 0 ? (int)(c.LargeurFixe * k)
                : i == affichees.Count - 1 && c.LargeurFixe <= 0 ? Width - x
                : reste / Math.Max(1, partagees);
            var hLibelle = c.Libelle.GetPreferredSize(Size.Empty).Height;
            c.Libelle.SetBounds(x, hLibelles - hLibelle, largeur, hLibelle);
            var hauteur = c.Champ is Bascule ? c.Champ.Height : hChamp;
            c.Champ.SetBounds(x, yChamp + ((hChamp - hauteur) / 2), largeur, hauteur);
            x += largeur + ecart;
        }
    }
}

/// <summary>
/// Élément de la composition d'un détendeur : point turquoise (obligatoire) ou interrupteur (présent / absent),
/// nom et précision dessous (« obligatoire », « source de secours », « absent »).
/// </summary>
public sealed class ElementComposition : ControleDessine
{
    private readonly string _nom;
    private readonly string _precision;

    public ElementComposition(string nom, string precision, bool facultatif)
    {
        _nom = nom;
        _precision = precision;
        TabStop = false;
        Size = new Size(190, Theme.HauteurChamp);
        if (facultatif)
        {
            Presence = new Interrupteur($"{nom} présent") { Checked = true, Location = new Point(0, (Theme.HauteurChamp - Theme.HauteurCliquable) / 2) };
            Presence.CheckedChanged += (_, _) => Invalidate();
            Controls.Add(Presence);
        }
    }

    /// <summary>Interrupteur d'un élément facultatif (octopus, manomètre), sinon null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Interrupteur? Presence { get; }

    /// <summary>Présent : obligatoire, ou interrupteur allumé.</summary>
    public bool Present => Presence?.Checked ?? true;

    protected override void Peindre(Graphics g)
    {
        float x;
        if (Presence is { } p)
        {
            x = p.Right + E(10);
        }
        else
        {
            var d = E(8);
            using var point = new SolidBrush(Theme.Accent);
            g.FillEllipse(point, E(26), (Height - d) / 2, d, d);
            x = E(60) + E(10);
        }

        var largeur = (int)(Width - x);
        var hNom = (int)E(22);
        var haut = (int)((Height / 2f) - E(20));
        Dessin.Texte(g, _nom, Theme.PoliceGrasse(10f), new Rectangle((int)x, haut, largeur, hNom), Present ? Theme.Texte : Theme.TexteSecondaire);
        Dessin.Texte(g, Present ? _precision : "absent", Theme.Police(8.25f), new Rectangle((int)x, haut + hNom, largeur, (int)E(18)), Theme.TexteSecondaire);
    }
}

/// <summary>
/// Composition d'un détendeur (maquette) : en-têtes Élément | Marque | Modèle | N° de série, une ligne par élément,
/// filet entre deux éléments ; une ligne peut être suivie d'une sous-ligne (Raccord, D.S).
/// </summary>
public sealed class GrilleComposition : TableLayoutPanel
{
    private const int LargeurElement = 190;
    private readonly HashSet<int> _filets = [];

    public GrilleComposition()
    {
        ColumnCount = 4;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 0, 10);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LargeurElement));
        for (var i = 0; i < 3; i++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 / 3f));
        }

        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var titres = new[] { "Élément", "Marque", "Modèle", "N° de série" };
        for (var i = 0; i < titres.Length; i++)
        {
            var l = Ui.Libelle(titres[i].ToUpper(Libelles.Culture), Theme.PoliceGrasse(7.5f), Theme.TexteSecondaire);
            l.Margin = new Padding(i == 0 ? 0 : 12, 0, 0, 8);
            Controls.Add(l, i, 0);
        }
    }

    /// <summary>Ligne d'un élément ; une case null (donnée que la fiche ne garde pas) reste vide.</summary>
    public void Element(ElementComposition element, Control? marque, Control? modele, Control? numero)
    {
        var ligne = RowStyles.Count;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _filets.Add(ligne);
        element.Margin = new Padding(0, 10, 0, 10);
        element.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(element, 0, ligne);
        var cases = new[] { marque, modele, numero };
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i] ?? new Label { Text = "—", AutoSize = false, ForeColor = Theme.TexteSecondaire, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Height = Theme.HauteurChamp, Font = Theme.Police(10.5f) };
            c.Margin = new Padding(12, 10, 0, 10);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            c.Height = Theme.HauteurChamp;
            Controls.Add(c, i + 1, ligne);
        }
    }

    /// <summary>Sous-ligne sous les trois cases de la dernière ligne (Raccord, D.S du premier étage).</summary>
    public void SousLigne(Control controle)
    {
        var ligne = RowStyles.Count;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controle.Margin = new Padding(12, 0, 0, 12);
        controle.Anchor = AnchorStyles.Left;
        Controls.Add(controle, 1, ligne);
        SetColumnSpan(controle, 3);
    }

    protected override void OnCellPaint(TableLayoutCellPaintEventArgs e)
    {
        base.OnCellPaint(e);
        if (e.Row > 1 && _filets.Contains(e.Row))
        {
            using var filet = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
            e.Graphics.DrawLine(filet, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Right, e.CellBounds.Top);
        }
    }
}

/// <summary>
/// Option « Nitrox » (maquette) : carte de la hauteur d'un champ, pastille « O₂ », texte, interrupteur ; verte quand cochée.
/// Case à cocher accessible (Espace).
/// </summary>
public sealed class OptionNitrox : CheckBox
{
    public OptionNitrox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = false;
        Height = Theme.HauteurChamp;
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        AccessibleName = "Nitrox : adhérents certifiés seulement";
        Text = "";
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    private float E(float v) => Dessin.Echelle(this, v);

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var vert = Theme.Nitrox;
        var fondChamp = ChampSaisie.FondChamp;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(r, E(Theme.RayonBouton)))
        {
            using var fond = new SolidBrush(Checked ? Theme.Sur(fondChamp, vert, 0.10f) : fondChamp);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Focused ? Theme.Accent : Checked ? Theme.Sur(fondChamp, vert, 0.5f) : Theme.BordureForte, E(Focused ? 2 : 1.2f));
            g.DrawPath(bord, chemin);
        }

        // Pastille « O₂ ».
        var cote = E(32);
        var pastille = new RectangleF(E(10), (Height - cote) / 2, cote, cote);
        using (var chemin = Dessin.Arrondi(pastille, E(9)))
        using (var fond = new SolidBrush(Checked ? vert : Theme.Teinte(Theme.AccentClair, 0.12f)))
        {
            g.FillPath(fond, chemin);
        }

        Dessin.Texte(g, "O₂", Theme.PoliceGrasse(9f), Rectangle.Round(pastille), Checked ? Theme.TexteSurRetour : Theme.TexteSecondaire, centre: true);

        // Interrupteur à droite.
        var l = E(40);
        var h = E(24);
        var piste = new RectangleF(Width - l - E(12), (Height - h) / 2, l, h);
        using (var chemin = Dessin.Arrondi(piste, h / 2))
        using (var fond = new SolidBrush(Checked ? vert : Theme.Teinte(Theme.AccentClair, 0.18f)))
        {
            g.FillPath(fond, chemin);
        }

        var d = E(18);
        using (var bouton = new SolidBrush(Color.White))
        {
            g.FillEllipse(bouton, Checked ? piste.Right - E(3) - d : piste.Left + E(3), piste.Top + E(3), d, d);
        }

        // Textes.
        var x = (int)(pastille.Right + E(10));
        var largeur = (int)(piste.Left - E(8)) - x;
        var haut = (int)((Height / 2f) - E(19));
        Dessin.Texte(g, "Nitrox", Theme.PoliceGrasse(10.5f), new Rectangle(x, haut, largeur, (int)E(21)), Theme.Texte);
        Dessin.Texte(g, "Adhérents certifiés seulement", Theme.Police(8.25f), new Rectangle(x, haut + (int)E(20), largeur, (int)E(17)), Theme.TexteSecondaire);
    }
}

/// <summary>
/// Aperçu de l'équipement pendant la saisie (colonne de droite de la maquette) : catégorie, médaille au code, nom,
/// pastilles (raccord, nitrox), points renseignés ou non, et en bas la prochaine échéance.
/// </summary>
public sealed class ApercuEquipement : ControleDessine
{
    /// <summary>Ce que montre l'aperçu.</summary>
    public sealed record Donnees(
        Categorie? Categorie,
        string Code,
        string Nom,
        IReadOnlyList<(string Texte, Color Couleur)> Pastilles,
        IReadOnlyList<(string Texte, bool Fait)> Points,
        string? Echeance,
        string? DateEcheance);

    private Donnees _donnees = new(null, "", "", [], [], null, null);

    public ApercuEquipement()
    {
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Aperçu";
        Size = new Size(290, 480);
    }

    public void Afficher(Donnees donnees)
    {
        _donnees = donnees;
        AccessibleDescription = $"{donnees.Code} · {donnees.Nom}";
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Width, (int)E(440));

    protected override void Peindre(Graphics g)
    {
        var d = _donnees;
        var fondColonne = Theme.Sur(Theme.Surface, Theme.Fond, 0.4f);
        var tout = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(tout, E(18)))
        {
            using var fond = new SolidBrush(fondColonne);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Bordure, E(1));
            g.DrawPath(bord, chemin);
        }

        var marge = E(18);
        var x = marge;
        var largeur = Width - (2 * marge);
        Dessin.Texte(g, "APERÇU", Theme.PoliceGrasse(8.25f), Rectangle.Round(new RectangleF(x, E(16), largeur, E(20))), Theme.TexteSecondaire);

        // --- Carte : catégorie, médaille, nom, pastilles ---
        var hCarte = E(d.Pastilles.Count > 0 ? 250 : 222);
        var carte = new RectangleF(x, E(48), largeur, hCarte);
        var couleurCategorie = d.Categorie is { } cat ? Theme.CouleurCategorie(cat.CouleurEffective) : Theme.Accent;
        using (var chemin = Dessin.Arrondi(carte, E(18)))
        {
            using var fond = new LinearGradientBrush(carte, Theme.Sur(fondColonne, Theme.Accent, 0.12f), Theme.Sur(fondColonne, Theme.Fond, 0.3f), 70f);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Sur(fondColonne, Theme.Accent, 0.3f), E(1));
            g.DrawPath(bord, chemin);
        }

        var y = carte.Top + E(18);
        if (d.Categorie is { } categorie)
        {
            var police = Theme.PoliceGrasse(8.25f);
            var texte = TextRenderer.MeasureText(categorie.Nom, police).Width;
            var l = Math.Min(largeur - E(24), texte + E(40));
            var pilule = new RectangleF(carte.Left + ((carte.Width - l) / 2), y, l, E(26));
            using (var chemin = Dessin.Arrondi(pilule, pilule.Height / 2))
            using (var fond = new SolidBrush(Theme.Sur(fondColonne, couleurCategorie, 0.16f)))
            {
                g.FillPath(fond, chemin);
            }

            Icones.Dessiner(g, Icones.DeCategorie(categorie.IconeEffective), new RectangleF(pilule.Left + E(9), pilule.Top + E(5), E(16), E(16)), couleurCategorie);
            Dessin.Texte(g, categorie.Nom, police, Rectangle.Round(new RectangleF(pilule.Left + E(28), pilule.Top, pilule.Width - E(34), pilule.Height)), couleurCategorie);
        }

        // Médaille.
        y += E(38);
        var diametre = E(92);
        var medaille = new RectangleF(carte.Left + ((carte.Width - diametre) / 2), y, diametre, diametre);
        using (var ombre = new SolidBrush(Theme.Alpha(Color.Black, 0.25f)))
        {
            g.FillEllipse(ombre, RectangleF.Inflate(new RectangleF(medaille.X, medaille.Y + E(5), medaille.Width, medaille.Height), -E(2), -E(2)));
        }

        using (var metal = new LinearGradientBrush(medaille, Theme.MedailleClair, Theme.MedailleFonce, 55f))
        {
            g.FillEllipse(metal, medaille);
        }

        using (var anneau = new Pen(Theme.Alpha(Color.White, 0.45f), E(3)))
        {
            g.DrawEllipse(anneau, RectangleF.Inflate(medaille, -E(3), -E(3)));
        }

        var code = d.Code.Length > 0 ? d.Code : "?";
        var taille = 16f;
        while (taille > 8f && TextRenderer.MeasureText(code, Theme.PoliceCode(taille)).Width > diametre - E(14))
        {
            taille -= 1f;
        }

        Dessin.Texte(g, code, Theme.PoliceCode(taille), Rectangle.Round(medaille), Theme.TexteSurMedaille, centre: true);

        // Nom.
        y = medaille.Bottom + E(12);
        Dessin.Texte(g, d.Nom, Theme.PoliceTitre(12f), Rectangle.Round(new RectangleF(carte.Left + E(10), y, carte.Width - E(20), E(28))), Theme.Texte, centre: true);

        // Pastilles centrées.
        if (d.Pastilles.Count > 0)
        {
            y += E(36);
            var police = Theme.PoliceGrasse(7.5f);
            var largeurs = d.Pastilles.Select(p => TextRenderer.MeasureText(p.Texte, police).Width + E(18)).ToList();
            var px = carte.Left + ((carte.Width - (largeurs.Sum() + (E(6) * (largeurs.Count - 1)))) / 2);
            for (var i = 0; i < d.Pastilles.Count; i++)
            {
                var (texte, couleur) = d.Pastilles[i];
                var zone = new RectangleF(px, y, largeurs[i], E(22));
                using (var chemin = Dessin.Arrondi(zone, zone.Height / 2))
                using (var fond = new SolidBrush(Theme.Sur(fondColonne, couleur, 0.16f)))
                {
                    g.FillPath(fond, chemin);
                }

                Dessin.Texte(g, texte, police, Rectangle.Round(zone), couleur, centre: true);
                px += largeurs[i] + E(6);
            }
        }

        // --- Points renseignés ---
        y = carte.Bottom + E(18);
        foreach (var (texte, fait) in d.Points)
        {
            var rond = new RectangleF(x, y + E(3), E(20), E(20));
            using (var fond = new SolidBrush(fait ? Theme.Retour : Theme.Teinte(Theme.AccentClair, 0.10f)))
            {
                g.FillEllipse(fond, rond);
            }

            Icones.Dessiner(g, fait ? Icones.Coche : Icones.Moins, RectangleF.Inflate(rond, -E(5), -E(5)), fait ? Theme.TexteSurRetour : Theme.TexteSecondaire, 3f);
            Dessin.Texte(g, texte, Theme.Police(9.75f), Rectangle.Round(new RectangleF(x + E(30), y, largeur - E(30), E(26))), fait ? Theme.Texte : Theme.TexteSecondaire);
            y += E(30);
        }

        // --- Échéance, en bas ---
        if (d.Echeance is { } echeance && d.DateEcheance is { } date)
        {
            var zone = new RectangleF(x, Height - marge - E(48), largeur, E(48));
            using (var chemin = Dessin.Arrondi(zone, E(14)))
            {
                using var fond = new SolidBrush(Theme.Sur(fondColonne, Theme.Avertissement, 0.08f));
                g.FillPath(fond, chemin);
                using var bord = new Pen(Theme.Sur(fondColonne, Theme.Avertissement, 0.3f), E(1));
                g.DrawPath(bord, chemin);
            }

            var police = Theme.Police(9f);
            var gras = Theme.PoliceGrasse(9f);
            var debut = $"{echeance} le ";
            const TextFormatFlags Mesure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var l1 = TextRenderer.MeasureText(g, debut, police, Size.Empty, Mesure).Width;
            var l2 = TextRenderer.MeasureText(g, date, gras, Size.Empty, Mesure).Width;
            var tx = (int)(zone.Left + E(14));
            var ty = (int)zone.Top;
            var h = (int)zone.Height;
            TextRenderer.DrawText(g, debut, police, new Rectangle(tx, ty, l1 + 2, h), Theme.Texte, Mesure | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, date, gras, new Rectangle(tx + l1, ty, l2 + 2, h), Theme.Avertissement, Mesure | TextFormatFlags.VerticalCenter);
        }
    }
}
