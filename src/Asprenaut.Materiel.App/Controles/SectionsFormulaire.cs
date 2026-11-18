using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

// Briques des formulaires en sections (maquette « Ajouter un adhérent ») : sections numérotées, puces de choix,
// cartes d'autorisation avec interrupteur, avatar de l'en-tête, libellé « obligatoire ».

/// <summary>Libellé suivi (ou précédé) d'une étoile de couleur : « Prénom * », « * champs obligatoires ».</summary>
public sealed class LibelleObligatoire : Label
{
    private readonly bool _etoileDevant;

    public LibelleObligatoire(string texte, Font police, Color couleur, bool etoileDevant = false)
    {
        _etoileDevant = etoileDevant;
        Text = texte;
        Font = police;
        ForeColor = couleur;
        AutoSize = true;
        BackColor = Color.Transparent;
        UseMnemonic = false;
        Margin = Padding.Empty;
    }

    private bool _etoile = true;
    private bool _modifie;

    /// <summary>Étoile « obligatoire » (faux : libellé simple, qui peut seulement porter le point <see cref="Modifie"/>).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Etoile
    {
        get => _etoile;
        set
        {
            _etoile = value;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }

    /// <summary>
    /// Point turquoise après le libellé : la valeur diffère de celle enregistrée (« Modifier la sortie »). Sa place est toujours
    /// réservée, pour que le libellé ne change pas de largeur.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Modifie
    {
        get => _modifie;
        set
        {
            if (_modifie != value)
            {
                _modifie = value;
                Invalidate();
            }
        }
    }

    /// <summary>Réserve la place du point <see cref="Modifie"/>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AvecPoint { get; set; }

    private Font PoliceEtoile => Theme.PoliceGrasse(Font.SizeInPoints);

    private int LargeurEtoile => _etoile ? TextRenderer.MeasureText("*", PoliceEtoile, Size.Empty, TextFormatFlags.NoPadding).Width + (int)Dessin.Echelle(this, 4) : 0;

    private int LargeurPoint => AvecPoint ? (int)Dessin.Echelle(this, 15) : 0;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var texte = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(texte.Width + LargeurEtoile + LargeurPoint + 2, texte.Height + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var mesure = TextRenderer.MeasureText(Text, Font, Size.Empty, drapeaux);
        var xTexte = _etoileDevant ? LargeurEtoile : 0;
        var xEtoile = _etoileDevant ? 0 : mesure.Width + (int)Dessin.Echelle(this, 4);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Point(xTexte, 1), ForeColor, drapeaux);
        if (_etoile)
        {
            TextRenderer.DrawText(e.Graphics, "*", PoliceEtoile, new Point(xEtoile, 1), Theme.AccentClair, drapeaux);
        }

        if (_modifie)
        {
            // Point de 7 px avec un léger halo (maquette « Modifier la sortie »).
            Dessin.Qualite(e.Graphics);
            var x = mesure.Width + LargeurEtoile + Dessin.Echelle(this, 5);
            var y = 1 + (mesure.Height / 2f);
            var r = Dessin.Echelle(this, 3.5f);
            var halo = Dessin.Echelle(this, 5.5f);
            using (var pinceauHalo = new SolidBrush(Color.FromArgb(55, Theme.Accent)))
            {
                e.Graphics.FillEllipse(pinceauHalo, x + r - halo, y - halo, 2 * halo, 2 * halo);
            }

            using var pinceau = new SolidBrush(Theme.Accent);
            e.Graphics.FillEllipse(pinceau, x, y - r, 2 * r, 2 * r);
        }
    }
}

/// <summary>Rond teinté au bord pointillé, pictogramme au centre (en-tête d'un formulaire).</summary>
public sealed class AvatarEntete : ControleDessine
{
    private string _icone = "";

    public AvatarEntete()
    {
        Size = new Size(52, 52);
        TabStop = false; // Décoratif : un Control prend le focus par défaut (Tab s'y arrêtait).
    }

    private string _initiales = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Icone { get => _icone; set => Invalider(ref _icone, value); }

    /// <summary>Initiales (« CM ») : rond plein en dégradé à la place du pictogramme ; vide = pictogramme.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Initiales { get => _initiales; set => Invalider(ref _initiales, value); }

    private Color? _couleur;
    private Color? _texteSurCouleur;
    private bool _tuile;

