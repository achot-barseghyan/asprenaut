using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Choix d'un résultat (Conforme, À surveiller, Refusé), coloré quand il est choisi. Formulaire « Ajouter une intervention »
/// de Révisions et de la fiche de suivi.
/// </summary>
public sealed class ChoixResultat : Button
{
    private readonly Color _couleur;
    private bool _selectionne;

    public ChoixResultat(string libelle, Color couleur)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseMnemonic = false;
        Cursor = Cursors.Hand;
        Text = libelle;
        _couleur = couleur;
    }

    /// <summary>Couleur d'un résultat : vert, jaune ou rouge.</summary>
    public static Color Couleur(ResultatControle resultat) => resultat switch
    {
        ResultatControle.Conforme => Theme.Retour,
        ResultatControle.ASurveiller => Theme.Avertissement,
        _ => Theme.Erreur,
    };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selectionne
    {
        get => _selectionne;
        set
        {
            _selectionne = value;
            AccessibleDescription = value ? "Choisi" : null;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Dessin.EffacerAvecParent(this, g);
        Dessin.Qualite(g);
        var fondParent = Dessin.CouleurParent(this);
        var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
        using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 12)))
        {
            using var fond = new SolidBrush(_selectionne ? Theme.Sur(fondParent, _couleur, 0.14f) : Theme.Sur(fondParent, Theme.Fond, 0.45f));
            g.FillPath(fond, chemin);
            using var bord = new Pen(_selectionne ? _couleur : Theme.Bordure, Dessin.Echelle(this, _selectionne ? 1.5f : 1));
            g.DrawPath(bord, chemin);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
                using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), Dessin.Echelle(this, 9));
                g.DrawPath(focus, interieur);
            }
        }

        Dessin.Texte(g, Text, _selectionne ? Theme.PoliceGrasse(9.75f) : Theme.PoliceTitre(9.75f), ClientRectangle, _selectionne ? _couleur : Theme.TexteDoux, centre: true);
    }
}
