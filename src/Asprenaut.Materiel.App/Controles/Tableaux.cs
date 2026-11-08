using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Dessin de cellules particulières des tableaux (pastilles de statut, pictogramme + texte).</summary>
public static class Tableaux
{
    /// <summary>En-têtes en majuscules, comme la maquette.</summary>
    public static void AjouterColonne(DataGridView grille, string nom, string titre, float poids = 100)
    {
        var i = grille.Columns.Add(nom, titre.ToUpper(Core.Modeles.Libelles.Culture));
        grille.Columns[i].FillWeight = poids;
        grille.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    /// <summary>Pastille arrondie avec point coloré, alignée à gauche dans la cellule.</summary>
    public static void PeindrePastille(DataGridViewCellPaintingEventArgs e, string texte, Color couleur, Color? couleurTexte = null)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selection);
        Dessin.Qualite(g);
        var police = Theme.PoliceTitre(8.5f);
        var fond = selection ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
        var tTexte = TextRenderer.MeasureText(g, texte, police, Size.Empty, TextFormatFlags.NoPadding);
        var hauteur = 28f * g.DpiY / 96f;
        var point = 7f * g.DpiY / 96f;
        var largeur = tTexte.Width + point + (32f * g.DpiX / 96f);
        var r = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - hauteur) / 2), largeur, hauteur);
        using (var chemin = Dessin.Arrondi(r, hauteur / 2))
        using (var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.14f)))
        {
            g.FillPath(pinceau, chemin);
        }

        var x = r.Left + (12f * g.DpiX / 96f);
        using (var pinceauPoint = new SolidBrush(couleur))
        {
            g.FillEllipse(pinceauPoint, x, r.Top + ((hauteur - point) / 2), point, point);
        }

        x += point + (7f * g.DpiX / 96f);
        Dessin.Texte(g, texte, police, Rectangle.Round(new RectangleF(x, r.Top, r.Right - x, hauteur)), couleurTexte ?? couleur);
        e.Handled = true;
    }

    /// <summary>Pictogramme dans un carré teinté, puis texte (première colonne de l'inventaire).</summary>
    public static void PeindreIconeTexte(DataGridViewCellPaintingEventArgs e, string icone, string texte)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selection);
        Dessin.Qualite(g);
        var cote = 36f * g.DpiX / 96f;
        var boite = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - cote) / 2), cote, cote);
        using (var chemin = Dessin.Arrondi(boite, cote * 0.3f))
        using (var pinceau = new SolidBrush(Theme.FondAccent))
        {
            g.FillPath(pinceau, chemin);
        }

        Icones.Dessiner(g, icone, RectangleF.Inflate(boite, -cote * 0.25f, -cote * 0.25f), Theme.AccentClair);
        var gauche = (int)(boite.Right + (12f * g.DpiX / 96f));
        Dessin.Texte(g, texte, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(gauche, e.CellBounds.Top, e.CellBounds.Right - 4, e.CellBounds.Bottom), Theme.Texte);
        e.Handled = true;
    }
}
