using System.Threading;
using System.Threading.Tasks;

namespace WebApplication
{
    public interface IAntivirusChecker
    {
        Task<bool> IsVirus(byte[] fileBytes, CancellationToken cancellationToken = default);
    }
}
