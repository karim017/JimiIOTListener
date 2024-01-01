using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class AppConfigSection
    {

        private AppConfigSection()
        {





        }

        public static string IP
        {
            get
            {
                return System.Configuration.ConfigurationManager.AppSettings["IP"].ToString();
            }
        }
        public static int Port { get { return int.Parse(System.Configuration.ConfigurationManager.AppSettings["Port"]); } }
        public static string UserName { get; set; }
        public static string Password { get; set; }
        public static string From { get; set; }
        public static bool EnableSsl { get; set; }
    }
}
