using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnapTunnel.Interfaces
{
    public interface IMimeService
    {
        string GetMimeForExtension(string extension);
    }
}
