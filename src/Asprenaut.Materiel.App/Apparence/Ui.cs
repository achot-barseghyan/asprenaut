namespace Asprenaut.Materiel.App.Apparence;

/// <summary>Fabriques de contrôles simples déjà thémés (libellés, conteneurs).</summary>
public static class Ui
{
    public static Label Libelle(string texte, Font? police = null, Color? couleur = null) => new()
    {
        Text = texte,
        AutoSize = true,
        Font = police ?? Theme.Police(),
        ForeColor = couleur ?? Theme.Texte,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 4),
        UseMnemonic = false,
    };

    /// <summary>Titre d'écran (32 px gras).</summary>
    public static Label Titre(string texte) => Libelle(texte, Theme.PoliceGrasse(24f));

    public static Label SousTitre(string texte) => Libelle(texte, Theme.Police(11.25f), Theme.TexteSecondaireClair);

    /// <summary>Titre de section (20 px semi-gras).</summary>
    public static Label TitreSection(string texte)
    {
        var l = Libelle(texte, Theme.PoliceTitre(15f));
        l.Margin = new Padding(0, 0, 0, 12);
        return l;
    }

    /// <summary>Titre de carte (17 px semi-gras).</summary>
    public static Label TitreCarte(string texte) => Libelle(texte, Theme.PoliceTitre(12.75f));

    /// <summary>Libellé de champ de formulaire.</summary>
    public static Label LibelleChamp(string texte)
    {
        var l = Libelle(texte, Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        l.Margin = new Padding(0, 0, 0, 8);
        return l;
    }

    /// <summary>Colonne verticale (TableLayoutPanel à une colonne), remplissant son parent.</summary>
    public static TableLayoutPanel Colonne(params (Control Controle, SizeType Type, float Valeur)[] lignes)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = lignes.Length,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < lignes.Length; i++)
        {
            var (controle, type, valeur) = lignes[i];
            table.RowStyles.Add(new RowStyle(type, valeur));
            controle.Dock = DockStyle.Fill;
            table.Controls.Add(controle, 0, i);
        }

        return table;
    }

    /// <summary>Rangée de colonnes (TableLayoutPanel à une ligne), remplissant son parent.</summary>
    public static TableLayoutPanel Colonnes(params (Control Controle, SizeType Type, float Valeur)[] colonnes)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = colonnes.Length,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < colonnes.Length; i++)
        {
            var (controle, type, valeur) = colonnes[i];
            table.ColumnStyles.Add(new ColumnStyle(type, valeur));
            controle.Dock = DockStyle.Fill;
            table.Controls.Add(controle, i, 0);
        }

        return table;
    }

    /// <summary>Rangée horizontale de contrôles (FlowLayoutPanel), de gauche à droite ou de droite à gauche.</summary>
    public static FlowLayoutPanel Rangee(bool versLaDroite = true, params Control[] controles)
    {
        var rangee = new FlowLayoutPanel
        {
            FlowDirection = versLaDroite ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        rangee.Controls.AddRange(controles);
        return rangee;
    }

    /// <summary>
    /// En-tête : contenu à gauche, contrôles à droite (centrés verticalement). Si la place manque (écran 4/3, menu
    /// déplié), les contrôles passent sous le titre et vont à la ligne au lieu de sortir de l'écran (<see cref="EnTeteEcran"/>).
    /// </summary>
    public static EnTeteEcran Entete(Control gauche, params Control[] droite) => new(gauche, Rangee(false, droite));

    /// <summary>Pile verticale auto-dimensionnée (titre + sous-titre par exemple).</summary>
    public static FlowLayoutPanel Pile(params Control[] controles)
    {
        var pile = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        pile.Controls.AddRange(controles);
        return pile;
    }
}

/// <summary>
/// En-tête d'écran adaptatif : titre à gauche et boutons à droite sur une ligne quand tout tient ; sinon boutons sous le
/// titre, alignés à droite et répartis sur plusieurs lignes. Sa hauteur suit (ligne AutoSize d'un TableLayoutPanel).
/// </summary>
public sealed class EnTeteEcran : Panel
{
    private readonly Control _gauche;
    private readonly FlowLayoutPanel _actions;
    private int _hauteurPosee = -1;

    public EnTeteEcran(Control gauche, FlowLayoutPanel actions)
    {
        _gauche = gauche;
        _actions = actions;
        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 0, 20);
        Padding = Padding.Empty;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;

        // Les boutons vont à la ligne dans la largeur qu'on leur donne : la rangée n'est plus auto-dimensionnée.
        actions.AutoSize = false;
        actions.WrapContents = true;
        gauche.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        actions.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        Controls.Add(gauche);
        Controls.Add(actions);
    }

    private readonly record struct Disposition(Rectangle Gauche, Rectangle Actions, int Hauteur);

    private Disposition Calculer(int largeur)
    {
        largeur = Math.Max(1, largeur);
        var pg = _gauche.GetPreferredSize(Size.Empty);
        if (_actions.Controls.Count == 0)
        {
            return new Disposition(new Rectangle(Point.Empty, pg), Rectangle.Empty, pg.Height);
        }

        var pa = _actions.GetPreferredSize(new Size(100_000, 0)); // Tous les boutons sur une ligne.
        var ecart = (int)Dessin.Echelle(this, 24);
        if (pg.Width + ecart + pa.Width <= largeur)
        {
            var h = Math.Max(pg.Height, pa.Height);
            return new Disposition(
                new Rectangle(0, (h - pg.Height) / 2, pg.Width, pg.Height),
                new Rectangle(largeur - pa.Width, (h - pa.Height) / 2, pa.Width, pa.Height),
                h);
        }

        // Trop étroit : boutons sous le titre, sur toute la largeur (alignés à droite, à la ligne si besoin).
        var dessous = _actions.GetPreferredSize(new Size(largeur, 0));
        var haut = pg.Height + (int)Dessin.Echelle(this, 14);
        return new Disposition(
            new Rectangle(0, 0, Math.Min(pg.Width, largeur), pg.Height),
            new Rectangle(0, haut, largeur, dessous.Height),
            haut + dessous.Height);
    }

    /// <summary>Un TableLayoutPanel sonde parfois avec une largeur de 0 ou 1 : on prend alors la largeur actuelle.</summary>
    private int LargeurDeReference(int proposee) =>
        proposee > 1 && proposee < 100_000 ? proposee
        : Width > 1 ? Width
        : Parent?.ClientSize.Width ?? 1;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = LargeurDeReference(proposedSize.Width);
        return new Size(largeur, Calculer(largeur).Hauteur);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Placement à la main, sans base.OnLayout : le moteur par défaut redonnerait à la rangée sa taille sur une ligne.
        var d = Calculer(ClientSize.Width);
        _gauche.Bounds = d.Gauche;
        _actions.Bounds = d.Actions;

        // Hauteur changée (boutons passés sous le titre, ou revenus à côté) : le parent refait sa mise en page, en différé.
        if (d.Hauteur != Height && d.Hauteur != _hauteurPosee && Parent is { } parent)
        {
            _hauteurPosee = d.Hauteur;
            if (IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                    {
                        parent.PerformLayout();
                    }
                });
            }
            else
            {
                parent.PerformLayout();
            }
        }
        else if (d.Hauteur == Height)
        {
            _hauteurPosee = -1;
        }
    }
}
