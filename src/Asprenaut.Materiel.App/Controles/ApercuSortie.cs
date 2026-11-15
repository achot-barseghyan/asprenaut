using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

// Briques de l'écran « Nouvelle sortie » (maquette) : étapes en haut à droite, aperçu de la sortie, note « Étape suivante ».

/// <summary>Étapes « 1 Sortie — 2 Départ du matériel — 3 Retour » dans une capsule ; l'étape active est éclairée.</summary>
public sealed class EtapesSortie : ControleDessine
{
    private static readonly string[] Etapes = ["Sortie", "Départ du matériel", "Retour"];
    private int _active = 1;

    public EtapesSortie()
    {
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = "Étape 1 sur 3 : sortie";
        Height = 50;
    }

    /// <summary>Étape éclairée (1 à 3).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Active
    {
        get => _active;
        set
        {
            Invalider(ref _active, Math.Clamp(value, 1, Etapes.Length));
            AccessibleName = $"Étape {_active} sur {Etapes.Length} : {Etapes[_active - 1].ToLower(Libelles.Culture)}";
        }
    }

    private static Font Police => Theme.PoliceGrasse(9.75f);

    private float LargeurEtape(int i) =>
        E(6 + 26 + 8 + 14) + TextRenderer.MeasureText(Etapes[i], Police, Size.Empty, TextFormatFlags.NoPadding).Width;

    private const int Separation = 16;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = E(6) * 2;
        for (var i = 0; i < Etapes.Length; i++)
        {
            largeur += LargeurEtape(i) + (i > 0 ? E(8 + Separation + 8) : 0);
        }

        return new Size((int)Math.Ceiling(largeur) + 2, (int)E(50));
    }

    protected override void Peindre(Graphics g)
    {
        var cadre = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var chemin = Dessin.Arrondi(cadre, E(16)))
        {
            using var fond = new SolidBrush(ChampSaisie.FondChamp);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Bordure, E(1));
            g.DrawPath(bord, chemin);
        }

        var x = E(6);
        var h = E(38);
        var y = (Height - h) / 2;
        for (var i = 0; i < Etapes.Length; i++)
        {
            if (i > 0)
            {
                using var trait = new Pen(Theme.BordureForte, E(1));
                g.DrawLine(trait, x + E(8), Height / 2f, x + E(8 + Separation), Height / 2f);
                x += E(8 + Separation + 8);
            }

            var active = i + 1 == _active;
            var zone = new RectangleF(x, y, LargeurEtape(i), h);
            if (active)
            {
                using var chemin = Dessin.Arrondi(zone, E(11));
                using var fond = new SolidBrush(Theme.Sur(ChampSaisie.FondChamp, Theme.Accent, 0.14f));
                g.FillPath(fond, chemin);
            }

            var numero = new RectangleF(zone.X + E(6), zone.Y + ((h - E(26)) / 2), E(26), E(26));
            using (var chemin = Dessin.Arrondi(numero, E(8)))
            using (var fond = new SolidBrush(active ? Theme.Accent : Theme.Sur(ChampSaisie.FondChamp, Theme.AccentClair, 0.08f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, (i + 1).ToString(Libelles.Culture), Theme.PoliceGrasse(9f), Rectangle.Round(numero), active ? Theme.TexteSurAccent : Theme.TexteSecondaire, centre: true);
            Dessin.Texte(g, Etapes[i], Police, Rectangle.Round(new RectangleF(numero.Right + E(8), zone.Y, zone.Right - numero.Right - E(8), h)),
                active ? Theme.Texte : Theme.TexteSecondaire);
            x = zone.Right;
        }
    }
}

/// <summary>
/// Aperçu de la sortie en cours de saisie : calendrier teinté du type, pastille du type, intitulé, puis une ligne par
/// information (pictogramme, texte avec une partie en gras ; en rouge ce qui manque). Tout est dessiné.
/// </summary>
public sealed class ApercuSortie : ControleDessine
{
    /// <summary>Ligne de l'aperçu : « Moniteur : » + <b>Pascal Durand</b> + suite ; <paramref name="Manquant"/> en rouge.</summary>
    public sealed record Ligne(string Icone, string Debut, string Gras, string Fin = "", bool Manquant = false);