    /// <summary>Couleur du rond (violet d'un moniteur) et du texte des initiales posé dessus ; null = turquoise de l'accent.</summary>
    public void Colorer(Color couleur, Color texteSurCouleur)
    {
        _couleur = couleur;
        _texteSurCouleur = texteSurCouleur;
        Invalidate();
    }

    /// <summary>Carré arrondi au bord plein plutôt que rond pointillé (en-tête de « Ajouter une classe »).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Tuile { get => _tuile; set => Invalider(ref _tuile, value); }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(E(1), E(1), Width - E(2) - 1, Height - E(2) - 1);
        var couleur = _couleur ?? Theme.Accent;
        if (_initiales.Length > 0)
        {
            var (clair, fonce) = _couleur is { } c ? (Theme.Sur(c, Theme.Texte, 0.25f), c) : (Theme.AccentClair, Theme.Retour);
            using var degrade = new LinearGradientBrush(r, clair, fonce, 45f);
            g.FillEllipse(degrade, r);
            Dessin.Texte(g, _initiales, Theme.PoliceGrasse(14f), Rectangle.Round(r), _texteSurCouleur ?? Theme.TexteSurAccent, centre: true);
            return;
        }

        using var forme = _tuile ? Dessin.Arrondi(r, r.Height * 0.3f) : new GraphicsPath();
        if (!_tuile)
        {
            forme.AddEllipse(r);
        }

        using (var fond = new SolidBrush(Theme.Teinte(couleur, _tuile ? 0.13f : 0.16f)))
        {
            g.FillPath(fond, forme);
        }

        using (var stylo = new Pen(Theme.Teinte(couleur, _tuile ? 0.4f : 0.55f), E(1.2f)) { DashStyle = _tuile ? DashStyle.Solid : DashStyle.Dash })
        {
            g.DrawPath(stylo, forme);
        }

        if (_icone.Length > 0)
        {
            var marge = _tuile ? 0.25f : 0.29f;
            Icones.Dessiner(g, _icone, RectangleF.Inflate(r, -Width * marge, -Height * marge), _couleur ?? Theme.AccentClair);
        }
    }
}

/// <summary>Petit carré arrondi avec le numéro d'une section (« 1 », « 2 »...).</summary>
public sealed class NumeroSection : ControleDessine
{
    private readonly string _numero;

    public NumeroSection(int numero)
    {
        _numero = numero.ToString(Libelles.Culture);
        TabStop = false; // Décoratif : un Control prend le focus par défaut (Tab s'y arrêtait).
        Size = new Size(24, 24);
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(r, E(7)))
        using (var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.2f)))
        {
            g.FillPath(fond, chemin);
        }

        Dessin.Texte(g, _numero, Theme.PoliceGrasse(9f), new Rectangle(0, 0, Width, Height), Theme.AccentClair, centre: true);
    }
}

