using Microsoft.Win32;
using System.ServiceProcess;

namespace WebApplication.Services
{
    public static class WindowsServiceHelper
    {
        /// <summary>
        /// Method to detect whether any Windows service is running or not
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns>true/false</returns>

        public static bool IsServiceRunning(string serviceName)
        {
            using (var serviceController = new ServiceController(serviceName))
            {
                return serviceController.Status == ServiceControllerStatus.Running;
            }
        }
    }
}
