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

    public static Label Titre(string texte) => Libelle(texte, Theme.PoliceTitre(20f));

    public static Label SousTitre(string texte) => Libelle(texte, Theme.Police(11f), Theme.TexteSecondaire);

    public static Label TitreSection(string texte) => Libelle(texte, Theme.PoliceTitre(12f), Theme.TexteSecondaireClair);

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

    /// <summary>En-tête d'écran : titre et sous-titre à gauche, contrôles à droite.</summary>
    public static TableLayoutPanel Entete(Control gauche, params Control[] droite)
    {
        var entete = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };
        entete.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entete.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gauche.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        entete.Controls.Add(gauche, 0, 0);
        var actions = Rangee(false, droite);
        actions.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        entete.Controls.Add(actions, 1, 0);
        return entete;
    }

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