/// <summary>
/// Section numérotée d'un formulaire : « 1 IDENTITÉ » (+ texte à droite, « non renseigné »), puis son contenu.
/// Se place dans une <see cref="FenetreFormulaire"/> par <see cref="FenetreFormulaire.Ajouter"/> ; sa hauteur suit son contenu.
/// </summary>
public sealed class SectionFormulaire : Carte
{
    private readonly TableLayoutPanel _contenu;
    private readonly Label _droite = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);

    public SectionFormulaire(int numero, string titre)
    {
        Padding = new Padding(16, 16, 16, 4);
        Margin = new Padding(0, 0, 0, 14);
        Rayon = 18;
        // Maquette : section un peu plus sombre que la fenêtre, bord discret.
        BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.3f);
        CouleurBordure = Theme.Bordure;
        AccessibleName = titre;
        // Sinon le TableLayoutPanel parent garde la hauteur actuelle au lieu de GetPreferredSize ; GrowAndShrink : une mesure
        // faite à une largeur provisoire étroite (textes à la ligne) ne doit pas rester.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _contenu = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        _contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_contenu);

        var badge = new NumeroSection(numero) { Margin = new Padding(0, 0, 10, 0) };
        var libelle = _titre = new LibelleObligatoire(titre.ToUpper(Libelles.Culture), Theme.PoliceGrasse(9.75f), Theme.Texte) { AvecPoint = true, Etoile = false };
        libelle.Margin = new Padding(0, 3, 0, 0);
        var gauche = Ui.Rangee(true, badge, libelle);
        gauche.Margin = Padding.Empty;
        _droite.Anchor = AnchorStyles.Right;
        _droite.Margin = Padding.Empty;
        var entete = _entete = Ui.Colonnes((gauche, SizeType.Percent, 100), (_droite, SizeType.AutoSize, 0));
        entete.AutoSize = true;
        entete.Margin = new Padding(0, 0, 0, 14);
        Ajouter(entete);

        _contenu.SizeChanged += (_, _) => Parent?.PerformLayout(this, nameof(Height));
    }

    private readonly LibelleObligatoire _titre;
    private readonly TableLayoutPanel _entete;
    private Pastille? _pastille;

    /// <summary>Pastille à droite du titre (« Toutes les sorties ») à la place du texte discret ; créée au premier appel.</summary>
    public void PastilleDroite(string texte, Color couleur)
    {
        var fond = Theme.Sur(BackColor, couleur, 0.14f);
        if (_pastille is null)
        {
            // Tailles calculées au dessin (E) : rien à mettre à l'échelle.
            _pastille = new Pastille(texte, fond, couleur, police: Theme.PoliceGrasse(8.25f), hauteur: 26)
            {
                Anchor = AnchorStyles.Right,
                Margin = Padding.Empty,
                TabStop = false,
            };
            _entete.Controls.Remove(_droite);
            _entete.Controls.Add(_pastille, 1, 0);
            return;
        }

        _pastille.Definir(texte, fond, couleur);
    }

    /// <summary>Point turquoise après le titre : un choix de la section diffère de celui enregistré.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Modifie
    {
        get => _titre.Modifie;
        set => _titre.Modifie = value;
    }

    /// <summary>Texte discret à droite du titre (« non renseigné », « Niveau 2 »).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TexteDroite
    {
        get => _droite.Text;
        set => _droite.Text = value;
    }

    /// <summary>Couleur du texte de droite.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CouleurDroite
    {
        get => _droite.ForeColor;
        set => _droite.ForeColor = value;
    }

    /// <summary>Ajoute une ligne pleine largeur.</summary>
    public T Ajouter<T>(T controle)
        where T : Control
    {
        FenetreFormulaire.AjouterLigne(_contenu, controle, SizeType.AutoSize, 0);
        return controle;
    }

    /// <summary>Champ pleine largeur, libellé au-dessus (« Nom * » : étoile).</summary>
    public T Champ<T>(string libelle, T controle, int hauteur = Theme.HauteurChamp)
        where T : Control
    {
        FenetreFormulaire.AjouterChamp(_contenu, libelle, controle, hauteur);
        return controle;
    }

    /// <summary>Champ pleine largeur sous un libellé déjà construit (<see cref="LibelleObligatoire.Modifie"/>).</summary>
    public T Champ<T>(Label libelle, T controle, int hauteur = Theme.HauteurChamp)
        where T : Control
    {
        FenetreFormulaire.AjouterChamp(_contenu, libelle, controle, hauteur);
        return controle;
    }

    /// <summary>Deux champs côte à côte (voir <see cref="FenetreFormulaire.DeuxChamps"/>).</summary>
    public TableLayoutPanel DeuxChamps(string libelle1, Control champ1, string libelle2, Control champ2, int hauteur = Theme.HauteurChamp) =>
        Ajouter(FenetreFormulaire.RangeeDeuxChamps(libelle1, champ1, libelle2, champ2, hauteur, 7));

    /// <summary>Deux champs côte à côte sous des libellés déjà construits.</summary>
    public TableLayoutPanel DeuxChamps(Label libelle1, Control champ1, Label libelle2, Control champ2, int hauteur = Theme.HauteurChamp) =>
        Ajouter(FenetreFormulaire.RangeeDeuxChamps(libelle1, champ1, libelle2, champ2, hauteur, 7));

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        var hauteur = _contenu.GetPreferredSize(new Size(Math.Max(1, largeur - Padding.Horizontal), 0)).Height;
        return new Size(largeur, hauteur + Padding.Vertical);
    }
}

