namespace SnapTunnel.Interfaces
{
    public interface IEtcHostService
    {
        bool AddOrUpdateHostEntry(string ipAddress, string domain);
        bool RemoveHostEntry(string domain);
    }
}