    private const int Marge = 20;
    private const int HauteurLigne = 20;
    private const int EcartLignes = 10;

    private DateOnly? _date;
    private string _type = "";
    private Color _couleur = Theme.Accent;
    private string _intitule = "";
    private IReadOnlyList<Ligne> _lignes = [];

    public ApercuSortie()
    {
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.StaticText;
        Height = 220;
    }

    public void Afficher(DateOnly? date, string type, Color couleur, string intitule, IReadOnlyList<Ligne> lignes)
    {
        var hauteurAvant = HauteurVoulue;
        _date = date;
        _type = type;
        _couleur = couleur;
        _intitule = intitule.Trim();
        _lignes = lignes;
        AccessibleName = $"Aperçu : {(_intitule.Length > 0 ? _intitule : "intitulé à saisir")}, {type}";
        AccessibleDescription = string.Join(". ", lignes.Select(l => $"{l.Debut}{l.Gras}{l.Fin}"));
        if (HauteurVoulue != hauteurAvant)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }

        Invalidate();
    }

    private int HauteurVoulue => (int)Math.Ceiling(E(Marge + 66 + 18) + (_lignes.Count * E(HauteurLigne + EcartLignes)) - E(EcartLignes) + E(Marge)) + 1;

    public override Size GetPreferredSize(Size proposedSize) => new(MesureFormulaire.Largeur(this, proposedSize), HauteurVoulue);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Height != HauteurVoulue)
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    protected override void Peindre(Graphics g)
    {
        var carte = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var chemin = Dessin.Arrondi(carte, E(22)))
        {
            using var fond = new SolidBrush(Theme.Surface);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.BordureForte, E(1));
            g.DrawPath(bord, chemin);
        }

        // Calendrier : 58 × 66, teinte du type.
        var m = E(Marge);
        var cal = new RectangleF(m, m, E(58), E(66));
        using (var chemin = Dessin.Arrondi(cal, E(15)))
        {
            using var fond = new SolidBrush(Theme.Teinte(_couleur, 0.12f));
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Teinte(_couleur, 0.35f), E(1));
            g.DrawPath(bord, chemin);
        }

        var jour = _date is { } d ? d.Day.ToString("00", Libelles.Culture) : "--";
        var mois = _date is { } dm ? dm.ToString("MMM", Libelles.Culture).TrimEnd('.').ToUpper(Libelles.Culture) : "";
        Dessin.Texte(g, jour, Theme.PoliceGrasse(17f), Rectangle.Round(new RectangleF(cal.X, cal.Y + E(9), cal.Width, E(28))), Theme.Texte, centre: true);
        Dessin.Texte(g, mois, Theme.PoliceGrasse(8.25f), Rectangle.Round(new RectangleF(cal.X, cal.Y + E(39), cal.Width, E(16))), _couleur, centre: true);

        // Pastille du type puis intitulé (deux lignes au plus).
        var x = cal.Right + E(14);
        var largeur = Width - x - m;
        var police = Theme.PoliceGrasse(8.25f);
        var texteType = _type.ToUpper(Libelles.Culture);
        var lPastille = Math.Min(largeur, TextRenderer.MeasureText(texteType, police, Size.Empty, TextFormatFlags.NoPadding).Width + E(18));
        var pastille = new RectangleF(x, cal.Y + E(2), lPastille, E(22));
        using (var chemin = Dessin.Arrondi(pastille, pastille.Height / 2))
        using (var fond = new SolidBrush(Theme.Teinte(_couleur, 0.16f)))
        {
            g.FillPath(fond, chemin);
        }

        Dessin.Texte(g, texteType, police, Rectangle.Round(pastille), _couleur, centre: true);
        var titre = _intitule.Length > 0 ? _intitule : "Intitulé à saisir";
        TextRenderer.DrawText(g, titre, Theme.PoliceGrasse(12f), Rectangle.Round(new RectangleF(x, pastille.Bottom + E(4), largeur, cal.Bottom - pastille.Bottom + E(6))),
            _intitule.Length > 0 ? Theme.Texte : Theme.TexteSecondaire,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Lignes : pictogramme puis texte, la partie importante en gras.
        var y = cal.Bottom + E(18);
        var normale = Theme.Police(9.75f);
        var grasse = Theme.PoliceGrasse(9.75f);
        const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        foreach (var ligne in _lignes)
        {
            var couleur = ligne.Manquant ? Theme.Erreur : Theme.TexteSecondaireClair;
            Icones.Dessiner(g, ligne.Icone, new RectangleF(m, y + E(2), E(16), E(16)), ligne.Manquant ? Theme.Erreur : Theme.TexteSecondaire);
            var xt = (int)(m + E(26));
            var fin = (int)(Width - m);
            foreach (var (texte, p, c) in new[] { (ligne.Debut, normale, couleur), (ligne.Gras, grasse, ligne.Manquant ? Theme.Erreur : Theme.Texte), (ligne.Fin, normale, couleur) })
            {
                if (texte.Length == 0 || xt >= fin)
                {
                    continue;
                }

                // Mesure sans EndEllipsis : avec une zone vide, il ramenait la largeur à presque rien (« R… »).
                var l = TextRenderer.MeasureText(g, texte, p, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                TextRenderer.DrawText(g, texte, p, new Rectangle(xt, (int)y, Math.Min(l + 4, fin - xt), (int)E(HauteurLigne)), c, drapeaux);
                xt += l;
            }

            y += E(HauteurLigne + EcartLignes);
        }
    }
}

