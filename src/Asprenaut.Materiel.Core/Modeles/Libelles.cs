using System.Globalization;

namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>Textes français affichés pour les énumérations et les dates.</summary>
public static class Libelles
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Culture);

    public static string DateLongue(DateOnly date) => date.ToString("dddd d MMMM yyyy", Culture);

    public static string De(TypeSortie type) => type switch
    {
        TypeSortie.Piscine => "Piscine",
        TypeSortie.Fosse => "Fosse",
        TypeSortie.Mer => "Mer",
        TypeSortie.Lac => "Lac",
        TypeSortie.Bapteme => "Baptême",
        _ => "Autre",
    };

    public static string De(StatutSortie statut) => statut switch
    {
        StatutSortie.EnPreparation => "En préparation",
        StatutSortie.EnCours => "En cours",
        _ => "Clôturée",
    };

    public static string De(EtatEquipement etat) => etat switch
    {
        EtatEquipement.EnService => "En service",
        EtatEquipement.HorsService => "Hors service",
        _ => "Réformé",
    };

    public static string De(StatutInventaire statut) => statut switch
    {
        StatutInventaire.Disponible => "Disponible",
        StatutInventaire.Dehors => "Dehors",
        StatutInventaire.Manquant => "Manquant",
        StatutInventaire.HorsService => "Hors service",
        _ => "Réformé",
    };

    public static string De(MontageDetendeur montage) => montage == MontageDetendeur.Din ? "DIN" : "Étrier";

    /// <summary>"1 équipement", "3 équipements".</summary>
    public static string Pluriel(int nombre, string singulier, string? pluriel = null) =>
        $"{nombre} {(nombre > 1 ? pluriel ?? singulier + "s" : singulier)}";
}
