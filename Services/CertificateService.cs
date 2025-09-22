using SnapTunnel.Interfaces;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Net;

namespace SnapTunnel.Services
{
    public class CertificateService : ICertificateService
    {
        public X509Certificate2 GetCertificate(string subject, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.CurrentUser)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var cert = store.Certificates
                .OfType<X509Certificate2>()
                .First(c => c.Subject.Contains(subject, StringComparison.OrdinalIgnoreCase));

            // Exporte en PFX puis réimporte pour garantir la présence de la clé privée
            var pfxBytes = cert.Export(X509ContentType.Pfx);
            var certWithPrivateKey = new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);

            if (!certWithPrivateKey.HasPrivateKey)
            {
                throw new Exception($"Certificate {subject} does not have a private key.");
            }

            return certWithPrivateKey;
        }
        public X509Certificate2 CreateRootCertificate(string subjectName, int keySize = 4096, int validYears = 10)
        {
            using var rsa = RSA.Create(keySize);
            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Authentification du serveur
                    true));

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddYears(validYears);

            var cert = request.CreateSelfSigned(notBefore, notAfter);

            var signedCert = new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable)
            {
                FriendlyName = subjectName // Définit le nom convivial
            };

            return signedCert;
        }

        public X509Certificate2 CreateSignedCertificate(X509Certificate2 issuerCert, string subjectName, IEnumerable<string> domains, int keySize = 2048, int validYears = 2)
        {
            using var rsa = RSA.Create(keySize);
            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Extensions pour un certificat serveur web
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication") }, true));

            // Ajout du SAN (Subject Alternative Name)
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var domain in domains)
            {
                // Add DNS
                sanBuilder.AddDnsName(domain);
            }
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            // Tu peux ajouter d'autres noms ou IP si besoin :
            // sanBuilder.AddDnsName("autre.domaine.com");
            request.CertificateExtensions.Add(sanBuilder.Build());

            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.AddYears(validYears);

            using var issuerPrivateKey = issuerCert.GetRSAPrivateKey();
            var cert = request.Create(
                issuerCert.SubjectName,
                X509SignatureGenerator.CreateForRSA(issuerPrivateKey, RSASignaturePadding.Pkcs1),
                notBefore,
                notAfter,
                GenerateSerialNumber());

            var signedCert = cert.CopyWithPrivateKey(rsa);

            return new X509Certificate2(signedCert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        }

        private static byte[] GenerateSerialNumber()
        {
            var serial = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(serial);
            return serial;
        }

        public bool InstallCertificate(X509Certificate2 certificate, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.CurrentUser)
        {
            try
            {
                using var store = new X509Store(storeName, storeLocation);
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
                store.Close();
            }
            catch (CryptographicException ex) when (ex.HResult == -2147023673) //cancelled operation
            {
                return false;
            }

            return true;
        }

        public bool IsCertificateInstalled(string subjectName, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.CurrentUser)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates
                .OfType<X509Certificate2>()
                .Any(cert => cert.Subject.Contains(subjectName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