/// <summary>Note teintée de vert : pictogramme, titre en gras et explication à la ligne (« Étape suivante »).</summary>
public sealed class NoteEtape : ControleDessine
{
    private readonly string _icone;
    private readonly string _titre;
    private readonly string _texte;

    public NoteEtape(string icone, string titre, string texte)
    {
        _icone = icone;
        _titre = titre;
        _texte = texte;
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = $"{titre} : {texte}";
        Height = 80;
    }

    private const TextFormatFlags Drapeaux = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;

    private int LargeurTexte(int largeur) => Math.Max(40, (int)(largeur - E(14 + 20 + 12 + 14)));

    private int HauteurPour(int largeur)
    {
        var titre = TextRenderer.MeasureText(_titre, Theme.PoliceGrasse(9.75f), Size.Empty, TextFormatFlags.NoPadding).Height;
        var texte = TextRenderer.MeasureText(_texte, Theme.Police(9.75f), new Size(LargeurTexte(largeur), 0), Drapeaux).Height;
        return (int)Math.Ceiling(E(14) + titre + E(2) + texte + E(14));
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var largeur = MesureFormulaire.Largeur(this, proposedSize);
        return new Size(largeur, HauteurPour(largeur));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width > 1 && Height != HauteurPour(Width))
        {
            Parent?.PerformLayout(this, nameof(Height));
        }
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var chemin = Dessin.Arrondi(r, E(16)))
        {
            using var fond = new SolidBrush(Theme.Teinte(Theme.Retour, 0.07f));
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Teinte(Theme.Retour, 0.35f), E(1));
            g.DrawPath(bord, chemin);
        }

        Icones.Dessiner(g, _icone, new RectangleF(E(14), E(15), E(20), E(20)), Theme.Retour);
        var x = (int)E(14 + 20 + 12);
        var policeTitre = Theme.PoliceGrasse(9.75f);
        var hTitre = TextRenderer.MeasureText(_titre, policeTitre, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, _titre, policeTitre, new Point(x, (int)E(14)), Theme.Texte, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _texte, Theme.Police(9.75f), new Rectangle(x, (int)(E(14) + hTitre + E(2)), LargeurTexte(Width), Height), Theme.TexteSecondaireClair, Drapeaux);
    }
}