/// <summary>
/// Puces à choix unique, rangées par groupes (« Classe » : Niveau 1, Niveau 2… Autonome), titre du groupe à gauche (facultatif),
/// détail facultatif sous le texte (« E1 / Initiateur »), retour à la ligne si la place manque. Un clic sur la puce choisie
/// la retire (aucun choix). Clavier : flèches, Espace. Un seul contrôle dessiné.
/// <see cref="Actions"/> : puces-raccourcis au contour turquoise (« Exemples ») qui ne restent pas choisies.
/// </summary>
public sealed class ChoixPuces : ControleDessine
{
    private const int LargeurTitre = 92;
    private const int HauteurPuce = 40;
    private const int HauteurPuceDetail = 44;
    private const int EcartPuces = 6;
    private const int EcartLignes = 10;

    private sealed record Puce(string Valeur, string Texte, string? Detail);

    private sealed record GroupePuces(string Titre, Color Couleur, Color TexteChoisi, List<Puce> Puces);

    private readonly List<GroupePuces> _groupes = [];
    private readonly List<(RectangleF Zone, Puce Puce, GroupePuces Groupe)> _zones = [];
    private readonly List<(RectangleF Zone, string Titre)> _titres = [];
    private readonly ToolTip _bulle = new();
    private string? _valeur;
    private int _survol = -1;
    private int _focus;
    private int _largeurPlacee = -1;
    private int _hauteur;

