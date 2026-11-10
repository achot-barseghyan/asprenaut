using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Responsable d'une sortie : « Moniteur | Classe » puis la liste correspondante (Nouvelle sortie, Modifier la sortie).
/// Le choix fait dans chaque liste est gardé quand on bascule de l'une à l'autre.
/// </summary>
public sealed class ChoixResponsable : Panel
{
    private readonly Bascule _genre = new("Moniteur", "Classe") { Compact = true };
    private readonly ListeDeroulante _liste = new() { AccessibleName = "Moniteur responsable" };
    private readonly Dictionary<GenreResponsable, long?> _choix = [];
    private IReadOnlyList<Moniteur> _responsables = [];
    private bool _remplissage;

    public ChoixResponsable()
    {
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        _genre.AccessibleName = "Type de responsable";
        Controls.Add(_genre);
        Controls.Add(_liste);
        _genre.Changement += (_, _) => AfficherListe();
        _liste.SelectedIndexChanged += (_, _) =>
        {
            if (!_remplissage)
            {
                _choix[Genre] = ResponsableId;
            }
        };
        Height = Theme.HauteurChamp;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GenreResponsable Genre => _genre.SelectedIndex == 1 ? GenreResponsable.Classe : GenreResponsable.Moniteur;

    /// <summary>Responsable choisi dans la liste affichée (null si la liste est vide).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? ResponsableId => (_liste.SelectedItem as Choix<long>)?.Valeur;

    /// <summary>Remplit les deux listes ; <paramref name="choisi"/> choisit le genre et l'élément affichés.</summary>
    public void Remplir(IReadOnlyList<Moniteur> responsables, long? choisi)
    {
        _responsables = responsables;
        _choix.Clear();
        var r = responsables.FirstOrDefault(m => m.Id == choisi);
        if (r is not null)
        {
            _choix[r.Genre] = r.Id;
        }

        var genre = r?.Genre ?? GenreResponsable.Moniteur;
        if (_genre.SelectedIndex == (genre == GenreResponsable.Classe ? 1 : 0))
        {
            AfficherListe();
        }
        else
        {
            _genre.SelectedIndex = genre == GenreResponsable.Classe ? 1 : 0; // Changement → AfficherListe.
        }
    }

    public void PrendreFocus() => _liste.Focus();

    private void AfficherListe()
    {
        var genre = Genre;
        var liste = _responsables.Where(m => m.Genre == genre).ToList();
        _remplissage = true;
        _liste.Items.Clear();
        foreach (var m in liste)
        {
            _liste.Items.Add(new Choix<long>(m.Id, m.NomComplet));
        }

        var index = _choix.TryGetValue(genre, out var id) ? liste.FindIndex(m => m.Id == id) : -1;
        _liste.SelectedIndex = index >= 0 ? index : liste.Count > 0 ? 0 : -1;
        _remplissage = false;
        _choix[genre] = ResponsableId;
        _liste.AccessibleName = genre == GenreResponsable.Classe ? "Classe responsable" : "Moniteur responsable";
        _liste.AccessibleDescription = liste.Count == 0
            ? genre == GenreResponsable.Classe ? "Aucune classe : ajoutez-en une dans Moniteurs et classes." : "Aucun moniteur : ajoutez-en un dans Moniteurs et classes."
            : null;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var ecart = (int)Dessin.Echelle(this, 8);
        var lb = _genre.GetPreferredSize(Size.Empty).Width;
        _genre.SetBounds(0, 0, lb, Height);
        _liste.SetBounds(lb + ecart, 0, Math.Max(0, Width - lb - ecart), _liste.Height);
    }
}
