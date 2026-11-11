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

    /// <summary>
    /// Bouton dessiné à droite de la cellule (« Enregistrer », « Modifier »). Le clic est géré par CellClick de la grille ;
    /// la colonne entière est la cible, assez haute (lignes de 60 px) pour un doigt ou une souris peu précise.
    /// </summary>
    public static void PeindreBouton(DataGridViewCellPaintingEventArgs e, string texte)
    {
        if (e.Graphics is not { } g)
        {
            return;
        }

        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        Dessin.Qualite(g);
        var hb = 40f * g.DpiY / 96f;
        var lb = Math.Min(e.CellBounds.Width - 10, 104f * g.DpiX / 96f);
        var r = new RectangleF(e.CellBounds.Right - lb - 6, e.CellBounds.Top + ((e.CellBounds.Height - hb) / 2), lb, hb);
        using (var chemin = Dessin.Arrondi(r, 12f * g.DpiX / 96f))
        using (var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.12f)))
        using (var bord = new Pen(Theme.Teinte(Theme.Accent, 0.30f)))
        {
            g.FillPath(fond, chemin);
            g.DrawPath(bord, chemin);
        }

        Dessin.Texte(g, texte, Theme.PoliceTitre(9f), Rectangle.Round(r), Theme.AccentClair, centre: true);
        e.Handled = true;
    }

    /// <summary>
    /// Zone du titre dans une cellule « pictogramme + deux lignes » (sa ligne du haut) : sert au dessin et à savoir
    /// si un clic tombe sur le nom souligné.
    /// </summary>
    public static Rectangle ZoneTitre(Graphics g, Rectangle cellule, Padding padding, string titre)
    {
        var sx = g.DpiX / 96f;
        var gauche = (int)(cellule.Left + padding.Left + (48f * sx));
        var t = TextRenderer.MeasureText(g, titre, Theme.PoliceTitre(10.5f), Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var s = TextRenderer.MeasureText(g, "Ag", Theme.Police(9f), Size.Empty, TextFormatFlags.NoPadding);
        var haut = cellule.Top + ((cellule.Height - t.Height - s.Height - (int)(2 * sx)) / 2);
        return new Rectangle(gauche, haut, Math.Min(t.Width, cellule.Right - 4 - gauche), t.Height);
    }

    /// <summary>
    /// Pictogramme dans un carré teinté, puis le nom souligné en pointillé (il ouvre la fiche) et une ligne
    /// secondaire (« Scubapro MK25 · Détendeurs »), comme la maquette.
    /// </summary>
    public static void PeindreIconeDeuxLignes(DataGridViewCellPaintingEventArgs e, string icone, string titre, string sousTitre, bool barre = false)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selection);
        Dessin.Qualite(g);
        var sx = g.DpiX / 96f;
        var cote = 36f * sx;
        var boite = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - cote) / 2), cote, cote);
        using (var chemin = Dessin.Arrondi(boite, cote * 0.3f))
        using (var pinceau = new SolidBrush(Theme.FondAccent))
        {
            g.FillPath(pinceau, chemin);
        }

        Icones.Dessiner(g, icone, RectangleF.Inflate(boite, -cote * 0.25f, -cote * 0.25f), Theme.AccentClair);
        var zone = ZoneTitre(g, e.CellBounds, e.CellStyle.Padding, titre);
        var droite = e.CellBounds.Right - 4;
        var couleurTitre = barre ? Theme.TexteSecondaire : Theme.Texte;
        TextRenderer.DrawText(g, titre, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(zone.Left, zone.Top, droite, zone.Bottom), couleurTitre,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        var y = zone.Bottom;
        if (barre)
        {
            using var trait = new Pen(couleurTitre, Math.Max(1f, sx));
            g.DrawLine(trait, zone.Left, zone.Top + (zone.Height / 2f), zone.Right, zone.Top + (zone.Height / 2f));
        }
        else
        {
            using var pointille = new Pen(Theme.Teinte(Theme.AccentClair, 0.45f), Math.Max(1f, sx)) { DashPattern = [3f, 2f] };
            g.DrawLine(pointille, zone.Left, y, zone.Right, y);
        }

        if (sousTitre.Length > 0)
        {
            var police = Theme.Police(9f);
            var h = TextRenderer.MeasureText(g, "Ag", police, Size.Empty, TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, sousTitre, police, Rectangle.FromLTRB(zone.Left, y + (int)(2 * sx), droite, y + (int)(2 * sx) + h), Theme.TexteSecondaire,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Compteur d'utilisation (colonne « Utilisation ») : « 64 / 80 utilisations », pastille « SEUIL MODÈLE » ou
    /// « SEUIL ÉQUIPEMENT » si le seuil n'est pas celui de la catégorie, puis une barre fine (jaune à 90 %, rouge au seuil).
    /// </summary>
    public static void PeindreUtilisation(DataGridViewCellPaintingEventArgs e, string texte, double part, Color couleur, Color couleurTexte, string? badge, bool barreVisible)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selection);
        Dessin.Qualite(g);
        var sx = g.DpiX / 96f;
        var fond = selection ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
        var police = Theme.PoliceTitre(9.5f);
        var drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var tTexte = TextRenderer.MeasureText(g, texte, police, Size.Empty, drapeaux);
        var hBarre = barreVisible ? 6f * sx : 0;
        var ecart = barreVisible ? 7f * sx : 0;
        var gauche = e.CellBounds.Left + e.CellStyle.Padding.Left;
        var droite = e.CellBounds.Right - (12f * sx);
        var haut = e.CellBounds.Top + ((e.CellBounds.Height - tTexte.Height - ecart - hBarre) / 2);
        TextRenderer.DrawText(g, texte, police, new Point(gauche, (int)haut), couleurTexte, drapeaux);

        if (badge is not null)
        {
            var pb = Theme.PoliceGrasse(7f);
            var tb = TextRenderer.MeasureText(g, badge, pb, Size.Empty, drapeaux);
            var rb = new RectangleF(gauche + tTexte.Width + (6f * sx), haut + ((tTexte.Height - tb.Height - (4f * sx)) / 2), tb.Width + (12f * sx), tb.Height + (4f * sx));
            if (rb.Right <= e.CellBounds.Right - 2)
            {
                using (var chemin = Dessin.Arrondi(rb, rb.Height / 2))
                using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Avertissement, 0.16f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                TextRenderer.DrawText(g, badge, pb, Rectangle.Round(rb), Theme.Avertissement, drapeaux | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        if (barreVisible)
        {
            var piste = new RectangleF(gauche, haut + tTexte.Height + ecart, Math.Max(0, droite - gauche), hBarre);
            using (var chemin = Dessin.Arrondi(piste, hBarre / 2))
            using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.AccentClair, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            var plein = (float)(piste.Width * Math.Clamp(part, 0, 1));
            if (plein >= 1)
            {
                using var chemin = Dessin.Arrondi(new RectangleF(piste.Left, piste.Top, Math.Max(plein, hBarre), hBarre), hBarre / 2);
                using var pinceau = new SolidBrush(couleur);
                g.FillPath(pinceau, chemin);
            }
        }

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