    public ChoixPuces()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = HauteurPuce;
        AutoSize = true;
    }

    /// <summary>Valeur choisie, ou null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Valeur
    {
        get => _valeur;
        set
        {
            if (_valeur == value)
            {
                return;
            }

            _valeur = value;
            Invalidate();
            ValeurChangee?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>La puce choisie a changé (clic, clavier ou code).</summary>
    public event EventHandler? ValeurChangee;

    /// <summary>Puces-raccourcis : un clic lève <see cref="PuceActivee"/> sans rien garder de choisi.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Actions { get; set; }

    /// <summary>Une puce a été cliquée en mode <see cref="Actions"/> (sa valeur).</summary>
    public event EventHandler<string>? PuceActivee;

    /// <summary>Ajoute un groupe de puces (valeur, texte affiché) ; titre vide = pas de colonne de titre.</summary>
    public void Groupe(string titre, IEnumerable<(string Valeur, string Texte)> puces, Color? couleur = null, Color? texteChoisi = null) =>
        AjouterGroupe(titre, puces.Select(p => new Puce(p.Valeur, p.Texte, null)), couleur, texteChoisi);

    /// <summary>Puces à deux lignes, sans titre de groupe : valeur en gras, détail dessous (« E1 » / « Initiateur »).</summary>
    public void GroupeDetaille(IEnumerable<(string Valeur, string Detail)> puces, Color couleur, Color texteChoisi) =>
        AjouterGroupe("", puces.Select(p => new Puce(p.Valeur, p.Valeur, p.Detail)), couleur, texteChoisi);

    private void AjouterGroupe(string titre, IEnumerable<Puce> puces, Color? couleur, Color? texteChoisi)
    {
        var liste = puces.ToList();
        if (liste.Count == 0)
        {
            return;
        }

        _groupes.Add(new GroupePuces(titre, couleur ?? Theme.Accent, texteChoisi ?? Theme.TexteSurAccent, liste));
        _largeurPlacee = -1;
        PerformLayout();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        Placer(largeur);
        return new Size(largeur, _hauteur);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Placer(Width);
        if (_hauteur > 0 && Height != _hauteur)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    private Font PolicePuce => Actions ? Theme.PoliceGrasse(9f) : Theme.PoliceGrasse(10f);

    private void Placer(int largeur)
    {
        if (largeur == _largeurPlacee || largeur <= 0)
        {
            return;
        }

        _largeurPlacee = largeur;
        _zones.Clear();
        _titres.Clear();
        var police = PolicePuce;
        var y = 0f;
        var derniere = 0f;
        foreach (var groupe in _groupes)
        {
            var gauche = groupe.Titre.Length > 0 ? E(LargeurTitre) : 0;
            var hauteur = E(groupe.Puces.Any(p => p.Detail is not null) ? HauteurPuceDetail : HauteurPuce);
            _titres.Add((new RectangleF(0, y, gauche, hauteur), groupe.Titre));
            var x = gauche;
            foreach (var puce in groupe.Puces)
            {
                var texte = TextRenderer.MeasureText(puce.Texte, police).Width;
                var detail = puce.Detail is null ? 0 : TextRenderer.MeasureText(puce.Detail, Theme.Police(7.5f)).Width;
                var l = Math.Max(E(Actions ? 40 : 56), Math.Max(texte, detail) + E(Actions ? 26 : 28));
                if (x > gauche && x + l > largeur)
                {
                    x = gauche;
                    y += hauteur + E(EcartPuces);
                }

                _zones.Add((new RectangleF(x, y, l, hauteur), puce, groupe));
                x += l + E(EcartPuces);
            }

            derniere = hauteur;
            y += hauteur + E(EcartLignes);
        }

        _hauteur = (int)Math.Ceiling(Math.Max(derniere, y - E(EcartLignes))) + 2;
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        Placer(Width);
        var police = PolicePuce;
        foreach (var (zone, titre) in _titres)
        {
            Dessin.Texte(g, titre, Theme.Police(9.75f), Rectangle.Round(zone), Theme.TexteSecondaire);
        }

        for (var i = 0; i < _zones.Count; i++)
        {
            var (zone, puce, groupe) = _zones[i];
            var choisie = !Actions && puce.Valeur == _valeur;
            var r = new RectangleF(zone.X + 0.5f, zone.Y + 0.5f, zone.Width - 1, zone.Height - 1);
            using var chemin = Dessin.Arrondi(r, Actions ? r.Height / 2 : E(10));
            var survol = i == _survol;
            var fondNormal = Actions ? (survol ? Theme.Teinte(Theme.Accent, 0.10f) : Theme.Sur(Theme.Surface, Theme.Fond, 0.3f)) : ChampSaisie.FondChamp;
            using (var fond = new SolidBrush(choisie ? groupe.Couleur : fondNormal))
            {
                g.FillPath(fond, chemin);
            }

            var focus = Focused && i == _focus;
            var bordure = choisie ? groupe.Couleur
                : focus ? Theme.AccentClair
                : survol ? (Actions ? Theme.AccentClair : Theme.Sur(ChampSaisie.FondChamp, groupe.Couleur, 0.6f))
                : Actions ? Theme.Sur(Theme.Surface, Theme.Accent, 0.3f) : Theme.BordureForte;
            using (var stylo = new Pen(bordure, focus ? E(2) : E(1)))
            {
                g.DrawPath(stylo, chemin);
            }

            var couleurTexte = choisie ? groupe.TexteChoisi : Actions ? Theme.AccentClair : Theme.Texte;
            if (puce.Detail is null)
            {
                Dessin.Texte(g, puce.Texte, police, Rectangle.Round(zone), couleurTexte, centre: true);
                continue;
            }

            // Deux lignes : « E1 » en gras, « Initiateur » en petit dessous.
            var petite = Theme.Police(7.5f);
            var hTexte = TextRenderer.MeasureText("E", police).Height;
            var hDetail = TextRenderer.MeasureText("I", petite).Height;
            var haut = zone.Y + ((zone.Height - hTexte - hDetail + E(2)) / 2);
            Dessin.Texte(g, puce.Texte, police, Rectangle.Round(new RectangleF(zone.X, haut, zone.Width, hTexte)), couleurTexte, centre: true);
            var couleurDetail = choisie ? Theme.Sur(groupe.Couleur, groupe.TexteChoisi, 0.75f) : Theme.TexteSecondaire;
            Dessin.Texte(g, puce.Detail, petite, Rectangle.Round(new RectangleF(zone.X, haut + hTexte - E(2), zone.Width, hDetail)), couleurDetail, centre: true);
        }
    }

    private int Sous(Point p) => _zones.FindIndex(z => z.Zone.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            Invalidate();
            var texte = i >= 0 && _zones[i].Puce.Texte != _zones[i].Puce.Valeur ? _zones[i].Puce.Valeur : null;
            _bulle.SetToolTip(this, texte); // « N2 » : la bulle donne le niveau entier.
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Sous(e.Location) is var i and >= 0)
        {
            _focus = i;
            Basculer(i);
        }
    }

    private void Basculer(int i)
    {
        var valeur = _zones[i].Puce.Valeur;
        if (Actions)
        {
            PuceActivee?.Invoke(this, valeur);
            return;
        }

        Valeur = valeur == _valeur ? null : valeur;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_zones.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                _focus = Math.Max(0, _focus - 1);
                break;
            case Keys.Right or Keys.Down:
                _focus = Math.Min(_zones.Count - 1, _focus + 1);
                break;
            case Keys.Space:
                Basculer(_focus);
                break;
            default:
                return;
        }

        e.Handled = true;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        var choisie = _zones.FindIndex(z => z.Puce.Valeur == _valeur);
        _focus = choisie >= 0 ? choisie : Math.Min(_focus, Math.Max(0, _zones.Count - 1));
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bulle.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Pastilles de couleur à choisir (une par clé), rondes de 44 px, 7 par ligne ; la choisie est entourée et cochée.
/// Clavier : flèches, Espace. Un seul contrôle dessiné.
/// </summary>
public sealed class ChoixCouleur : ControleDessine
{
    private const int Taille = 44;
    private const int Ecart = 8;
    private const int ParLigne = 7;

    private readonly List<(string Cle, string Nom, Color Couleur)> _couleurs;
    private readonly ToolTip _bulle = new();
    private string _valeur;
    private int _survol = -1;
    private int _focus;

    public ChoixCouleur(IEnumerable<(string Cle, string Nom, Color Couleur)> couleurs)
    {
        _couleurs = couleurs.ToList();
        _valeur = _couleurs.Count > 0 ? _couleurs[0].Cle : "";
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        var lignes = Math.Max(1, (_couleurs.Count + ParLigne - 1) / ParLigne);
        Size = new Size((ParLigne * Taille) + ((ParLigne - 1) * Ecart), (lignes * Taille) + ((lignes - 1) * Ecart));
    }

    /// <summary>Clé de la couleur choisie.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valeur
    {
        get => _valeur;
        set
        {
            if (_valeur == value || _couleurs.All(c => c.Cle != value))
            {
                return;
            }

            _valeur = value;
            Invalidate();
            ValeurChangee?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValeurChangee;

    private RectangleF Zone(int i) =>
        new(E((i % ParLigne) * (Taille + Ecart)), E((i / ParLigne) * (Taille + Ecart)), E(Taille), E(Taille));

    protected override void Peindre(Graphics g)
    {
        for (var i = 0; i < _couleurs.Count; i++)
        {
            var (cle, _, couleur) = _couleurs[i];
            var zone = Zone(i);
            var choisie = cle == _valeur;
            using (var pinceau = new SolidBrush(couleur))
            {
                g.FillEllipse(pinceau, RectangleF.Inflate(zone, -E(8), -E(8)));
            }

            var focus = Focused && i == _focus;
            if (choisie || focus || i == _survol)
            {
                var anneau = RectangleF.Inflate(zone, -E(1.5f), -E(1.5f));
                using var stylo = new Pen(choisie ? couleur : Theme.AccentClair, E(choisie || focus ? 2 : 1));
                g.DrawEllipse(stylo, anneau);
            }

            if (choisie)
            {
                Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(zone, -E(14), -E(14)), Theme.TexteSurAvatar);
            }
        }
    }

    private int Sous(Point p)
    {
        for (var i = 0; i < _couleurs.Count; i++)
        {
            if (Zone(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            Invalidate();
            _bulle.SetToolTip(this, i >= 0 ? _couleurs[i].Nom : null);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Sous(e.Location) is var i and >= 0)
        {
            _focus = i;
            Valeur = _couleurs[i].Cle;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var avant = _focus;
        _focus = e.KeyCode switch
        {
            Keys.Left => Math.Max(0, _focus - 1),
            Keys.Right => Math.Min(_couleurs.Count - 1, _focus + 1),
            Keys.Up => _focus >= ParLigne ? _focus - ParLigne : _focus,
            Keys.Down => Math.Min(_couleurs.Count - 1, _focus + ParLigne),
            _ => _focus,
        };
        if (e.KeyCode == Keys.Space && _focus < _couleurs.Count)
        {
            Valeur = _couleurs[_focus].Cle;
        }
        else if (avant == _focus)
        {
            return;
        }

        e.Handled = true;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _focus = Math.Max(0, _couleurs.FindIndex(c => c.Cle == _valeur));
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bulle.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Carré arrondi teinté avec un pictogramme ou un texte court (« O₂ »).</summary>
public sealed class TuileIcone : ControleDessine
{
    private readonly string? _icone;
    private readonly string? _texte;
    private readonly Color _couleur;

    public TuileIcone(string? icone, string? texte, Color couleur, int taille = 42)
    {
        _icone = icone;
        _texte = texte;
        _couleur = couleur;
        Size = new Size(taille, taille);
        TabStop = false; // Décoratif : un Control prend le focus par défaut (Tab s'y arrêtait).
    }

    /// <summary>Tuile grisée (carte indisponible).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Estompee { get => _estompee; set => Invalider(ref _estompee, value); }

    private bool _estompee;

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var couleur = _estompee ? Theme.TexteSecondaire : _couleur;
        using (var chemin = Dessin.Arrondi(r, E(10)))
        {
            using var fond = new SolidBrush(Theme.Teinte(couleur, 0.16f));
            g.FillPath(fond, chemin);
            using var stylo = new Pen(Theme.Teinte(couleur, 0.45f), E(1));
            g.DrawPath(stylo, chemin);
        }

        if (_icone is not null)
        {
            Icones.Dessiner(g, _icone, RectangleF.Inflate(r, -Width * 0.27f, -Height * 0.27f), couleur);
        }
        else if (_texte is not null)
        {
            Dessin.Texte(g, _texte, Theme.PoliceGrasse(10.5f), new Rectangle(0, 0, Width, Height), couleur, centre: true);
        }
    }
}

/// <summary>
/// Carte d'une autorisation : tuile colorée, titre, description, interrupteur à droite ; contenu facultatif dessous
/// (date du CACI quand il est contrôlé). Un clic sur la carte bascule l'interrupteur.
/// </summary>
public sealed class CarteAutorisation : Carte
{
    private readonly TableLayoutPanel _table;
    private readonly Label _description = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private Control? _dessous;
    private bool _dessousALEchelle;
    private readonly Color _couleur;

    public CarteAutorisation(TuileIcone tuile, string titre, string description, Interrupteur interrupteur, Color couleur)
    {
        Padding = new Padding(16, 12, 16, 12);
        Margin = new Padding(0, 0, 0, 12);
        Rayon = 14;
        BackColor = ChampSaisie.FondChamp;
        CouleurBordure = Theme.Bordure;
        Interrupteur = interrupteur;
        _couleur = couleur;
        interrupteur.CouleurActive = couleur;
        interrupteur.CheckedChanged += (_, _) => AppliquerEtat();
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var libelle = _titre = Ui.Libelle(titre, Theme.PoliceTitre(10.5f));
        libelle.Margin = new Padding(0, 0, 0, 2);
        _description.Text = description;
        _description.Margin = Padding.Empty;
        var textes = Ui.Pile(libelle, _description);
        textes.Anchor = AnchorStyles.Left;
        textes.Margin = new Padding(14, 0, 12, 0);
        tuile.Anchor = AnchorStyles.Left;
        interrupteur.Anchor = AnchorStyles.Right;
        interrupteur.Margin = Padding.Empty;

        _table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.Controls.Add(tuile, 0, 0);
        _table.Controls.Add(textes, 1, 0);
        _table.Controls.Add(interrupteur, 2, 0);
        Controls.Add(_table);

        foreach (var c in new Control[] { this, _table, textes, libelle, _description, tuile })
        {
            c.Click += (_, _) =>
            {
                if (_disponible)
                {
                    interrupteur.Checked = !interrupteur.Checked;
                }
            };
            c.Cursor = Cursors.Hand;
        }

        _tuile = tuile;

        _table.SizeChanged += (_, _) => Parent?.PerformLayout(this, nameof(Height));
        AppliquerEtat();
    }

    // Maquette : carte activée teintée de sa couleur (nitrox citron, CACI vert).
    private void AppliquerEtat()
    {
        BackColor = Interrupteur.Checked ? Theme.Sur(ChampSaisie.FondChamp, _couleur, 0.07f) : ChampSaisie.FondChamp;
        CouleurBordure = Interrupteur.Checked ? Theme.Sur(ChampSaisie.FondChamp, _couleur, 0.5f) : Theme.Bordure;
        Invalidate(true);
    }

    private readonly Label _titre;
    private readonly TuileIcone? _tuile;
    private bool _disponible = true;

    /// <summary>Titre de la carte (peut changer avec la saisie : « Prépare le Niveau 3 »).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Titre
    {
        get => _titre.Text;
        set => _titre.Text = value;
    }

    /// <summary>
    /// Faux : rien à choisir (« Choisis d'abord le brevet obtenu ») ; interrupteur coupé et grisé, textes estompés, clic sans effet.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Disponible
    {
        get => _disponible;
        set
        {
            if (_disponible == value)
            {
                return;
            }

            _disponible = value;
            Interrupteur.Enabled = value;
            _titre.ForeColor = value ? Theme.Texte : Theme.TexteSecondaire;
            if (_tuile is not null)
            {
                _tuile.Estompee = !value;
            }

            Cursor = value ? Cursors.Hand : Cursors.Default;
            Invalidate(true);
        }
    }

    /// <summary>Interrupteur de la carte.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Interrupteur Interrupteur { get; }

    /// <summary>Description sous le titre (peut changer avec l'interrupteur).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Description
    {
        get => _description.Text;
        set => _description.Text = value;
    }

    /// <summary>Contenu sous la carte, toute largeur, après un trait pointillé (masquable : <see cref="DessousVisible"/>).</summary>
    public T Dessous<T>(T controle)
        where T : Control
    {
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controle.Margin = new Padding(0, 18, 0, 0);
        controle.Dock = DockStyle.Fill;
        _dessous = controle;
        DessousVisible = true;
        return controle;
    }

    /// <summary>
    /// Montre ou cache le contenu de <see cref="Dessous"/>. Il est retiré de la carte plutôt que masqué : masqué, il comptait
    /// encore dans la hauteur de la section (taille préférée gardée en cache).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DessousVisible
    {
        get => _dessous is not null && _table.Contains(_dessous);
        set
        {
            if (_dessous is null || value == DessousVisible)
            {
                return;
            }

            if (value)
            {
                // Retiré avant l'affichage, il a manqué la mise à l'échelle de la fenêtre : une seule fois, ici.
                if (_table.IsHandleCreated && !_dessousALEchelle)
                {
                    Dessin.MettreALEchelle(_dessous, this);
                    _dessousALEchelle = true;
                }

                _table.Controls.Add(_dessous, 0, 1);
                _table.SetColumnSpan(_dessous, 3);
            }
            else
            {
                _dessousALEchelle |= _table.IsHandleCreated; // Déjà affiché : déjà à l'échelle.
                _table.Controls.Remove(_dessous);
            }
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        var hauteur = _table.GetPreferredSize(new Size(Math.Max(1, largeur - Padding.Horizontal), 0)).Height;
        return new Size(largeur, hauteur + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_dessous is null || !DessousVisible)
        {
            return;
        }

        // Trait pointillé entre la carte et son contenu (maquette : bas de la carte CACI).
        var y = _table.Top + _dessous.Top - (int)Dessin.Echelle(this, 9);
        using var stylo = new Pen(Theme.Sur(BackColor, _couleur, 0.35f), Dessin.Echelle(this, 1)) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawLine(stylo, 1, y, Width - 2, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _dessous is not null && !DessousVisible)
        {
            _dessous.Dispose(); // Retiré de la carte : personne d'autre ne le libère.
        }

        base.Dispose(disposing);
    }
}

/// <summary>Largeur à laquelle mesurer un contrôle de ces formulaires.</summary>
internal static class MesureFormulaire
{
    /// <summary>
    /// La largeur proposée ; sinon (0 ou illimitée) celle que lui laisse son parent. Repartir de <c>Width</c> donnait une
    /// largeur provisoire étroite : textes à la ligne, section bien trop haute.
    /// </summary>
    public static int Largeur(Control controle, Size proposee)
    {
        // Un TableLayoutPanel sonde ses lignes avec une largeur de 1 : à cette largeur les puces se mettent une par ligne
        // et la mesure gonflait toute la fenêtre (le surplus allait à la dernière section).
        if (proposee.Width is > 1 and < int.MaxValue)
        {
            return proposee.Width;
        }

        var parent = controle.Parent is { } p ? p.DisplayRectangle.Width - p.Padding.Horizontal - controle.Margin.Horizontal : 0;
        return Math.Max(controle.Width, parent);
    }
}
