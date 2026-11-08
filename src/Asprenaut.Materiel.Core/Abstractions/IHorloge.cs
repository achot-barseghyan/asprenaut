namespace Asprenaut.Materiel.Core.Abstractions;

/// <summary>Source de l'heure, remplaçable dans les tests.</summary>
public interface IHorloge
{
    DateTime MaintenantUtc { get; }

    /// <summary>Date du jour en heure locale (pour les révisions et les dates de sortie).</summary>
    DateOnly AujourdHui { get; }
}

public sealed class HorlogeSysteme : IHorloge
{
    public DateTime MaintenantUtc => DateTime.UtcNow;

    public DateOnly AujourdHui => DateOnly.FromDateTime(DateTime.Now);
}
