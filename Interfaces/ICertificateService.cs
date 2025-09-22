using System.Security.Cryptography.X509Certificates;

namespace SnapTunnel.Interfaces
{
    public interface ICertificateService
    {
        X509Certificate2 GetCertificate(string subject, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.CurrentUser);
        X509Certificate2 CreateRootCertificate(string subjectName, int keySize = 4096, int validYears = 10);
        X509Certificate2 CreateSignedCertificate(X509Certificate2 issuerCert, string subjectName, IEnumerable<string> domains, int keySize = 2048, int validYears = 2);
        bool InstallCertificate(X509Certificate2 certificate, StoreName storeName = StoreName.Root, StoreLocation location = StoreLocation.CurrentUser);
        bool IsCertificateInstalled(string subjectName, StoreName storeName = StoreName.Root, StoreLocation location = StoreLocation.CurrentUser);
    }
}
