using System.Configuration;
using System.Data;
using System.Net;
using System.Net.Security;
using System.Windows;


namespace ShowStasher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    /// 
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Enforce modern TLS for all HttpWebRequests
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        }

    }

}
