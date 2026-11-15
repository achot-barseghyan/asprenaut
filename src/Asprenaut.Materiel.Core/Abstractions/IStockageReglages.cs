using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>Enregistrement des réglages (fichier parametres.json dans l'application).</summary>
public interface IStockageReglages
{
    void Enregistrer(Reglages reglages);
}

/// <summary>Image du logo du club, rangée à côté de la base (« Club\logo.png ») et copiée par la sauvegarde complète.</summary>
public interface IStockageLogo
{
    /// <summary>Chemin du logo enregistré, ou null s'il n'y en a pas.</summary>
    string? Chemin();

    /// <summary>Remplace le logo par cette image PNG (écrite à côté puis mise en place : jamais à moitié écrite).</summary>
    void Enregistrer(byte[] png);

    /// <summary>Supprime le logo enregistré (sans erreur s'il n'y en a pas).</summary>
    void Supprimer();
}
