using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace TestEnvOS.services
{
    public class Usermanager
    {
        public static User CurrentUser = null;
        public static bool IsLoaded
        {
            get
            {
                return CurrentUser != null;
            }
        }
        public enum privilege
        {
            User,
            Privileged,
            Authority,
            System
        }
        public class User
        {
            public string username;
            public privilege privLevel;
        }
    }
}
