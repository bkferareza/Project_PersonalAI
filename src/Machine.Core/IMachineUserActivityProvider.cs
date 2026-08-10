namespace Machine.Core;

public interface IMachineUserActivityProvider
{
    Task<MachineUserActivitySnapshot> GetAsync(
        CancellationToken cancellationToken = default);
}
