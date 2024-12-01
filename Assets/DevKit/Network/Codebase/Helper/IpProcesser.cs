using System.Linq;
using System.Net;

namespace Tfaz.Services
{
    public static class IpProcesser
    {
        /// <summary>
        /// 處理IP字串，如果是無效IP會優先使用defaultIp的值，如果defaultIp也是無效IP，會使用127.0.0.1
        /// </summary>
        /// <param name="validateIp">要處理的IP字串</param>
        /// <param name="defaultIp">預設的IP字串</param>
        /// <returns></returns>
        public static string Process(string validateIp, string defaultIp = "")
        {
            if (ValidateIPv4(validateIp) == false)
            {
                validateIp = defaultIp;
                if (ValidateIPv4(defaultIp) == false)
                {
                    validateIp = "127.0.0.1";
                }
            }
            return validateIp;
        }

        private static bool ValidateIPv4(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            if (ip.Count(c => c == '.') != 3) return false;
            return IPAddress.TryParse(ip, out _);
        }
    }
}