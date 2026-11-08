using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Données affichées par une ligne de <see cref="ListeMateriel"/>.</summary>
public sealed record ElementMateriel(
    long EquipementId,
    string Code,
    string Libelle,
    string Details,
    string? Alerte = null,
    Color? CouleurAlerte = null);

/// <summary>Liste verticale défilante de lignes de matériel, avec bouton d'action optionnel par ligne.</summary>
public sealed class ListeMateriel : FlowLayoutPanel
{
    private readonly Label _vide;

    public ListeMateriel(string texteVide)
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        BackColor = Theme.Fond;
        Margin = Padding.Empty;
        Padding = new Padding(0, 4, 0, 0);
        _vide = Ui.Libelle(texteVide, Theme.Police(11f), Theme.TexteSecondaire);
        _vide.Margin = new Padding(4, 12, 0, 0);
        Controls.Add(_vide);
        Natif.ScrollbarsSombres(this);
    }

    /// <summary>Remplace le contenu. <paramref name="libelleAction"/> null = pas de bouton.</summary>
    public void Afficher(IEnumerable<ElementMateriel> elements, string? libelleAction = null, Action<ElementMateriel>? action = null)
    {
        SuspendLayout();
        while (Controls.Count > 1)
        {
            var c = Controls[1];
            Controls.RemoveAt(1);
            c.Dispose();
        }

        var nombre = 0;
        foreach (var element in elements)
        {
            Controls.Add(new LigneMateriel(element, libelleAction, action) { Width = LargeurLigne() });
            nombre++;
        }

        _vide.Visible = nombre == 0;
        ResumeLayout(true);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        var largeur = LargeurLigne();
        foreach (Control c in Controls)
        {
            if (c is LigneMateriel)
            {
                c.Width = largeur;
            }
        }
    }

    private int LargeurLigne() => Math.Max(200, ClientSize.Width - Padding.Horizontal - 2);

    private sealed class LigneMateriel : Carte
    {
        public LigneMateriel(ElementMateriel element, string? libelleAction, Action<ElementMateriel>? action)
        {
            Height = 72;
            Margin = new Padding(0, 0, 0, 8);
            Padding = new Padding(16, 8, 12, 8);
            Rayon = Theme.RayonBouton;

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Surface, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titre = Ui.Libelle($"{element.Code}  ·  {element.Libelle}", Theme.PoliceTitre(11f));
            titre.AutoEllipsis = true;
            var details = Ui.Libelle(element.Details, Theme.Police(9.5f), Theme.TexteSecondaire);
            var pile = Ui.Pile(titre, details);
            if (element.Alerte is { } alerte)
            {
                pile.Controls.Add(Ui.Libelle(alerte, Theme.PoliceTitre(9.5f), element.CouleurAlerte ?? Theme.Avertissement));
                Height = 92;
            }

            table.Controls.Add(pile, 0, 0);
            if (libelleAction is not null && action is not null)
            {
                var bouton = new BoutonArrondi(libelleAction, StyleBouton.Secondaire) { Anchor = AnchorStyles.Right };
                bouton.Click += (_, _) => action(element);
                table.Controls.Add(bouton, 1, 0);
            }

            Controls.Add(table);
        }
    }
}
