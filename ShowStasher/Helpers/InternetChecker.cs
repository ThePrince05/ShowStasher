using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Helpers
{
    // Helpers/InternetChecker.cs
    public static class InternetChecker
    {
        public static bool IsInternetAvailable()
        {
            try
            {
                using var client = new HttpClient();
                var response = client.GetAsync("https://www.google.com").Result;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

}
